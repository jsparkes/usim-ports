/* Local Chaos -- this should go into chlib.c
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

static void
chaos_queue(struct packet *packet)
{
	struct packet_queue *node;

	node = malloc(sizeof(struct packet_queue));
	node->packet = packet;
	pthread_mutex_lock(&recvqueue);
	TAILQ_INSERT_TAIL(&queuehead, node, next);
	pthread_mutex_unlock(&recvqueue);
}

int
chaos_connection_queue(struct connection *conn, struct packet *packet)
{
	struct packet_queue *node;
	unsigned short nextpacket;

	INFO(TRACE_CHAOS, "chaos: sending packet (0%o) to 0%o from 0%o (length: %d)\n", packet->pk_op, CH_ADDR_SHORT(packet->pk_daddr), CH_ADDR_SHORT(packet->pk_saddr), PH_LEN(packet->pk_phead));
	if (trace_level == LOG_DEBUG && (trace_facilities & TRACE_CHAOS)) {
		dumpmem(packet->pk_cdata, PH_LEN(packet->pk_phead));
	}
	/*
	 * Any packet for remote gets queued.
	 */
	if (CH_ADDR_SHORT(packet->pk_daddr) == CH_ADDR_SHORT(conn->cn_faddr)) {
		for (;;) {
			if (chtfull(conn) && packet->pk_op != STSOP) {
				struct timespec ts;

				DEBUG(TRACE_CHAOS, "chaos: waiting for remote ack packet=%d (cn_tlast = %d cn_tacked = %d twsize = %d)\n", packet->pk_pkn, conn->cn_tlast, conn->cn_tacked, conn->cn_twsize);
				pthread_mutex_lock(&conn->twsem);
				clock_gettime(CLOCK_REALTIME, &ts);
				ts.tv_sec += 5;
				if (pthread_cond_timedwait(&conn->twcond, &conn->twsem, &ts)) {
					if (conn->lastpacket) {
						struct packet *retransmit;

						DEBUG(TRACE_CHAOS, "chaos: re-transmit last packet\n");
						retransmit = conn->lastpacket;
						conn->lastpacket = 0;
						chaos_queue(retransmit);
					}
				}
				pthread_mutex_unlock(&conn->twsem);
			} else
				break;
		}
		if (packet->pk_op != STSOP && CH_INDEX_SHORT(packet->pk_didx) == CH_INDEX_SHORT(conn->cn_fidx) && cmp_gt(packet->pk_pkn, conn->cn_tlast))
			conn->cn_tlast = packet->pk_pkn;
		conn->lastpacket = packet;
		chaos_queue(packet);
		return 0;
	}
	node = malloc(sizeof(struct packet_queue));
	node->packet = packet;
	nextpacket = conn->cn_rlast + 1;
	if (cmp_gt(packet->pk_pkn, nextpacket)) {
		DEBUG(TRACE_CHAOS, "chaos: queuing out-of-order packet: nextpacket=%d packet=%d\n", nextpacket, packet->pk_pkn);
		pthread_mutex_lock(&conn->queuelock);
		TAILQ_INSERT_TAIL(&conn->orderhead, node, next);
		pthread_mutex_unlock(&conn->queuelock);
		return 0;
	}
	{
		pthread_mutex_lock(&conn->queuelock);
		TAILQ_INSERT_TAIL(&conn->queuehead, node, next);
		pthread_mutex_unlock(&conn->queuelock);
	}
	pthread_mutex_lock(&conn->queuesem);
	pthread_cond_signal(&conn->queuecond);
	pthread_mutex_unlock(&conn->queuesem);
	return 0;
}

