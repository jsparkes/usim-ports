#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <time.h>
#include <sys/types.h>
#include <sys/uio.h>
#include <sys/errno.h>

/*
 * UNIX device driver interface to the Chaos N.C.P.
 */

#include "chaos.h"
#include "chsys.h"
#include "chconf.h"
#include "../chncp/chncp.h"

#ifdef BSD42

#ifndef BSD4_3
#include "vnode.h"
#endif
#include "file.h"
#include "dir.h"
#include "user.h"
#include "conf.h"
#include "buf.h"
#include "systm.h"
#include "uio.h"
#include "kernel.h"
#include "ioctl.h"
#include "proc.h"

#ifdef vax
#ifndef BSD4_3
#include "cht.h"
#endif
#endif				/* vax */

static int initted;		/* NCP initialization flag */
int Rfcwaiting;			/* Someone waiting on unmatched RFC */

#define CHIOPRIO	(PZERO+1)	/* Just interruptible */

#ifdef DEBUG_CHAOS
#define ASSERT(x,y)	if(!(x)) printf("%s: Assertion failure\n",y);
#else
#define ASSERT(x,y)
#endif

#ifndef vax
#define setjmp save
#endif

/*
 * 4.2 BSD glue between generic file descriptor and chaos connection code.
 */

int chf_rw(struct file *fp, enum uio_rw rw, struct uio *uio);
int chf_ioctl(struct file *fp, int cmd, caddr_t value);
int chf_select(struct file *fp, int which);
void chf_close(struct file *fp);
int chread(struct connection *conn, int uio);
int chwrite(struct connection *conn, int uio);

struct fileops chfileops = { chf_rw, chf_ioctl, chf_select, chf_close };

int
chf_rw(struct file *fp, enum uio_rw rw, struct uio *uio)
{
	int ret;

	ASSERT(fp->f_ops == &chfileops, "chf_rw ent")
	    ret = (rw == UIO_READ ? chread : chwrite)
	    ((struct connection *)fp->f_data, (int)uio);
	ASSERT(fp->f_ops == &chfileops, "chf_rw exit")
	    return ret;
}

int
chf_ioctl(struct file *fp, int cmd, caddr_t value)
{
	int ret;

	ASSERT(fp->f_ops == &chfileops, "chf_ioctl ent")
	    ret = chioctl((struct connection *)fp->f_data, cmd, value);
	ASSERT(fp->f_ops == &chfileops, "chf_ioctl exit")
	    return ret;
}

/* select support -- Bruce Nemnich, 6 May 85 */
int
chf_select(struct file *fp, int which)
{
	struct connection *conn = (struct connection *)fp->f_data;
	int s = splimp();
	struct proc *p;
	int retval = 0;

	ASSERT(fp->f_ops == &chfileops, "chf_select ent")
	    switch (which) {

	case FREAD:
		if (chrempty(conn)) {
			if ((p = conn->cn_rsel) && p->p_wchan == (caddr_t)&selwait)
				conn->cn_flags |= CHSELRCOLL;
			else
				conn->cn_rsel = u.u_procp;
		} else
			retval = 1;
		break;

	case FWRITE:
		if (chtfull(conn)) {
			if ((p = conn->cn_tsel) && p->p_wchan == (caddr_t)&selwait)
				conn->cn_flags |= CHSELTCOLL;
			else
				conn->cn_tsel = u.u_procp;
		} else
			retval = 1;
		break;
	}
	splx(s);
	ASSERT(fp->f_ops == &chfileops, "chf_select exit")
	    return retval;
}

void
chf_close(struct file *fp)
{
	struct connection *conn = (struct connection *)fp->f_data;

	/*
	 * If this connection has been turned into a tty, then the
	 * tty owns it and we don't do anything.
	 */

	ASSERT(fp->f_ops == &chfileops, "chf_close ent")
	    if (conn && conn->cn_mode != CHTTY)
		chclose(conn);
}

#define UCOUNT ((struct uio *)uio)->uio_resid
#define UREAD(from, count) uiomove(from, count, UIO_READ, uio)
#define UWRITE(to, count) uiomove(to, count, UIO_WRITE, uio)

int
chwcopy(char *from, char *to, unsigned count, int uio, int *errorp)
{
	*errorp = UWRITE(to, count);
	return 0;
}

int
chrcopy(char *from, char *to, unsigned count, int uio, int *errorp)
{
	*errorp = UREAD(from, count);
	return 0;
}

