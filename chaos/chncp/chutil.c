#ifndef LINUX_KERNEL
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#endif

#ifdef SELECT
# ifndef USE_CBRIDGE
#define CHDEFINE
#include "uch11-backend.h"
#undef CHDEFINE
# else /* USE_CBRIDGE */
#include "chaos.h"
#include "chunix/chconf.h"
#define CHDEFINE
#include "chncp/chncp.h"
#undef CHDEFINE
#  include "chcbridge/cbncp.h"
# endif /* USE_CBRIDGE */
#else /* SELECT */
#include "chaos.h"
#include "chsys.h"
#include "../chunix/chconf.h"
#define CHDEFINE
#include "chncp.h"
#undef CHDEFINE
#endif

#ifdef DMALLOC
#include "dmalloc.h"
#endif

/*
 * Miscellaneous utility routines - notice the CHDEFINE is turned on here
 * and only here.
 */

int uniq;

/*
 * Allocate a connection and return it, also allocating a slot in Chconntab
 */
struct connection *
allconn(void)
{
#ifndef SELECT
	struct connection *conn;
	struct connection **cptr;

/*	static int uniq; */

	if ((conn = connalloc()) == NOCONN) {
		debug(DCONN | DABNOR, printf("Conn: alloc failed (packet)\n"));
		Chaos_error = CHNOPKT;
		return (NOCONN);
	}
	for (cptr = &Chconntab[0]; cptr != &Chconntab[CHNCONNS]; cptr++) {
		if (*cptr != NOCONN)
			continue;
		*cptr = conn;
		clear((char *)conn, sizeof(struct connection));
		SET_CH_INDEX(conn->cn_lidx, cptr - &Chconntab[0]);
		if (++uniq == 0)
			uniq = 1;
		conn->cn_luniq = uniq;
		debug(DCONN, printf("Conn: alloc #%x\n", CH_INDEX_SHORT(conn->cn_lidx)));
		return (conn);
	}
	ch_free((char *)conn);
	Chaos_error = CHNOCONN;
	debug(DCONN | DABNOR, printf("Conn: alloc failed (table)\n"));
	return (NOCONN);
#else
	for (int i = 0; i < CHNCONNS; i++) {
		if (Chconntab[i] == 0) {
			struct connection *conn;
			static unsigned short index = 0x0101;

			conn = connalloc();
			if (conn == NOCONN) {
				debug(DCONN | DABNOR, printf("Conn: alloc failed (packet)\n"));
				Chaos_error = CHNOPKT;
				return (NOCONN);
			}
			conn->cn_state = CSCLOSED;
			conn->packetnumber = 0;
			conn->cn_racked = 0;
			SET_CH_INDEX(conn->cn_fidx, 0);
			SET_CH_ADDR(conn->cn_faddr, 0);
#ifndef USE_CBRIDGE
			SET_CH_ADDR(conn->cn_laddr, uch11_serveraddr);
#endif
			SET_CH_INDEX(conn->cn_lidx, index);
			index += 0x0100;
#ifndef USE_CBRIDGE
			TAILQ_INIT(&conn->queuehead);

			pthread_mutex_init(&conn->queuelock, NULL);

			pthread_mutex_init(&conn->queuesem, NULL);
			pthread_cond_init(&conn->queuecond, NULL);
			pthread_mutex_init(&conn->twsem, NULL);
			pthread_cond_init(&conn->twcond, NULL);
#endif

			conn->cn_rlast = 0;
			conn->cn_tlast = 0;
			conn->cn_tacked = 0;
#ifndef USE_CBRIDGE
			TAILQ_INIT(&conn->orderhead);
#endif
			conn->cn_rwsize = 5;
			conn->cn_twsize = 5;

			Chconntab[i] = conn;
			debug(DCONN, printf("Conn: alloc #%x\n", CH_INDEX_SHORT(conn->cn_lidx)));
			return conn;
		}
	}

	return 0;
#endif
}

/*
 * Make a connection closed with given state, at interrupt time.
 * Queue the given packet on the input queue for the user.
 */