struct packet *
chaos_connection_dequeue(struct connection *conn)
{
	struct packet *packet;
	struct packet_queue *node;
#if 0
	unsigned short nextpacket;
#endif

	packet = NOPKT;
	node = 0;
#if 0
	nextpacket = conn->cn_rlast + 1;
#endif
	if (conn->cn_state == CSCLOSED)
		return NOPKT;
	for (;;) {
		pthread_mutex_lock(&conn->queuelock);
		if (TAILQ_EMPTY(&conn->orderhead) == false) {
#if 0
			struct packet_queue *prev;

			for (node = conn->orderhead; node; prev = node, node = node->next)
				if (node->packet->pk_pkn == nextpacket) {
					if (prev == 0)
						conn->orderhead = node->next;
					else
						prev->next = node->next;
					if (conn->ordertail == node)
						conn->ordertail = prev;
					packet = node->packet;
					break;
				}
#endif
#if 0
			TAILQ_FOREACH(node, &conn->orderhead, next) {
				if (node->packet->pk_pkn == nextpacket) {
					DEBUG(TRACE_CHAOS, "chaos: dequeued out-of-order packet %d\n", nextpacket);
					/*
					 * ---!! magic
					 */
					break;
				}
			}
#else
			DEBUG(TRACE_CHAOS, "chaos: ordered queue empty\n");
#endif
		}
		if (node == 0 && TAILQ_EMPTY(&conn->queuehead) == false) {
			node = TAILQ_FIRST(&conn->queuehead);
			packet = node->packet;
			TAILQ_REMOVE(&conn->queuehead, node, next);
		}
		pthread_mutex_unlock(&conn->queuelock);
		if (node) {
			free(node);
			node = 0;
		}
		if (packet) {
			if (cmp_gt(packet->pk_pkn, conn->cn_rlast))
				conn->cn_rlast = packet->pk_pkn;
			conn->cn_tacked = packet->pk_ackn;
			if (3 * (short) (conn->cn_rlast - conn->cn_racked) > conn->cn_rwsize) {
				struct packet *status;

				status = chaos_allocate_packet(conn, STSOP, 2 * sizeof(unsigned short));
				conn->cn_racked = conn->cn_rlast;
				*(unsigned short *) &status->pk_cdata[0] = conn->cn_rlast;
				*(unsigned short *) &status->pk_cdata[2] = conn->cn_rwsize;
				chaos_queue(status);
			}
			return packet;
		}
		pthread_mutex_lock(&conn->queuesem);
		pthread_cond_wait(&conn->queuecond, &conn->queuesem);
		pthread_mutex_unlock(&conn->queuesem);
		if (conn->cn_state == CSCLOSED)
			return NOPKT;
	}
}

static int
chaos_queue_time_pkt(unsigned short saddr, unsigned short sidx)
{
	time_t t;
	struct timeval time;
	struct packet *answer;

	INFO(TRACE_CHAOS, "chaos: RFC (TIME): answering...\n");
	gettimeofday(&time, NULL);
	t = time.tv_sec;
	t += 60UL * 60 * 24 * ((1970 - 1900) * 365L + 1970 / 4 - 1900 / 4);
	answer = pkalloc(sizeof(long), 0);
	answer->pk_op = ANSOP;
	SET_PH_LEN(answer->pk_phead, sizeof(long));
	SET_CH_ADDR(answer->pk_daddr, saddr);
	SET_CH_INDEX(answer->pk_didx, sidx);
	SET_CH_ADDR(answer->pk_saddr, chaos_addr(ucfg.chaos_servername, 0));
	SET_CH_INDEX(answer->pk_sidx, 0);
	answer->pk_pkn = 0;
	answer->pk_ackn = 0;
	*(long *) &answer->pk_cdata[0] = t;
	chaos_queue(answer);
	return 0;
}

static int
chaos_queue_uptime_pkt(unsigned short saddr, unsigned short sidx)
{
	struct packet *answer;

	INFO(TRACE_CHAOS, "chaos: RFC (UPTIME): answering...\n");
	answer = pkalloc(sizeof(long), 0);
	answer->pk_op = ANSOP;
	SET_PH_LEN(answer->pk_phead, sizeof(long));
	SET_CH_ADDR(answer->pk_daddr, saddr);
	SET_CH_INDEX(answer->pk_didx, sidx);
	SET_CH_ADDR(answer->pk_saddr, chaos_addr(ucfg.chaos_servername, 0));
	SET_CH_INDEX(answer->pk_sidx, 0);
	answer->pk_pkn = 0;
	answer->pk_ackn = 0;
	*(long *) &answer->pk_cdata[0] = 0;
	chaos_queue(answer);
	return 0;
}