/*
 * Access the Chaos NCP.
 * For one of two reasons:
 *	1. To perform basic NCP control functions, via minor device CHURFCMIN
 *	2. To open a connection via CHAOSMIN
 * The actual opening of the connection happens later via the CHIOCOPEN ioctl.
 */
int
chropen(dev_t dev, int flag)
{
	int errno = 0;

	ch_bufalloc();
	/*
	 * initialize the NCP somewhere else? 
	 */
	if (!initted) {
		chreset();	/* Reset drivers */
		chtimeout();	/* Start clock "process" */
		initted++;
	}
	if (minor(dev) == CHURFCMIN)
		if (Chrfcrcv == 0)
			Chrfcrcv++;
		else
			errno = ENXIO;

	return errno;
}
#endif				/* BSD42 */

/*
 * Return a connection or return NULL and set *errnop to any error.
 */
struct connection *
chopen(struct chopen *c, int wflag, int *errnop)
{
	struct connection *conn;
	struct packet *pkt;
	int rwsize, length;

	*errnop = 0;
	length = c->co_clength + c->co_length + (c->co_length ? 1 : 0);
	if (length > CHMAXPKT || c->co_clength <= 0) {
		*errnop = -E2BIG;
		return NOCONN;
	}
	pkt = pkalloc(length, 0);
	if (pkt == NOPKT) {
		*errnop = -ENOBUFS;
		return NOCONN;
	}
	if (c->co_length)
		pkt->pk_cdata[c->co_clength] = ' ';
	memcpy(pkt->pk_cdata, c->co_contact, c->co_clength);
	if (c->co_length)
		memcpy(&pkt->pk_cdata[c->co_clength + 1], c->co_data, c->co_length);
	rwsize = c->co_rwsize ? c->co_rwsize : CHDRWSIZE;
	SET_PH_LEN(pkt->pk_phead, length);
	conn = c->co_host ? ch_open(c->co_host, rwsize, pkt) : ch_listen(pkt, rwsize);
	if (conn == NOCONN) {
		*errnop = -ENXIO;
		return NOCONN;
	}
#if 0
	if (!c->co_async) {
		/*
		 * We should hang until the connection changes from
		 * its initial state.
		 * If interrupted, flush the connection.
		 */
		if (setjmp(&u.u_qsave) == 0) {
			CHLOCK;
			while (conn->cn_state == (c->co_host ? CSRFCSENT : CSLISTEN))
				sleep((caddr_t)conn, CHIOPRIO);
			CHUNLOCK;
			/*
			 * If the connection is not open, the open failed.
			 * Unless is got an ANS back.
			 */
			if (conn->cn_state != CSOPEN && (conn->cn_state != CSCLOSED || (pkt = conn->cn_rhead) == NOPKT || pkt->pk_op != ANSOP)) {
				rlsconn(conn);
				*errnop = EIO;
				return NOCONN;
			}
		}
	}
	if (wflag)
		conn->cn_sflags |= CHFWRITE;
	conn->cn_sflags |= CHRAW;
#endif
	conn->cn_mode = CHSTREAM;
	return conn;
}

#if BSD42
void
chrclose(dev_t dev, int flag, struct connection *conn)
{
	if (minor(dev) == CHURFCMIN) {
		Chrfcrcv = 0;
		freelist(Chrfclist);
		Chrfclist = NOPKT;
		return;
#ifdef BSD42
	}
#else
	} else {
		/*
		 * If this connection has been turned into a tty, then the
		 * tty owns it and we don't do anything.
		 */
		if (conn && conn->cn_mode != CHTTY)
			chclose(conn);
	}
#endif
}

