/* Chaos over UDP */

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
#include <errno.h>

#include "uch11.h"
#include "uch11-backend.h"
#include "chaosd.h"
#include "hosttab.h"
#include "misc.h"
#include "ucfg.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

int hybrid_udp_and_local = 0;
u_short udp_bridge_chaddr;

extern int chaos_send_to_local(char *buffer, int size);
extern int chaos_poll_local(void);
extern unsigned short uch11_checksum(const unsigned char *addr, int count);

int
chudpopen(void)
{
	int sock, lport, res, udp_dport, braddr;
	struct sockaddr_in sin;
	struct addrinfo *he, hi;
	struct in_addr udp_dest;

	if (hybrid_udp_and_local) {
		if (uch11_serveraddr == 0) {
			ERR(TRACE_CHAOS, "You configured udp_local_hybrid but there is no server address! Disabling hybrid.\n");
			hybrid_udp_and_local = 0;
		} else {
			/* Do the local init too */
			TAILQ_INIT(&queuehead);
			pthread_mutex_init(&recvqueue, NULL);
		}
	}

	/* Parse the local port given */
	if ((ucfg.chaos_bridgeport_local == NULL) || (ucfg.chaos_bridgeport_local[0] == '\0') || ((lport = atoi(ucfg.chaos_bridgeport_local)) <= 0) || (lport >= (1 << 16))) {
		/* Or default to 42042? */
		err(1, "bad bridgeport_local");
	}
	/* Parse the bridge port given */
	if ((ucfg.chaos_bridgeport == NULL) || (ucfg.chaos_bridgeport[0] == '\0') || ((udp_dport = atoi(ucfg.chaos_bridgeport)) <= 0) || (udp_dport >= (1 << 16))) {
		/* Or default to 42042? */
		err(1, "bad bridgeport");
	}
	/* Parse the bridge address given */
	if ((ucfg.chaos_bridgeip != NULL) && (ucfg.chaos_bridgeip[0] != '\0')) {
		/* @@@@ allow IPv6 */
		/* Check if it is an explicit IPv4 address */
		if (inet_aton(ucfg.chaos_bridgeip, &udp_dest) == 0) {
			/* Else try to parse a host name */
			memset(&hi, 0, sizeof(hi));
			hi.ai_family = AF_INET;
			hi.ai_flags = AI_ADDRCONFIG;
			if ((res = getaddrinfo(ucfg.chaos_bridgeip, NULL, &hi, &he)) == 0) {
				struct sockaddr_in *s = (struct sockaddr_in *) he->ai_addr;
				memcpy(&udp_dest.s_addr, (u_char *) & s->sin_addr, 4);
			} else {
				err(1, "bad bridgeip");
			}
		}
	} else {
		err(1, "no bridge?");
	}
	/* Parse bridge Chaos address */
	if ((ucfg.chaos_bridgechaos == NULL) || (ucfg.chaos_bridgechaos[0] == '\0') | (sscanf(ucfg.chaos_bridgechaos, "%o", &braddr) != 1) ||
	    /* Check it's a valid address */
	    (braddr == 0) || (braddr >= (1 << 16)) || ((braddr & 0xff) == 0) || (((braddr >> 8) & 0xff) == 0)) {
		err(1, "bad bridgechaos");
	} else {
		udp_bridge_chaddr = (braddr & 0xffff);
	}

	NOTICE(TRACE_USIM, "chaos: chudp init, bridge is %#o at %s:%d, local port %d, hybrid %s\n", udp_bridge_chaddr, inet_ntoa(udp_dest), udp_dport, lport, hybrid_udp_and_local ? "true" : "false");

	/* Now create a socket, and bind it to our local port */
	if ((sock = socket(AF_INET, SOCK_DGRAM, 0)) < 0) {
		perror("socket failed");
		exit(1);
	}
	sin.sin_family = AF_INET;
	sin.sin_port = htons(lport);
	sin.sin_addr.s_addr = INADDR_ANY;
	if (bind(sock, (struct sockaddr *) &sin, sizeof(sin)) < 0) {
		perror("udp bind failed");
		exit(1);
	}
	/* and then connect it to the remote address - since we're only using one and the same */
	sin.sin_port = htons(udp_dport);
	memcpy(&sin.sin_addr.s_addr, &udp_dest.s_addr, 4);
	if (connect(sock, (struct sockaddr *) &sin, sizeof(sin)) < 0) {
		perror("udp connect failed");
		exit(1);
	}
	return sock;
}