static int
chaos_queue_status_pkt(unsigned short saddr, unsigned short sidx)
{
	struct packet *answer;
	struct status *status;

	INFO(TRACE_CHAOS, "chaos: RFC (STATUS): answering...\n");
	answer = pkalloc(CHSTATNAME + 2 * 2, 0);	/* Only room for name and subnet+nwords */
	answer->pk_op = ANSOP;
	SET_PH_LEN(answer->pk_phead, CHSTATNAME + 2 * 2);
	SET_CH_ADDR(answer->pk_daddr, saddr);
	SET_CH_INDEX(answer->pk_didx, sidx);
	SET_CH_ADDR(answer->pk_saddr, chaos_addr(ucfg.chaos_servername, 0));
	SET_CH_INDEX(answer->pk_sidx, 0);
	answer->pk_pkn = 0;
	answer->pk_ackn = 0;
	status = (struct status *) &answer->pk_cdata[0];
	memset(status, 0, sizeof(struct status));
	strncpy(status->sb_name, ucfg.chaos_servername, CHSTATNAME);
	status->sb_name[CHSTATNAME - 1] = '\0';
	/* Only put in the subnet (with the 400 marker) */
	status->sb_data->sb_ident = (chaos_addr(ucfg.chaos_servername, 0) >> 8) | 0400;
	/* and say no data is following */
	status->sb_data->sb_nshorts = 0 * sizeof(int) / sizeof(short);
	chaos_queue(answer);
	return 0;
}

static int
chaos_queue_file_pkt(struct packet *packet)
{
	void processdata(struct connection *conn);
	struct packet *answer;
	struct connection *conn;

	packet->pk_cdata[PH_LEN(packet->pk_phead)] = '\0';
	INFO(TRACE_CHAOS, "chaos: RFC (%s): answering...\n", &packet->pk_cdata);
	conn = allconn();
	conn->cn_faddr = packet->pk_saddr;
	conn->cn_fidx = packet->pk_sidx;
	answer = chaos_allocate_packet(conn, OPNOP, 2 * sizeof(unsigned short));
	answer->pk_ackn = packet->pk_pkn;
	conn->cn_racked = packet->pk_pkn;
	*(unsigned short *) &answer->pk_cdata[0] = packet->pk_pkn;	/* Last packed received. */
	*(unsigned short *) &answer->pk_cdata[2] = conn->cn_rwsize;
	chaos_connection_queue(conn, answer);
	conn->cn_state = CSLISTEN;
	processdata(conn);
	return 0;
}

static int
chaos_queue_mini_pkt(struct packet *packet)
{
	void processmini(struct connection *conn);
	struct packet *answer;
	struct connection *conn;

	packet->pk_cdata[PH_LEN(packet->pk_phead)] = '\0';
	INFO(TRACE_CHAOS, "chaos: RFC (%s): answering...\n", &packet->pk_cdata);
	conn = allconn();
	conn->cn_faddr = packet->pk_saddr;
	conn->cn_fidx = packet->pk_sidx;
	answer = chaos_allocate_packet(conn, OPNOP, 2 * sizeof(unsigned short));
	answer->pk_ackn = packet->pk_pkn;
	conn->cn_racked = packet->pk_pkn;
	*(unsigned short *) &answer->pk_cdata[0] = packet->pk_pkn;	/* Last packed received. */
	*(unsigned short *) &answer->pk_cdata[2] = conn->cn_rwsize;
	chaos_connection_queue(conn, answer);
	conn->cn_state = CSLISTEN;
	conn->cn_rlast = 1;
	processmini(conn);
	return 0;
}