void
chclose(struct connection *conn)
{
	struct packet *pkt;

	switch (conn->cn_mode) {
	case CHTTY:
		panic("chclose on tty");
	case CHSTREAM:
		splimp();
		if (setjmp(&u.u_qsave)) {
			pkt = pktstr(NOPKT, "User interrupted", 16);
			if (pkt != NOPKT)
				pkt->pk_op = CLSOP;
			ch_close(conn, pkt, 0);
			goto shut;
		}
		if (conn->cn_sflags & CHFWRITE) {
			/*
			 * If any input packets other than the RFC are around
			 * something is wrong and we just abort the connection
			 */
			while ((pkt = conn->cn_rhead) != NOPKT) {
				ch_read(conn);
				if (pkt->pk_op != RFCOP)
					goto recclose;
			}
			/*
			 * We set this flag telling the interrupt time
			 * receiver to abort the connection if any new packets
			 * arrive.
			 */
			conn->cn_sflags |= CHCLOSING;
			/*
			 * Closing a stream transmitter involves flushing
			 * the last packet, sending an EOF and waiting for
			 * it to be acknowledged.  If the connection was
			 * bidirectional, the reader should have already
			 * read until EOF if everything is to be closed
			 * cleanly.
			 */
		      checkfull:
			while (chtfull(conn)) {
				conn->cn_sflags |= CHOWAIT;
				sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
			}
			if (conn->cn_state == CSOPEN || conn->cn_state == CSRFCRCVD) {
				if (conn->cn_toutput) {
					ch_sflush(conn);
					goto checkfull;
				}
				if (conn->cn_state == CSOPEN)
					(void)ch_eof(conn);
			}
			while (!chtempty(conn)) {
				conn->cn_sflags |= CHOWAIT;
				sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
			}
		} else if (conn->cn_state == CSOPEN) {
			/*
			 * If we are only reading then we should read the EOF
			 * before closing and wiat for the other end to close.
			 */
			if (conn->cn_flags & CHEOFSEEN)
				while (conn->cn_state == CSOPEN)
					sleep((caddr_t)conn, CHIOPRIO);
		}
	      recclose:
		spl0();
		/*
		 * Fall into... 
		 */
	case CHRECORD:		/* Record oriented close is just sending a CLOSE */
		if (conn->cn_state == CSOPEN) {
			pkt = pkalloc(0, 0);
			if (pkt != NOPKT) {
				pkt->pk_op = CLSOP;
				pkt->pk_len = 0;
			}
			ch_close(conn, pkt, 0);
		}
	}
      shut:
	spl0();
	ch_close(conn, NOPKT, 1);
	ch_buffree();
}

/*
 * Raw read routine.
 */
int
chrread(dev_t dev, int uio)
{
	struct packet *pkt;
	int errno = 0;

	/*
	 * only CHURFCMIN is readable as char device 
	 */
	if (minor(dev) == CHURFCMIN) {
		splimp();
		while ((pkt = ch_rnext()) == NOPKT) {
			Rfcwaiting++;
			sleep((caddr_t)&Chrfclist, CHIOPRIO);
		}
		spl0();
		if (UCOUNT < pkt->pk_len)
			errno = EIO;
		else
			UREAD((caddr_t)pkt->pk_cdata, pkt->pk_len);
	} else
		errno = ENXIO;

	return errno;
}

/*
 * Return an errno on error
 */
int
chread(struct connection *conn, int uio)
{
	struct packet *pkt;
	int count;

	switch (conn->cn_mode) {
	case CHTTY:
		return ENXIO;
	case CHSTREAM:
		if (conn->cn_state == CSRFCRCVD)
			ch_accept(conn);
		for (count = UCOUNT; UCOUNT != 0;) {
			int error, n;

			n = ch_sread(conn, (char *)0, (unsigned)UCOUNT, uio, &error);
			if (error)
				return error;
			switch (n) {
			case 0:	/* No data to read */
				if (count != UCOUNT)
					return 0;
				splimp();
				while (chrempty(conn)) {
					conn->cn_sflags |= CHIWAIT;
					sleep((char *)&conn->cn_rhead, CHIOPRIO);
				}
				spl0();
				continue;
			case CHEOF:
				return 0;
			default:
				if (n < 0)
					return EIO;
			}
		}
		return 0;
		/*
		 * Record oriented mode gives a one byte packet opcode
		 * followed by the data in the packet.  The buffer must
		 * be large enough to fit the data and the opcode, otherwise an
		 * i/o error results.
		 */
	case CHRECORD:
		splimp();
		while (chrempty(conn)) {
			conn->cn_sflags |= CHIWAIT;
			sleep((char *)&conn->cn_rhead, CHIOPRIO);
		}
		spl0();
		if ((pkt = conn->cn_rhead) == NOPKT || pkt->pk_len + 1 > UCOUNT)	/* + 1 for opcode */
			return EIO;
		else {
			int errno;

			if ((errno = UREAD(&pkt->pk_op, (unsigned)1)) == 0 && (errno = UREAD(pkt->pk_cdata, pkt->pk_len)) == 0) {
				splimp();
				ch_read(conn);
				spl0();
			}
			return errno;
		}
	}
	/*
	 * NOTREACHED 
	 */
}

/*
 * Raw write routine
 * Note that user programs can write to a connection
 * that has been CHIOCANSWER"'d, implying transmission of an ANS packet
 * rather than a normal packet.  This is illegal for TTY mode connections,
 * is handled in the system independent stream code for STREAM mode, and
 * is handled here for RECORD mode.
 */