void
clsconn(struct connection *conn, int state, struct packet *pkt)
{
#ifndef SELECT
	freelist(conn->cn_thead);
	conn->cn_thead = conn->cn_ttail = NOPKT;
	conn->cn_state = state;
	debug(DCONN | DABNOR, printf("Conn #%x: CLOSED: state: %d\n", CH_INDEX_SHORT(conn->cn_lidx), state));
	if (pkt != NOPKT) {
		pkt->pk_next = NOPKT;
		if (conn->cn_rhead != NOPKT)
			conn->cn_rtail->pk_next = pkt;
		else
			conn->cn_rhead = pkt;
		conn->cn_rtail = pkt;
	}
	NEWSTATE(conn);
#else
	debug(DCONN | DABNOR, printf("Conn #%x: CLOSED: state: %d\n", CH_INDEX_SHORT(conn->cn_lidx), state));
#endif
}

/*
 * Release a connection - freeing all associated storage.
 * This removes all trace of the connection.
 * Always called from top level at low priority.
 */
void
rlsconn(struct connection *conn)
{
#ifndef SELECT
	Chconntab[conn->cn_ltidx] = NOCONN;
	freelist(conn->cn_routorder);
	freelist(conn->cn_rhead);
	freelist(conn->cn_thead);
#ifdef CHSTRCODE
	if (conn->cn_toutput != NOPKT)
		ch_free((char *)conn->cn_toutput);
#endif
	debug(DCONN, printf("Conn: release #%x\n", CH_INDEX_SHORT(conn->cn_lidx)));
	ch_free((char *)conn);
#else
	for (int i = 0; i < CHNCONNS; i++) {
		if (Chconntab[i] == conn) {
			Chconntab[i] = NOCONN;
			break;
		}
	}

#ifndef USE_CBRIDGE
	pthread_mutex_destroy(&conn->queuelock);

	pthread_mutex_destroy(&conn->queuesem);
	pthread_cond_destroy(&conn->queuecond);
	pthread_mutex_destroy(&conn->twsem);
	pthread_cond_destroy(&conn->twcond);
#endif
	debug(DCONN, printf("Conn: release #%x\n", CH_INDEX_SHORT(conn->cn_lidx)));
	ch_free((char *)conn);
#endif
}

/*
 * Free a list of packets
 */
void
freelist(struct packet *pkt)
{
#ifndef SELECT
	struct packet *opkt;

	while ((opkt = pkt) != NOPKT) {
		pkt = pkt->pk_next;
		ch_free((char *)opkt);
	}
#endif
}

/*
 * Fill a packet with a string, returning packet because it may reallocate
 * Assumes we are called from interrupt level (high priority).
 * If the pkt argument is NOPKT then allocate a packet here.
 * The string is null terminated and may be shorter than "len".
 */
struct packet *
pktstr(struct packet *pkt, char *str, int len)
{
#ifndef SELECT
	struct packet *npkt;
	char *odata;

	if (pkt == NOPKT /* || ch_size((char *)pkt) < CHHEADSIZE + len */ ) {
		if ((npkt = pkalloc(len, 1)) == NOPKT)
			return (NOPKT);
		if (pkt != NOPKT) {
			SET_PH_LEN(pkt->pk_phead, 0);
			movepkt(pkt, npkt);
			ch_free((char *)pkt);
		}
		pkt = npkt;
	}
	odata = pkt->pk_cdata;
	SET_PH_LEN(pkt->pk_phead, len);
	if (len)
		do
			*odata++ = *str;
		while (*str++ && --len);
	return (pkt);
#else
	return NULL;		/* keep compiler happy */
#endif
}

/*
 * Zero out n bytes - this should be somewhere else, probably provided
 * by the implementation.
 */
void
clear(char *ptr, int n)
{
	if (n)
		do {
			*ptr++ = 0;
		} while (--n);
}

/*
 * Move contents of opkt to npkt
 */
void
movepkt(struct packet *opkt, struct packet *npkt)
{
	short *nptr, *optr, n;

	n = (CHHEADSIZE + PH_LEN(opkt->pk_phead) + sizeof(short) - 1) / sizeof(short);
	nptr = (short *)npkt;
	optr = (short *)opkt;
	do {
		*nptr++ = *optr++;
	} while (--n);
}