int
chaos_send_to_local(char *buffer, int size)
{				/* ---!!! rcvpkt ? */
	struct packet *packet;
	struct connection *conn;

	packet = (struct packet *) buffer;
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
	DEBUG(TRACE_CHAOS, "chaos: transmitting packet (dest_addr = %o, uch11_myaddr=%o, size %d, wcount %d, op: %o)\n", CH_ADDR_SHORT(packet->pk_daddr), uch11_myaddr, size, (size + 1) / 2, packet->pk_op);
	/*
	 * Receive packets addressed to ourselves.
	 */
	if (CH_ADDR_SHORT(packet->pk_daddr) == uch11_myaddr) {
		memcpy(uch11_rcv_buffer, buffer, size);
		uch11_rcv_buffer_size = (size + 1) / 2;
		uch11_rcv_buffer_empty = false;
		uch11_rx_pkt();
	}
	/*
	 * Ignore packets anything that isn't addressed to us.
	 */
	if (CH_ADDR_SHORT(packet->pk_daddr) != chaos_addr(ucfg.chaos_servername, 0)) {
		DEBUG(TRACE_CHAOS, "chaos: ignoring packet not to us (%o): %o\n", chaos_addr(ucfg.chaos_servername, 0), CH_ADDR_SHORT(packet->pk_daddr));
		return 0;
	}
	conn = chaos_find_connection(CH_INDEX_SHORT(packet->pk_didx));
	/*
	 * ---!!! Large parts of this is in rcvrfc.
	 */
	if (packet->pk_op == RFCOP) {
		DEBUG(TRACE_CHAOS, "chaos: got RFC packet\n");
		if (conn && conn->cn_state == CSLISTEN) {
			DEBUG(TRACE_CHAOS, "chaos: duplicate RFC\n");
			return 0;
		}
		if (conn) {
			struct packet *pkt;

			pkt = pkalloc((size_t) size, 0);
			memcpy(pkt, packet, size);
			chaos_connection_queue(conn, pkt);
			return 0;
		}
		if (memcmp(&packet->pk_cdata, "STATUS", 6) == 0)
			return chaos_queue_status_pkt(CH_ADDR_SHORT(packet->pk_saddr), CH_INDEX_SHORT(packet->pk_sidx));
		else if (memcmp(&packet->pk_cdata, "TIME", 4) == 0)
			return chaos_queue_time_pkt(CH_ADDR_SHORT(packet->pk_saddr), CH_INDEX_SHORT(packet->pk_sidx));
		else if (memcmp(&packet->pk_cdata, "UPTIME", 6) == 0)
			return chaos_queue_uptime_pkt(CH_ADDR_SHORT(packet->pk_saddr), CH_INDEX_SHORT(packet->pk_sidx));
		else if (memcmp(&packet->pk_cdata, "FILE", 4) == 0)
			return chaos_queue_file_pkt(packet);
		else if (memcmp(&packet->pk_cdata, "MINI", 4) == 0)
			return chaos_queue_mini_pkt(packet);
		else {
			char buffer[512];

			strncpy(buffer, (char *) packet->pk_cdata, PH_LEN(packet->pk_phead));
			buffer[PH_LEN(packet->pk_phead)] = '\0';
			WARNING(TRACE_CHAOS, "chaos: RFC (%s): protocol not implemented\n", buffer);
			return 0;
		}
	}
	if (packet->pk_op == SNSOP && conn) {
		struct packet *sts;

		sts = pkalloc(2 * sizeof(unsigned short) + 3 * sizeof(unsigned short), 1);
		sts->pk_op = STSOP;
		SET_PH_LEN(sts->pk_phead, 2 * sizeof(unsigned short));
		sts->pk_daddr = packet->pk_saddr;
		sts->pk_didx = packet->pk_sidx;
		conn->cn_faddr = packet->pk_saddr;
		conn->cn_fidx = packet->pk_sidx;
		sts->pk_saddr = conn->cn_laddr;
		sts->pk_sidx = conn->cn_lidx;
		sts->pk_pkn = packet->pk_pkn;
		if (cmp_gt(packet->pk_ackn, conn->cn_tacked))
			conn->cn_tacked = packet->pk_ackn;
		sts->pk_ackn = conn->cn_racked;
		*(unsigned short *) &sts->pk_cdata[0] = conn->cn_racked;
		*(unsigned short *) &sts->pk_cdata[2] = conn->cn_rwsize;
		chaos_connection_queue(conn, sts);
		return 0;
	}
	if (packet->pk_op == STSOP) {
		if (conn) {
			if (cmp_gt(packet->pk_ackn, conn->cn_tacked))
				conn->cn_tacked = packet->pk_ackn;
			if (conn->lastpacket && conn->cn_tacked == conn->lastpacket->pk_pkn) {
				free(conn->lastpacket);
				conn->lastpacket = 0;
			}
			conn->cn_state = CSOPEN;
			conn->cn_twsize = *(unsigned short *) &packet->pk_cdata[2];
			DEBUG(TRACE_CHAOS, "chaos: STSOP: twsize = %d\n", conn->cn_twsize);
			pthread_mutex_lock(&conn->twsem);
			pthread_cond_signal(&conn->twcond);
			pthread_mutex_unlock(&conn->twsem);
		}
		return 0;
	}
	if (conn && packet->pk_op == CLSOP) {	/* clsconn ? */
		DEBUG(TRACE_CHAOS, "chaos: CLSOP: got close \n");
		conn->cn_state = CSCLOSED;
		pthread_mutex_lock(&conn->queuesem);
		pthread_cond_signal(&conn->queuecond);
		pthread_mutex_unlock(&conn->queuesem);
		usleep(100000);	/* wait for queue to wake up */
		rlsconn(conn);
		return 0;
	}
	if (conn && (cmp_gt(conn->cn_rlast, packet->pk_pkn) || (conn->cn_rlast == packet->pk_pkn))) {
		struct packet *status;

		DEBUG(TRACE_CHAOS, "chaos: duplicate data packet\n");
		status = chaos_allocate_packet(conn, STSOP, 2 * sizeof(unsigned short));
		status->pk_pkn = packet->pk_pkn;
		conn->cn_racked = conn->cn_rlast;
		*(unsigned short *) &status->pk_cdata[0] = conn->cn_rlast;
		*(unsigned short *) &status->pk_cdata[2] = conn->cn_rwsize;
		chaos_connection_queue(conn, status);
		return 0;
	}
	if (conn) {
		struct packet *pkt;

		pkt = pkalloc((size_t) size, 0);
		memcpy(pkt, packet, size);
		chaos_connection_queue(conn, pkt);
	}
	return 0;
}