/* from chudp.h and cbridge-chaos.h */

// Max pkt size (12 bits) plus header
// The limit of 488 bytes is from MIT AIM 628, although more would fit any modern pkt (and 12 bits would give 4096 as max).
// This is due to original Chaos hardware pkts limited to 4032 bits, of which 16 bytes are header.
#define CH_PK_MAX_DATALEN 488

/* Protocol version */
#define CHUDP_VERSION 1
/* Protocol function codes */
#define CHUDP_PKT 1		/* Chaosnet packet */

struct chudp_header
{
	char chudp_version;
	char chudp_function;
	char chudp_arg1;
	char chudp_arg2;
};

struct chaos_hw_trailer
{
	unsigned short ch_hw_destaddr:16;
	unsigned short ch_hw_srcaddr:16;
	unsigned short ch_hw_checksum:16;
};

/* Max: CHUDP header, Chaos header, Chaos data, trailer */
#define CHUDP_MAXLEN (sizeof(struct chudp_header)+sizeof(struct pkt_header)+CH_PK_MAX_DATALEN+sizeof(struct chaos_hw_trailer))

u_char trans_chudpbuf[CHUDP_MAXLEN];

/* So sorry about this. */
static void
ntohs_buf(u_short *ibuf, u_short *obuf, int len)
{
	int i;
	for (i = 0; i < len; i += 2)
		*obuf++ = ntohs(*ibuf++);
}

int
chaos_send_to_udp(char *buffer, int size)
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
	if (hybrid_udp_and_local) {
		/* Check if it is for our "local server" */
		struct packet *packet;
		packet = (struct packet *) buffer;
		if (CH_ADDR_SHORT(packet->pk_daddr) == chaos_addr(ucfg.chaos_servername, 0)) {
			return chaos_send_to_local(buffer, size);
		}
	}
	wcount = (size + 1) / 2;
	dest_addr = ((unsigned short *) buffer)[wcount - 3];
	DEBUG(TRACE_CHAOS, "chaos: sending packet to udp (dest_addr=%o, uch11_myaddr=%o, size %d, wcount %d)\n", dest_addr, uch11_myaddr, size, wcount);
	if (size > (int) CHUDP_MAXLEN) {
		ERR(TRACE_CHAOS, "chaos: packet too long: %d", size);
		return -1;
	}
	/*
	 * Receive packets addressed to us, or broadcasts.
	 */
	if ((dest_addr == uch11_myaddr) || (dest_addr == 0)) {
		memcpy(uch11_rcv_buffer, buffer, size);
		uch11_rcv_buffer_size = (size + 1) / 2;
		uch11_rcv_buffer_empty = false;
		uch11_rx_pkt();
		if (dest_addr != 0)	/* Broadcasts should be sent also to other */
			return 0;
	}
	if (chaosd_fd == -1) {
		ERR(TRACE_CHAOS, "chaos: transmit but chaosd_fd not open!\n");
		return 0;
	}
	{
		struct chudp_header *hp = (struct chudp_header *) &trans_chudpbuf;
		u_char *op = trans_chudpbuf + sizeof(struct chudp_header);
		int nb;

		memset(trans_chudpbuf, 0, sizeof(trans_chudpbuf));
		/* Set up CHUDP header */
		hp->chudp_version = CHUDP_VERSION;
		hp->chudp_function = CHUDP_PKT;

		memcpy(op, buffer, size);

		/* Update the hw trailer dest (and checksum) since what is there is probably the ultimate dest,
		 * but it should be just the next hop */
		struct pkt_header *ph = (struct pkt_header *) ((char *) op);
		u_short pklen = LENFC_LEN(ph->ph_lenfc);
		u_short offs = sizeof(struct pkt_header) + pklen;
		if (offs % 2)
			offs++;
		struct chaos_hw_trailer *tp = (struct chaos_hw_trailer *) (op + offs);
		u_short hwdest = tp->ch_hw_destaddr;
		// u_short cksum = tp->ch_hw_checksum;
		if (hwdest != udp_bridge_chaddr) {
			INFO(TRACE_CHAOS, "chaos: hw trailer dest is %#o should be %#o\n", hwdest, udp_bridge_chaddr);
			tp->ch_hw_destaddr = udp_bridge_chaddr;
		}
		tp->ch_hw_checksum = htons(uch11_checksum(op, size - 2));

		/* Now swap it */
		ntohs_buf((u_short *) op, (u_short *) op, size);

		INFO(TRACE_CHAOS, "chaos: sending %d bytes (pkt size %d)\n", size + sizeof(struct chudp_header), size);
		if ((nb = send(chaosd_fd, (char *) hp, size + sizeof(struct chudp_header), 0)) < 0) {
			if ((errno != EHOSTUNREACH) && (errno != ENETDOWN) && (errno != ENETUNREACH))
				perror("chaos: send to udp");
			// ERR(TRACE_CHAOS, "chaos: send to udp failed\n");
			return -1;
		} else if (nb != size + (int)sizeof(struct chudp_header)) {
			ERR(TRACE_CHAOS, "chaos: could not send the full pkt: %d sent, expected %d\n", nb, size + sizeof(struct chudp_header));
			return -1;
		}
	}
	return 0;
}