/*
 * Move n bytes - should probably be elsewhere.
 * Can we replace this with a movc3?
 */
void
chmove(char *from, char *to, int n)
{
	if (n)
		do
			*to++ = *from++;
		while (--n);
}

/*
 * Set packet fields from connection, many routines count on the fact that
 * this routine clears pk_type and pk_next
 */
void
setpkt(struct connection *conn, struct packet *pkt)
{
#ifndef SELECT
	pkt->pk_daddr = conn->cn_faddr;
	pkt->pk_didx = conn->cn_fidx;
	pkt->pk_saddr = conn->cn_laddr;
	pkt->pk_sidx = conn->cn_lidx;
	pkt->pk_type = 0;
	pkt->pk_next = NOPKT;
	SET_PH_FC(pkt->pk_phead, 0);
#endif
}

#ifdef pdp11
/*
 * Swap the word of n longs.
 */
swaplong(short *lp, int n)
{
	short temp;

	if (n)
		do {
			temp = *lp++;
			lp[-1] = *lp;
			*lp++ = temp;
		} while (--n);
}
#endif

/*
 * Attach an interface.
 * This is how the device driver indicates its initial
 * attachment to the chaos NCP.
 */
void
chattach(struct chxcvr *xp)
{
	struct chroute *r;

	r = &Chroutetab[xp->xc_subnet];
	r->rt_type = CHDIRECT;
	r->rt_cost = CHHCOST / 2;
	r->rt_xcvr = xp;
}

#ifdef pdp11
/*
 * Swap the word of n longs.
 */
swaplong(short *lp, int n)
{
	short temp;

	if (n)
		do {
			temp = *lp++;
			lp[-1] = *lp;
			*lp++ = temp;
		} while (--n);
}
#endif

#ifdef SELECT
void
chaos_dump_connection(struct connection *conn)
{
	for (int i = 0; i < CHNCONNS; i++) {
		if (Chconntab[i] == conn) {
			printf("conn: %d\n", i);
			printf("conn: remote %04x %04x\n", CH_ADDR_SHORT(conn->cn_faddr), CH_INDEX_SHORT(conn->cn_fidx));
			printf("conn: local %04x %04x\n", CH_ADDR_SHORT(conn->cn_laddr), CH_INDEX_SHORT(conn->cn_lidx));
			printf("conn: cn_rlast=%d cn_racked=%d\n", conn->cn_rlast, conn->cn_racked);
			printf("conn: cn_tlast=%d cn_tacked=%d\n", conn->cn_tlast, conn->cn_tacked);
			break;
		}
	}
}

#ifndef USE_CBRIDGE
void
chaos_interrupt_connection(struct connection *conn)
{
	conn->cn_tacked = conn->cn_tlast;

	pthread_mutex_lock(&conn->twsem);
	pthread_cond_signal(&conn->twcond);
	pthread_mutex_unlock(&conn->twsem);
}
#endif

struct connection *
chaos_find_connection(unsigned short index)
{
	for (int i = 0; i < CHNCONNS; i++) {
		if (Chconntab[i] && CH_INDEX_SHORT(Chconntab[i]->cn_lidx) == index)
			return Chconntab[i];
	}

	return NOCONN;
}

/* should really become pkalloc() */
struct packet *
chaos_allocate_packet(struct connection *conn, int opcode, ssize_t len)
{
	struct packet *packet;

	packet = pkalloc(len, 1);

	packet->pk_op = (unsigned short)opcode;
	SET_PH_LEN(packet->pk_phead, (unsigned short)len);
	packet->pk_daddr = conn->cn_faddr;
	packet->pk_didx = conn->cn_fidx;
	packet->pk_saddr = conn->cn_laddr;
	packet->pk_sidx = conn->cn_lidx;
	if (opcode == STSOP)
		packet->pk_pkn = conn->packetnumber;
	else
		packet->pk_pkn = ++conn->packetnumber;
	if (packet->pk_pkn == 0)
		packet->pk_pkn = conn->packetnumber = 1;
	packet->pk_ackn = conn->cn_rlast;
	conn->cn_racked = conn->cn_rlast;

	return packet;
}
#endif