int
chrwrite(dev_t dev)
{
	return ENXIO;
}

/*
 * Return an errno or 0
 */
int
chwrite(struct connection *conn, int uio)
{
	struct packet *pkt;

	if (conn->cn_state == CSRFCRCVD)
		ch_accept(conn);
	switch (conn->cn_mode) {
	case CHTTY:
		/*
		 * Fall into (on RAW mode only) 
		 */
	case CHSTREAM:
		while (UCOUNT != 0) {
			int error, n;

			n = ch_swrite(conn, (char *)0, (unsigned)UCOUNT, uio, &error);
			if (error)
				return error;
			switch (n) {
			case 0:
				splimp();
				while (chtfull(conn)) {
					conn->cn_sflags |= CHOWAIT;
					sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
				}
				spl0();
				continue;
			case CHTEMP:
				sleep((caddr_t)&lbolt, CHIOPRIO);
				continue;
			default:
				if (n < 0)
					return EIO;
			}
		}
		return 0;
	case CHRECORD:		/* One write call -> one packet */
		if (UCOUNT < 1 || UCOUNT - 1 > CHMAXDATA || conn->cn_state == CSINCT)
			return EIO;

		splimp();
		while (chtfull(conn)) {
			conn->cn_sflags |= CHOWAIT;
			sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
		}
		spl0();
		while ((pkt = pkalloc((int)(UCOUNT - 1), 0)) == NOPKT) {
			/*
			 * block if we can't get a packet 
			 */
			sleep((caddr_t)&lbolt, CHIOPRIO);
		}
		{
			int errno = UWRITE(&pkt->pk_op, 1);

			pkt->pk_lenword = UCOUNT;
			if (errno != 0 || UCOUNT && (errno = UWRITE(pkt->pk_cdata, UCOUNT)))
				ch_free((char *)pkt);
			else {
				splimp();
				if (ch_write(conn, pkt))
					errno = EIO;
				spl0();
			}
			return errno;
		}
	}
	/*
	 * NOTREACHED 
	 */
}

/*
 * Raw ioctl routine - perform non-connection functions, otherwise call down
 * errno holds error return value until "out:"
 */
int
chrioctl(int dev, int cmd, caddr_t addr, int flag)
{
	int errno = 0;

	if (minor(dev) == CHURFCMIN) {
		switch (cmd) {
			/*
			 * Skip the first unmatched RFC at the head of the queue
			 * and mark it so that ch_rnext will never pick it up again.
			 */
		case CHIOCRSKIP:
			splimp();
			ch_rskip();
			spl0();
			break;

#if NCHETHER > 0
		case CHIOCETHER:
			errno = cheaddr(addr);
			break;
#endif
#if NCHIL > 0
			/*
			 * Specify chaosnet address of an interlan ethernet interface.
			 */
		case CHIOCILADDR:
			{
				struct chiladdr *ca;
				struct chiladdr chiladdr;

				if (addr == 0 || copyin(addr, (caddr_t)&chiladdr, sizeof(chiladdr)))
					errno = EFAULT;
				ca = &chiladdr;
				if (errno == 0 && chilseta(ca->cil_device, ca->cil_address))
					errno = ENXIO;
			}
			break;
#endif				/* NCHIL > 0 */
		case CHIOCNAME:
			bcopy(addr, Chmyname, CHSTATNAME);
			break;
			/*
			 * Specify my own network number.
			 */
		case CHIOCADDR:
			addr = *(caddr_t *)addr;
			Chmyaddr = (int)addr;
		}
	} else {
#ifdef BSD42
		struct file *fp;

		if (cmd != CHIOCOPEN)
			errno = ENXIO;
		else if ((fp = falloc()) == NULL)
			errno = u.u_error;
		else {
			fp->f_flag = getf(u.u_arg[0])->f_flag;
			fp->f_type = DTYPE_CHAOS;
			fp->f_ops = &chfileops;
			fp->f_data = (caddr_t)chopen((struct chopen *)addr, fp->f_flag & FWRITE, &errno);
			if (fp->f_data == NULL) {
				u.u_ofile[u.u_r.r_val1] = 0;
				fp->f_count = 0;
			}
		}
#endif
	}
	return errno;
}

/*
 * Returns an errno
 */
