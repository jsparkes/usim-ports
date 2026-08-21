/* uch11.c --- chaosnet interface
 */

#include <sys/poll.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <sys/types.h>
#include <sys/uio.h>
#include <sys/un.h>

#include <netinet/in.h>

#include <sys/types.h>
#include <sys/socket.h>
#include <netdb.h>
#include <arpa/inet.h>

#include <err.h>
#include <limits.h>
#include <pthread.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "chaosd.h"
#include "hosttab.h"
#include "idle.h"
#include "misc.h"
#include "uch11.h"
#include "uch11-backend.h"
#include "ucfg.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

int uch11_backend = UCH11_BACKEND_LOCAL;
int uch11_myaddr = 0177040;	/* LOCAL-CADR */
int uch11_serveraddr = 0177001;	/* LOCAL-BRIDGE */

int uch11_csr;
int uch11_bit_count;
int uch11_lost_count;

unsigned short uch11_xmit_buffer[4096];
int uch11_xmit_buffer_size;
int uch11_xmit_buffer_ptr;

unsigned short uch11_rcv_buffer[4096];
unsigned short uch11_rcv_buffer_toss[4096];
int uch11_rcv_buffer_ptr;
int uch11_rcv_buffer_size;
bool uch11_rcv_buffer_empty;

int reconnect_delay;
bool reconnect_chaos;

int (*uch11_send)(char *, int);

void uch11_force_reconect(void);
extern void uch11_reconnect(void);

extern void settreeroot(const char *, const char *prefix);

pthread_mutex_t recvqueue;
pthread_mutex_t recvqueue;
struct queue_head queuehead = TAILQ_HEAD_INITIALIZER(queuehead);

// udp

int chudpopen(void);
int chaos_send_to_udp(char *buffer, int size);
int chaos_poll_udp(void);

// deamon
int chaosd_fd;

int chaos_send_to_chaosd(char *buffer, int size);
int chaos_poll_chaosd(void);

// local

int chaos_send_to_local(char *buffer, int size);
int chaos_poll_local(void);

/*
 * RFC1071: Compute Internet Checksum for COUNT bytes beginning at
 * location ADDR.
 */
unsigned short
uch11_checksum(const unsigned char *addr, int count)
{
	long sum;

	sum = 0;
	while (count > 1) {
		sum += *(addr) << 8 | *(addr + 1);
		addr += 2;
		count -= 2;
	}
	/*
	 * Add left-over byte, if any.
	 */
	if (count > 0)
		sum += *(unsigned char *) addr;
	/*
	 * Fold 32-bit sum to 16 bits.
	 */
	while (sum >> 16)
		sum = (sum & 0xffff) + (sum >> 16);
	return (~sum) & 0xffff;
}

/*
 * Called when we are we have something in uch11_rcv_buffer.
 */
void
uch11_rx_pkt(void)
{
    idle_chaos_activity();
	uch11_rcv_buffer_ptr = 0;
	uch11_bit_count = (uch11_rcv_buffer_size * 2 * 8) - 1;
	INFO(TRACE_CHAOS, "chaos: receiving %d words\n", uch11_rcv_buffer_size);
	if (uch11_rcv_buffer_size > 0) {
		DEBUG(TRACE_CHAOS, "chaos: set csr receive done, generate interrupt\n");
		uch11_csr |= CHAOS_CSR_RECEIVE_DONE;
		if (uch11_csr & CHAOS_CSR_RECEIVE_ENABLE)
			assert_unibus_interrupt(0270);
	} else {
		DEBUG(TRACE_CHAOS, "chaos: recieve buffer empty\n");
	}
}

void
uch11_xmit_done_intr(void)
{
	uch11_csr |= CHAOS_CSR_TRANSMIT_DONE;
	if (uch11_csr & CHAOS_CSR_TRANSMIT_ENABLE)
		assert_unibus_interrupt(0270);
}

void
uch11_xmit_pkt(void)
{
    idle_chaos_activity();
	INFO(TRACE_CHAOS, "chaos: transmitting %d bytes, data len %d\n", uch11_xmit_buffer_ptr * 2, (uch11_xmit_buffer_ptr > 0 ? uch11_xmit_buffer[1] & 0x3f : -1));
	uch11_xmit_buffer_size = uch11_xmit_buffer_ptr;
	/*
	 * Dest. is already in the buffer.
	 */
	uch11_xmit_buffer[uch11_xmit_buffer_size++] = (unsigned short) uch11_myaddr;	/* Source. */
	uch11_xmit_buffer[uch11_xmit_buffer_size] = uch11_checksum((unsigned char *) uch11_xmit_buffer, uch11_xmit_buffer_size * 2);	/* Checksum. */
	uch11_xmit_buffer_size++;
	(*uch11_send) ((char *) uch11_xmit_buffer, uch11_xmit_buffer_size * 2);
	uch11_xmit_buffer_ptr = 0;
	uch11_xmit_done_intr();
}

