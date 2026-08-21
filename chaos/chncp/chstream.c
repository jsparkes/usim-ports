#include "../h/chaos.h"
#include "chsys.h"
#include "../chunix/chconf.h"
#include "chncp.h"

/*
 * This file contains code for a stream level (as opposed to packet level)
 * interface to the chaosnet. It is written to be general enough to fit into
 * various systems where such an interface in desirable.  This has been the
 * case both in UNIX and in FEZ, although UNIX also needs its own "tty"
 * interface.
 *
 * Several macros must be supplied in chsys.h(they can do nothing of course):
 *	LOCK, UNLOCK	- mask out chaos interrupts (send, receive, clock)
 *	NOINPUT		- a LOCK'd hook for indicating that there is no input
 *			  available on a channel (used in FEZ...)
 *	NOOUTPUT	- a hook for indicating when no output is possible
 *			  (window is full) (unused in UNIX, used in FEZ...)
 *	CHRCOPY		- read copy routine (from, to, count) which should
 *			  return the (to) ptr after copying is done.
 *	CHWCOPY		- write copy routine (from, to, count) like CHRCOPY
 */

/*
 * Stream read routine - given connection, ptr and number of chars
 *	As many of the desired characters are transferred as are available
 *	in the received packet list.  If any have been read, the number
 *	transferred is return.  If none were transferred then if
 *	an EOF has been encountered, CHEOF is returned, otherwise if the
 *	connection is still open, zero is returned, otherwise CHERROR is
 *	returned (connection no longer open).
 *	Note that answer (ANSOP) packets read just like data at act like
 *	an EOF was encountered after the ANSOP.
 *	UNCOP (uncontrolled data) packets are treated just like data packets.
 *	An EOF packet just terminates a read that has made some progress
 *	and is specifically indicated (by zero chars read) in the next read
 *	call.  After this indication a bit (CHEOFSEEN) is set which causes
 *	end-of-file indications to be read as long as the connection
 *	is open.  If more data packets arrive, this bit is turned off.
 */
int
ch_sread(struct connection *conn, char *ptr, unsigned nchars, int arg, int *errorp)
{
	struct packet *pkt;
	unsigned count;
	unsigned ntodo = nchars;

	*errorp = 0;
	while (ntodo != 0 && (pkt = conn->cn_rhead) != NOPKT)
		switch (pkt->pk_op) {
		case EOFOP:
			if (ntodo != nchars)
				return (nchars - ntodo);
			CHLOCK;
			ch_read(conn);	/* consume it */
			ch_sts(conn);	/* ensure immediately ack */
			CHUNLOCK;
			conn->cn_flags |= CHEOFSEEN;
			return (CHEOF);
		default:
			if (!ISDATOP(pkt)) {
				CHLOCK;
				ch_read(conn);
				CHUNLOCK;
				break;
			}
			/*
			 * Fall into... 
			 */
		case ANSOP:
		case UNCOP:
			count = PH_LEN(pkt->pk_phead) >= ntodo ? ntodo : PH_LEN(pkt->pk_phead);
			ptr = CHRCOPY(&pkt->pk_cdata[conn->cn_roffset], ptr, count, arg, errorp);
			if (*errorp)
				goto out;
			ntodo -= count;
			conn->cn_flags &= ~CHEOFSEEN;
			SET_PH_LEN(pkt->pk_phead, PH_LEN(pkt->pk_phead) - count);
			if (PH_LEN(pkt->pk_phead) == 0) {
				CHLOCK;
				ch_read(conn);
				CHUNLOCK;
				conn->cn_roffset = 0;
				if (pkt->pk_op == ANSOP)
					conn->cn_flags |= CHEOFSEEN;
			} else
				conn->cn_roffset += count;
		}
      out:if (ntodo != 0) {
		CHLOCK;
		NOINPUT(conn);
		CHUNLOCK;
	}
	return (ntodo != nchars ? nchars - ntodo : conn->cn_flags & CHEOFSEEN ? CHEOF : conn->cn_state == CSOPEN ? 0 : CHERROR);
}

/*
 * Stream write routine - given connection, ptr and number of characters
 *	Characters are written until exhausted or until the window is full.
 *	The number of characters not read is returned, unless this connection
 *	closes, in which case CHERROR is returned.  If no buffers are avaiable
 *	(presumably a temporary condition), CHTEMP is returned.
 *	ANS packets are sent if the flag is set.
 */
int
ch_swrite(struct connection *conn, char *ptr, unsigned nchars, int arg, int *errorp)
{
	struct packet *pkt;
	unsigned count;
	unsigned ntodo = nchars;

	*errorp = 0;
	CHLOCK;
	for (;;) {
		if (conn->cn_state != CSOPEN && (conn->cn_state != CSRFCRCVD || (conn->cn_flags & CHANSWER) == 0)) {
			CHUNLOCK;
			return CHERROR;
		}
		if (ntodo == 0 || chtfull(conn))
			break;
		if ((pkt = conn->cn_toutput) == NOPKT) {
			CHUNLOCK;
			if ((pkt = pkalloc(CHMAXDATA, 0)) == NOPKT)
				return (ntodo == nchars ? CHTEMP : nchars - ntodo);
			pkt->pk_op = conn->cn_flags & CHANSWER ? ANSOP : DATOP;
			SET_PH_LEN(pkt->pk_phead, 0);
			conn->cn_troom = CHMAXDATA;
		} else {
			conn->cn_toutput = NOPKT;
			CHUNLOCK;
		}
		count = ntodo > conn->cn_troom ? conn->cn_troom : ntodo;
		ptr = CHWCOPY(ptr, &pkt->pk_cdata[PH_LEN(pkt->pk_phead)], count, arg, errorp);
		if (*errorp)
			break;
		SET_PH_LEN(pkt->pk_phead, PH_LEN(pkt->pk_phead) + count);
		ntodo -= count;
		CHLOCK;
		if ((conn->cn_troom -= count) == 0) {
			if (ch_write(conn, pkt))
				return CHERROR;
		} else {
			conn->cn_toutput = pkt;
			pkt->pk_time = Chclock;
		}
	}
	if (ntodo != 0)
		NOOUTPUT(conn);
	CHUNLOCK;
	return (nchars - ntodo);
}

/*
 * Output flush routine - release the currently-being-filled packet for
 * transmission immediately.
 * Called at high priority.
 * If output window is full, CHTEMP is returned.
 * If connection is not open, CHERROR is returned from ch_write.
 */
int
ch_sflush(struct connection *conn)
{
	struct packet *pkt;

	if ((pkt = conn->cn_toutput) != NOPKT)
		if (chtfull(conn))
			return CHTEMP;
		else {
			conn->cn_toutput = NOPKT;
			return ch_write(conn, pkt);
		}
	return 0;
}