int
chioctl(struct connection *conn, int cmd, caddr_t addr)
{
	struct packet *pkt;
	int flag;

	switch (cmd) {
		/*
		 * Read the first packet in the read queue for a connection.
		 * This call is primarily intended for those who want to read
		 * non-data packets (which are normally ignored) like RFC
		 * (for arguments in the contact string), CLS (for error string) etc.
		 * The reader's buffer is assumed to be CHMAXDATA long.  When ioctl's
		 * change on the VAX to have data length arguments, this will be done
		 * right. An error results if there is no packet to read.
		 * No hanging is currently provided for.
		 * The normal mode of operation for reading such packets is to
		 * first do a CHIOCGSTAT call to find out whether there is a packet
		 * to read (and what kind) and then make this call - except for
		 * RFC's when you know it must be there.
		 */
	case CHIOCPREAD:
		if ((pkt = conn->cn_rhead) == NULL)
			return ENXIO;
		addr = *(caddr_t *)addr;
		if (copyout((caddr_t)pkt->pk_cdata, addr, pkt->pk_len))
			return EFAULT;
		splimp();
		ch_read(conn);
		spl0();
		return 0;
		/*
		 * Change the mode of the connection.
		 * The default mode is CHSTREAM.
		 */
	case CHIOCSMODE:
		addr = *(caddr_t *)addr;
		switch ((int)addr) {
		case CHTTY:
#if NCHT > 0
			if (conn->cn_state == CSOPEN && conn->cn_mode != CHTTY && chttyconn(conn) == 0)
				return 0;
#endif
			return ENXIO;
		case CHSTREAM:
		case CHRECORD:
			if (conn->cn_mode == CHTTY)
				return EIO;
			conn->cn_mode = (int)addr;
			return 0;
		}
		return ENXIO;

		/*
		 * Like (CHIOCSMODE, CHTTY) but return a tty unit to open.
		 * For servers that want to do their own "getty" work.
		 */

	case CHIOCGTTY:

#if NCHT > 0
		if (((conn->cn_state == CSOPEN) || (conn->cn_state == CSRFCRCVD)) && conn->cn_mode != CHTTY) {
			int x = chtgtty(conn);

			*(int *)(addr) = x;
			if (x >= 0) {
				if (conn->cn_state == CSRFCRCVD)
					ch_accept(conn);
				conn->cn_mode = CHTTY;
				return 0;
			}
		}
#endif
		return ENXIO;

		/*
		 * Flush the current output packet if there is one.
		 * This is only valid in stream mode.
		 * If the argument is non-zero an error is returned if the
		 * transmit window is full, otherwise we hang.
		 */
	case CHIOCFLUSH:
		addr = *(caddr_t *)addr;
		if (conn->cn_mode == CHSTREAM) {
			splimp();
			while ((flag = ch_sflush(conn)) == CHTEMP)
				if (addr)
					break;
				else {
					conn->cn_sflags |= CHOWAIT;
					sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
				}
			spl0();
			return flag ? EIO : 0;
		}
		return ENXIO;
		/*
		 * Wait for all output to be acknowledged.  If addr is non-zero
		 * an EOF packet is also sent before waiting.
		 * If in stream mode, output is flushed first.
		 */
	case CHIOCOWAIT:
		addr = *(caddr_t *)addr;
		if (conn->cn_mode == CHSTREAM) {
			splimp();
			while ((flag = ch_sflush(conn)) == CHTEMP) {
				conn->cn_sflags |= CHOWAIT;
				sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
			}
			spl0();
			if (flag)
				return EIO;
		}
		if (addr) {
			splimp();
			while (chtfull(conn)) {
				conn->cn_sflags |= CHOWAIT;
				sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
			}
			flag = ch_eof(conn);
			spl0();
			if (flag)
				return EIO;
		}
		splimp();
		while (!chtempty(conn)) {
			conn->cn_sflags |= CHOWAIT;
			sleep((caddr_t)&conn->cn_thead, CHIOPRIO);
		}
		spl0();
		return conn->cn_state != CSOPEN ? EIO : 0;
		/*
		 * Return the status of the connection in a structure supplied
		 * by the user program.
		 */
	case CHIOCGSTAT:
		{
#define chst ((struct chstatus *)addr)

			chst->st_fhost = conn->cn_faddr;
			chst->st_cnum = conn->cn_ltidx;
			chst->st_rwsize = conn->cn_rwsize;
			chst->st_twsize = conn->cn_twsize;
			chst->st_state = conn->cn_state;
			chst->st_cmode = conn->cn_mode;
			chst->st_oroom = conn->cn_twsize - (conn->cn_tlast - conn->cn_tacked);
			if ((pkt = conn->cn_rhead) != NOPKT) {
				chst->st_ptype = pkt->pk_op;
				chst->st_plength = pkt->pk_len;
			} else {
				chst->st_ptype = 0;
				chst->st_plength = 0;
			}
			return 0;
		}
		/*
		 * Wait for the state of the connection to be different from
		 * the given state.
		 */
	case CHIOCSWAIT:
		addr = *(caddr_t *)addr;
		splimp();
		while (conn->cn_state == (int)addr)
			sleep((caddr_t)conn, CHIOPRIO);
		spl0();
		return 0;
		/*
		 * Answer an RFC.  Basically this call does nothing except
		 * setting a bit that says this connection should be of the
		 * datagram variety so that the connection automatically gets
		 * closed after the first write, whose data is immediately sent
		 * in an ANS packet.
		 */
	case CHIOCANSWER:
		flag = 0;
		splimp();
		if (conn->cn_state == CSRFCRCVD && conn->cn_mode != CHTTY)
			conn->cn_flags |= CHANSWER;
		else
			flag = EIO;
		spl0();
		return flag;
		/*
		 * Reject a RFC, giving a string (null terminated), to put in the
		 * close packet.  This call can also be used to shut down a connection
		 * prematurely giving an ascii close reason.
		 */
	case CHIOCREJECT:
		{
			struct chreject *cr;

			cr = (struct chreject *)addr;
			pkt = NOPKT;
			flag = 0;
			splimp();
			if (cr && cr->cr_length != 0 && cr->cr_length < CHMAXPKT && cr->cr_length >= 0 && (pkt = pkalloc(cr->cr_length, 0)) != NOPKT)
				if (copyin(cr->cr_reason, pkt->pk_cdata, cr->cr_length)) {
					ch_free((char *)pkt);
					flag = EFAULT;
				} else {
					pkt->pk_op = CLSOP;
					pkt->pk_lenword = cr->cr_length;
				}
			ch_close(conn, pkt, 0);
			spl0();
			return flag;
		}
		/*
		 * Accept an RFC causing the OPEN packet to be sent
		 */
	case CHIOCACCEPT:
		if (conn->cn_state == CSRFCRCVD) {
			ch_accept(conn);
			return 0;
		}
		return EIO;
		/*
		 * Count how many bytes can be immediately read.
		 */
	case FIONREAD:
		if (conn->cn_mode != CHTTY) {
			off_t nread = 0;

			for (pkt = conn->cn_rhead; pkt != NOPKT; pkt = pkt->pk_next)
				if (ISDATOP(pkt))
					nread += pkt->pk_len;
			if (conn->cn_rhead != NOPKT)
				nread -= conn->cn_roffset;

			*(int *)addr = nread;
			return 0;
		}
	}
	return ENXIO;
}