int
uch11_get_bit_count(void)
{
	if (uch11_rcv_buffer_size > 0)
		return uch11_bit_count;
	DEBUG(TRACE_CHAOS, "chaos: returned empty bit count\n");
	return 07777;
}

int
uch11_get_rcv_buffer(void)
{
	if (uch11_rcv_buffer_ptr < uch11_rcv_buffer_size) {
		int ret;

		ret = uch11_rcv_buffer[uch11_rcv_buffer_ptr++];
		if (uch11_rcv_buffer_ptr == uch11_rcv_buffer_size) {
			DEBUG(TRACE_CHAOS, "chaos: marked recieve buffer as empty\n");
			uch11_rcv_buffer_empty = true;
		}
		return ret;
	}
	/*
	 * Read last word, clear receive done.
	 */
	DEBUG(TRACE_CHAOS, "chaos: cleared csr receive done\n");
	uch11_csr &= ~CHAOS_CSR_RECEIVE_DONE;
	uch11_rcv_buffer_size = 0;
	return 0;
}

void
uch11_put_xmit_buffer(int v)
{
	if (uch11_xmit_buffer_ptr < (int) sizeof(uch11_xmit_buffer) / 2)
		uch11_xmit_buffer[uch11_xmit_buffer_ptr++] = (unsigned short) v;
	uch11_csr &= ~CHAOS_CSR_TRANSMIT_DONE;
}

int
uch11_get_csr(void)
{
	static int old_csr = 0;

	if (uch11_csr != old_csr) {
		DEBUG(TRACE_CHAOS, "chaos: read csr %o\n", uch11_csr);
		old_csr = uch11_csr;
	}
	return uch11_csr | ((uch11_lost_count << 9) & 017);
}

void
uch11_set_csr(int v)
{
	int mask;
	int old_csr;

	old_csr = uch11_csr;
	v &= 0xffff;
	/*
	 * Writing these don't stick.
	 */
	/* *INDENT-OFF* */
	mask =
		CHAOS_CSR_TRANSMIT_DONE |
		CHAOS_CSR_LOST_COUNT |
		CHAOS_CSR_CRC_ERROR |
		CHAOS_CSR_RECEIVE_DONE |
		CHAOS_CSR_RECEIVER_CLEAR;
	/* *INDENT-ON* */
	uch11_csr = (uch11_csr & mask) | (v & ~mask);
	if (uch11_csr & CHAOS_CSR_RESET) {
		uch11_rcv_buffer_ptr = 0;
		uch11_rcv_buffer_size = 0;
		uch11_xmit_buffer_ptr = 0;
		uch11_lost_count = 0;
		uch11_bit_count = 0;
		uch11_csr &= ~(CHAOS_CSR_RESET | CHAOS_CSR_RECEIVE_DONE);
		uch11_csr |= CHAOS_CSR_TRANSMIT_DONE;
		reconnect_delay = 200;	/* Do it right away. */
		uch11_force_reconect();
	}
	if (v & CHAOS_CSR_RECEIVER_CLEAR) {
		uch11_rcv_buffer_ptr = 0;
		uch11_rcv_buffer_size = 0;
		uch11_lost_count = 0;
		uch11_bit_count = 0;
		uch11_csr &= ~CHAOS_CSR_RECEIVE_DONE;
	}
	if (v & (CHAOS_CSR_TRANSMITTER_CLEAR | CHAOS_CSR_TRANSMIT_DONE)) {
		uch11_xmit_buffer_ptr = 0;
		uch11_csr &= ~CHAOS_CSR_TRANSMIT_ABORT;
		uch11_csr |= CHAOS_CSR_TRANSMIT_DONE;
	}
	if (uch11_csr & CHAOS_CSR_RECEIVE_ENABLE) {
		if ((old_csr & CHAOS_CSR_RECEIVE_ENABLE) == 0)
        {
			DEBUG(TRACE_CHAOS, "chaos: CSR receive enable\n");
        }
		if (uch11_rcv_buffer_empty) {
			uch11_rcv_buffer_ptr = 0;
			uch11_rcv_buffer_size = 0;
		}
		/*
		 * If buffer is full, generate status and interrupt again.
		 */
		if (uch11_rcv_buffer_size > 0) {
			DEBUG(TRACE_CHAOS, "chaos: rx-enabled and buffer is full\n");
			uch11_rx_pkt();
		}
	} else if (old_csr & CHAOS_CSR_RECEIVE_ENABLE)
    {
		DEBUG(TRACE_CHAOS, "chaos: CSR receive DISable\n");
    }
	if (uch11_csr & CHAOS_CSR_TRANSMIT_ENABLE) {
		if ((old_csr & CHAOS_CSR_TRANSMIT_ENABLE) == 0)
        {
			DEBUG(TRACE_CHAOS, "chaos: CSR transmit enable\n");
        }
		uch11_csr |= CHAOS_CSR_TRANSMIT_DONE;
	} else if (old_csr & CHAOS_CSR_TRANSMIT_ENABLE)
    {
		DEBUG(TRACE_CHAOS, "chaos: CSR transmit DISable\n");
    }
	DEBUG(TRACE_CHAOS, "chaos: set csr bits 0%o, old 0%o, new 0%o\n", v, old_csr, uch11_csr);
}