int
chaos_poll_udp(void)
{
	/* basically copy chaos_poll_chaosd but skip CHUDP header and swap */
	ssize_t ret;
	struct pollfd pfd[1];
	int nfds, timeout;
	// unsigned char lenbytes[4];
	// ssize_t len;
	int dest_addr;

	if (hybrid_udp_and_local) {
		/* Prioritize local traffic */
		chaos_poll_local();
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
		ERR(TRACE_CHAOS, "chaos: polling udp: nothing there (RDN=%o)\n", uch11_csr & CHAOS_CSR_RECEIVE_DONE);
		return -1;
	} else if (ret == 0) {
		// this happens all the time so don't
		// ERR(TRACE_CHAOS, "chaos: udp poll timeout\n");
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
		//ERR(TRACE_CHAOS, "chaos: polling udp: unread data, drop (RDN=%o, lost %d)\n", uch11_csr & CHAOS_CSR_RECEIVE_DONE, uch11_lost_count);
		uch11_lost_count++;
		/* Toss it by reading it */
		ret = recv(chaosd_fd, (char *) uch11_rcv_buffer_toss, sizeof(uch11_rcv_buffer_toss), 0);
		DEBUG(TRACE_CHAOS, "chaos: tossing udp packet of %d bytes\n", ret);
		return -1;
	}
	ret = recv(chaosd_fd, (char *) uch11_rcv_buffer, sizeof(uch11_rcv_buffer), 0);
	if (ret == -1) {
		perror("chaos: udp read");
		return -1;
	} else if (ret == 0) {
		ERR(TRACE_CHAOS, "chaos: udp read zero bytes\n");
		return -1;
	} else if (ret > (int) CHUDP_MAXLEN) {
		ERR(TRACE_CHAOS, "chaos: udp read too many bytes: %d\n", ret);
		return -1;
	}
	INFO(TRACE_CHAOS, "chaos: polling; got udp packet len %d\n", ret);
	struct chudp_header *hp = (struct chudp_header *) uch11_rcv_buffer;
	if (hp->chudp_version != CHUDP_VERSION) {
		ERR(TRACE_CHAOS, "chaos: chudp version is wrong: %d\n", hp->chudp_version);
		return -1;
	}
	if (hp->chudp_function != CHUDP_PKT) {
		ERR(TRACE_CHAOS, "chaos: chudp function is wrong: %d\n", hp->chudp_function);
		return -1;
	}
	/* zap chudp header */
	memmove((char *) uch11_rcv_buffer, ((char *) uch11_rcv_buffer) + sizeof(struct chudp_header), ret - sizeof(struct chudp_header));
	ret -= sizeof(struct chudp_header);

	/* and swap it */
	ntohs_buf(uch11_rcv_buffer, uch11_rcv_buffer, ret);

	uch11_rcv_buffer_size = (ret + 1) / 2;
	uch11_rcv_buffer_empty = false;
	dest_addr = uch11_rcv_buffer[uch11_rcv_buffer_size - 3];
	/*
	 * If not to us (or broadcast), ignore.
	 */
	if ((dest_addr != uch11_myaddr) && (dest_addr != 0)) {
		INFO(TRACE_CHAOS, "chaos: ignoring packet not to us (%o): %o\n", uch11_myaddr, dest_addr);
		uch11_rcv_buffer_size = 0;
		uch11_rcv_buffer_empty = true;
		/*
		 * Should uch11_rx_pkt be called here?
		 */
		return 0;
	}
	INFO(TRACE_CHAOS, "chaos: udp receiving packet: from %o, my %o\n", dest_addr, uch11_myaddr);
	uch11_rx_pkt();
	return 0;
}