/*
 * Timeout routine that implements the chaosnet clock process.
 */
void
chtimeout(void)
{
	int s = splimp();

	ch_clock();
	timeout(chtimeout, 0, 1);
	splx(s);
}

/*
 * Routines to wake up processes sleeping on connections.  These were
 * macros before, but they were getting too hairy to be expanded inline.
 * --Bruce Nemnich, 6 May 85
 */

void
chrwake(struct connection *conn)
{
	if (conn->cn_sflags & CHCLOSING) {
		conn->cn_sflags &= ~CHCLOSING;
		clsconn(conn, CSCLOSED, NOPKT);
	} else {
		if (conn->cn_sflags & CHIWAIT) {
			conn->cn_sflags &= ~CHIWAIT;
			wakeup((char *)&conn->cn_rhead);
		}
		if (conn->cn_rsel) {
			selwakeup(conn->cn_rsel, conn->cn_flags & CHSELRCOLL);
			conn->cn_rsel = 0;
			conn->cn_flags &= ~CHSELRCOLL;
		}
		if (conn->cn_mode == CHTTY)
			chtrint(conn);
	}
}

void
chtwake(struct connection *conn)
{
	if (conn->cn_sflags & CHOWAIT) {
		conn->cn_sflags &= ~CHOWAIT;
		wakeup((char *)&conn->cn_thead);
	}
	if (conn->cn_tsel) {
		selwakeup(conn->cn_tsel, conn->cn_flags & CHSELTCOLL);
		conn->cn_tsel = 0;
		conn->cn_flags &= ~CHSELTCOLL;
	}
	if (conn->cn_mode == CHTTY)
		chtxint(conn);
}

#endif				/* BSD42 */