int
chaos_poll_local(void)
{
	struct packet *packet;
	struct packet_queue *node;
	int size;

	/*
	 * Is RX buffer full?
	 */
	if (!uch11_rcv_buffer_empty && (uch11_csr & CHAOS_CSR_RECEIVE_DONE)) {
		DEBUG(TRACE_CHAOS, "chaos: polling, but unread data exists\n");
		return 0;
	}
	if (!uch11_rcv_buffer_empty) {
		DEBUG(TRACE_CHAOS, "chaos: polling, but buffer not empty\n");
		return 0;
	}
	if (TAILQ_EMPTY(&queuehead) == true) {
		return 0;
	}
	if (!(uch11_csr & CHAOS_CSR_RECEIVE_ENABLE)) {
		DEBUG(TRACE_CHAOS, "chaos: polling but rx not enabled\n");
		return 0;
	}
	pthread_mutex_lock(&recvqueue);
	node = TAILQ_FIRST(&queuehead);
	packet = node->packet;
	TAILQ_REMOVE(&queuehead, node, next);
	free(node);
	pthread_mutex_unlock(&recvqueue);
	size = ((PH_LEN(packet->pk_phead) & 0x0fff) + CHHEADSIZE + 1) / 2;
	/*
	 * Ignore any packets not to us.
	 */
	if (CH_ADDR_SHORT(packet->pk_daddr) != uch11_myaddr)
		return 0;
	memcpy(uch11_rcv_buffer, packet, (size_t) size * sizeof(unsigned short));
	/*
	 * Hardware header.
	 */
	uch11_rcv_buffer[size] = CH_ADDR_SHORT(packet->pk_daddr);
	uch11_rcv_buffer[size + 1] = CH_ADDR_SHORT(packet->pk_saddr);
	uch11_rcv_buffer[size + 2] = 0;
	size += 3;
	uch11_rcv_buffer_size = size;
	uch11_rcv_buffer_empty = false;
	DEBUG(TRACE_CHAOS, "chaos: polling: got chaos packet of %d bytes\n", uch11_rcv_buffer_size * 2);
#if 0
	dumpbuffer(uch11_rcv_buffer, size * 2);
#endif
	uch11_rx_pkt();
	return 0;
}