void
uch11_poll(void)
{
	if (uch11_backend == UCH11_BACKEND_LOCAL)
		chaos_poll_local();
	else if (uch11_backend == UCH11_BACKEND_DAEMON)
		chaos_poll_chaosd();
	else if (uch11_backend == UCH11_BACKEND_UDP)
		chaos_poll_udp();
}

void
uch11_force_reconect(void)
{
	if (uch11_backend == UCH11_BACKEND_DAEMON) {
		WARNING(TRACE_CHAOS, "chaos: forcing reconnect to chaosd\n");
		close(chaosd_fd);
		chaosd_fd = -1;
		reconnect_chaos = true;
	}
}

void
uch11_reconnect(void)
{
	static int reconnect_time;

	if (++reconnect_delay < 200)
		return;
	reconnect_delay = 0;
	if (reconnect_time && time(NULL) < (reconnect_time + 5))	/* Try every 5 seconds. */
		return;
	reconnect_time = time(NULL);
	NOTICE(TRACE_CHAOS, "chaos: reconnecting to chaosd\n");
	if (uch11_init() == 0) {
		INFO(TRACE_CHAOS, "chaos: chaosd reconnected\n");
		reconnect_chaos = false;
		reconnect_delay = 0;
	}
}

int
uch11_init(void)
{
	char hosts_file[PATH_MAX] = {0};
    char root_directory[PATH_MAX] = {0};

	if (realpath(ucfg.chaos_hosts, hosts_file) == NULL)
    {
        strcpy(hosts_file, "hosts.text");
		WARNING(TRACE_USIM, "chaos: no host table; using defaults\n");
	}
	NOTICE(TRACE_USIM, "chaos: using hosts table from \"%s\"\n", hosts_file);
	readhosts(ucfg.chaos_myname, hosts_file);
	uch11_myaddr = chaos_addr(ucfg.chaos_myname, 0);
	uch11_serveraddr = chaos_addr(ucfg.chaos_servername, 0);
	NOTICE(TRACE_USIM, "chaos: I am %s (0%o)\n", ucfg.chaos_myname, uch11_myaddr);
	if (uch11_backend == UCH11_BACKEND_LOCAL) {
		NOTICE(TRACE_USIM, "chaos: backend is \"local\", connecting to %s (0%o)\n", ucfg.chaos_servername, uch11_serveraddr);
		TAILQ_INIT(&queuehead);
		uch11_send = &chaos_send_to_local;
		pthread_mutex_init(&recvqueue, NULL);
	} else if (uch11_backend == UCH11_BACKEND_DAEMON) {
		NOTICE(TRACE_USIM, "chaos: backend is \"chaosd\"\n");
		uch11_send = &chaos_send_to_chaosd;
		chaosd_fd = chdopen();
		if (chaosd_fd == -1) {
			close(chaosd_fd);
			return -1;
		}
	} else if (uch11_backend == UCH11_BACKEND_UDP) {
		if (hybrid_udp_and_local)
			NOTICE(TRACE_USIM, "chaos: backend is \"udp\" with server %s (%#o)\n", ucfg.chaos_servername, uch11_serveraddr);
		else
			NOTICE(TRACE_USIM, "chaos: backend is \"udp\"\n");
		uch11_send = &chaos_send_to_udp;
		chaosd_fd = chudpopen();
		if (chaosd_fd == -1) {
			close(chaosd_fd);
			return -1;
		}
	}
	uch11_rcv_buffer_empty = true;
	char *whichconf, *whichdir;
	if (ucfg.usim_sys_directory != NULL) {
		// Backwards compat
		whichconf = "sys_directory";
		whichdir = "/tree";
		if (realpath(ucfg.usim_sys_directory, root_directory) != NULL)
			settreeroot(root_directory, whichdir);
	} else {
		whichconf = "fs_root_directory";
		whichdir = "/";
		if (realpath(ucfg.usim_fs_root_directory, root_directory) != NULL)
			settreeroot(root_directory, NULL);
	}
	if (root_directory[0] == 0)
		err(1, "could not resolve %s", whichconf);
	NOTICE(TRACE_USIM, "chaos: mapping %s to %s\n", whichdir, root_directory);
	return 0;
}

//#include "uch11-chaosd.c"
//#include "uch11-local.c"
//#include "uch11-udp.c"
