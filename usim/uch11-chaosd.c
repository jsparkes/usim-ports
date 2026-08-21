/* Connect to a chaos daemon via a Unix socket.
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
#include <pthread.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "uch11.h"
#include "uch11-backend.h"
#include "chaosd.h"
#include "hosttab.h"
#include "misc.h"
#include "ucfg.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

#define UNIX_SOCKET_PATH	"/var/tmp/"
#define UNIX_SOCKET_CLIENT_NAME	"chaosd_"
#define UNIX_SOCKET_SERVER_NAME	"chaosd_server"
#define UNIX_SOCKET_PERM	S_IRWXU

int
chaos_send_to_chaosd(char *buffer, int size)
{
	int wcount, dest_addr;

	/*
	 * Local loopback.
	 */
	if (uch11_csr & CHAOS_CSR_LOOP_BACK) {
		DEBUG(TRACE_CHAOS, "chaos: loopback %d bytes\n", size);
		memcpy(uch11_rcv_buffer, buffer, size);
		uch11_rcv_buffer_size = (size + 1) / 2;
		uch11_rcv_buffer_empty = false;
		uch11_rx_pkt();
		return 0;
	}
	wcount = (size + 1) / 2;
	dest_addr = ((unsigned short *) buffer)[wcount - 3];
	DEBUG(TRACE_CHAOS, "chaos: sending packet to chaosd (dest_addr=%o, uch11_myaddr=%o, size %d, wcount %d)\n", dest_addr, uch11_myaddr, size, wcount);
	/*
	 * Recieve packets addressed to us, but don't receive broadcasts we send
	 */
	if (dest_addr == uch11_myaddr) {
		memcpy(uch11_rcv_buffer, buffer, size);
		uch11_rcv_buffer_size = (size + 1) / 2;
		uch11_rcv_buffer_empty = false;
		uch11_rx_pkt();
	}
	if (chaosd_fd == -1)
		return 0;
	{
		struct iovec iov[2];
		unsigned char lenbytes[4];
		int ret;

		lenbytes[0] = size >> 8;
		lenbytes[1] = size;
		lenbytes[2] = 1;
		lenbytes[3] = 0;
		iov[0].iov_base = lenbytes;
		iov[0].iov_len = 4;
		iov[1].iov_base = buffer;
		iov[1].iov_len = size;
		ret = writev(chaosd_fd, iov, 2);
		if (ret < 0) {
			perror("chaos write");
			return -1;
		}
	}
	return 0;
}

int
chaos_poll_chaosd(void)
{
	ssize_t ret;
	struct pollfd pfd[1];
	int nfds, timeout;
	unsigned char lenbytes[4];
	ssize_t len;
	int dest_addr;

	if (reconnect_chaos == true) {
		uch11_reconnect();
	}
	if (chaosd_fd == -1) {
		return 0;
	}
	timeout = 0;
	nfds = 1;
	pfd[0].fd = chaosd_fd;
	pfd[0].events = POLLIN;
	pfd[0].revents = 0;
	ret = poll(pfd, nfds, timeout);
	if (ret == -1) {
		ERR(TRACE_CHAOS, "chaos: polling chaosd: nothing there (RDN=%o)\n", uch11_csr & CHAOS_CSR_RECEIVE_DONE);
		reconnect_chaos = true;
		return -1;
	} else if (ret == 0) {
		ERR(TRACE_CHAOS, "chaos: timeout\n");
		return -1;
	}
	/*
	 * Is RX buffer full?
	 */
	if (!uch11_rcv_buffer_empty && (uch11_csr & CHAOS_CSR_RECEIVE_DONE)) {
		/*
		 * Toss packets arriving when buffer is already in
		 * use, they will be resent.
		 */
		ERR(TRACE_CHAOS, "chaos: polling chaosd: unread data, drop (RDN=%o, lost %d)\n", uch11_csr & CHAOS_CSR_RECEIVE_DONE, uch11_lost_count);
		uch11_lost_count++;
		ret = read(chaosd_fd, lenbytes, 4);
		if (ret != 4)
			perror("read");
		len = (lenbytes[0] << 8) | lenbytes[1];
		DEBUG(TRACE_CHAOS, "chaos: tossing packet of %d bytes\n", len);
		if (len > (ssize_t) sizeof(uch11_rcv_buffer_toss)) {
			ERR(TRACE_CHAOS, "chaos: packet won't fit (len=%d)", len);
			uch11_force_reconect();
			return -1;
		}
		/*
		 * Toss it...
		 */
		ret = read(chaosd_fd, (char *) uch11_rcv_buffer_toss, len);
		if (ret != len)
			perror("read");
		return -1;
	}
	/*
	 * Read header from chaosd.
	 */
	ret = read(chaosd_fd, lenbytes, 4);
	if (ret <= 0) {
		perror("chaos: header read error");
		uch11_force_reconect();
		return -1;
	}
	len = (lenbytes[0] << 8) | lenbytes[1];
	if (len > (ssize_t) sizeof(uch11_rcv_buffer)) {
		ERR(TRACE_CHAOS, "chaos: packet too big: pkt size %d, buffer size %lu\n", len, sizeof(uch11_rcv_buffer));
		/*
		 * When we get out of synch break socket conn.
		 */
		uch11_force_reconect();
		return -1;
	}
	ret = read(chaosd_fd, (char *) uch11_rcv_buffer, len);
	if (ret == -1) {
		perror("chaos: read");
		uch11_force_reconect();
		return -1;
	} else if (ret == 0) {
		ERR(TRACE_CHAOS, "chaos: read zero bytes\n");
		return -1;
	}
	DEBUG(TRACE_CHAOS, "chaos: polling; got chaosd packet %d\n", ret);
	uch11_rcv_buffer_size = (ret + 1) / 2;
	uch11_rcv_buffer_empty = false;
	dest_addr = uch11_rcv_buffer[uch11_rcv_buffer_size - 3];
	/*
	 * If not to us, ignore.
	 */
	if (dest_addr != uch11_myaddr) {
		DEBUG(TRACE_CHAOS, "chaos: ignoring packet not to us (%o): %o\n", uch11_myaddr, dest_addr);
		uch11_rcv_buffer_size = 0;
		uch11_rcv_buffer_empty = true;
		/*
		 * Should uch11_rx_pkt be called here?
		 */
		return 0;
	}
	DEBUG(TRACE_CHAOS, "chaos: recieving packet: from %o, my %o\n", dest_addr, uch11_myaddr);
	uch11_rx_pkt();
	return 0;
}
