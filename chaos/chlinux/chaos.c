#include <linux/anon_inodes.h>
#include <linux/device.h>
#include <linux/errno.h>
#include <linux/file.h>
#include <linux/fs.h>
#include <linux/init.h>
#include <linux/kernel.h>
#include <linux/module.h>
#include <linux/param.h>
#include <linux/poll.h>
#include <linux/proc_fs.h>
#include <linux/sched.h>
#include <linux/sched/signal.h>
#include <linux/seq_file.h>
#include <linux/signal.h>
#include <linux/slab.h>
#include <linux/timer.h>
#include <linux/types.h>
#include <linux/uaccess.h>

#include <asm/io.h>
#include <asm/ioctls.h>
#include <asm/irq.h>
#include <asm/segment.h>

#include "chaos.h"
#include "chlinux/chsys.h"
#include "chunix/chconf.h"
#include "chncp/chncp.h"

/*
 * Linux device driver interface to the Chaos N.C.P.
 *
 * The model is a pseudo device which "clones" FD's; not right in
 * today's world but it made sense in 1985.  Rather than make this a
 * full blown network family this just ports the existing
 * functionality.
 */

int initted;			/* NCP initialization flag */
int Rfcwaiting;			/* Someone waiting on unmatched RFC */
wait_queue_head_t Rfc_wait_queue;	/* rfc input wait queue */

#ifdef DEBUG_CHAOS
#define ASSERT(x,y)	if(!(x)) printk("%s: Assertion failure\n",y);
#else
#define ASSERT(x,y)
#endif

#define f_data private_data

DEFINE_SPINLOCK(chaos_lock);

extern struct chxcvr chetherxcvr[NCHETHER];

#define CHRMAJOR	80	/* pulled from thin air */

struct timer_list chtimer;
int chtimer_running;		/* timer is running */

ssize_t chrread(struct file *fp, char *buf, size_t count, loff_t *offset);
ssize_t chrwrite(struct file *file, const char *buf, size_t count, loff_t *offset);
long chrioctl(struct file *fp, unsigned int cmd, unsigned long addr);
int chropen(struct inode *inode, struct file *file);
int chrclose(struct inode *inode, struct file *file);
struct connection *chopen_conn(struct chopen *c, int wflag, int *errnop);
void chclose(struct connection *conn);
int chioctl(struct connection *conn, int cmd, caddr_t addr);
int chwaitforrfc(struct connection *conn, struct packet **ppkt);
int chwaitforoutput(struct connection *conn, int state);
int chwaitfornotstate_conn(struct connection *conn, int state);
void chtimeout(unsigned long);
ssize_t chf_read(struct file *fp, char *ubuf, size_t size, loff_t *offset);
ssize_t chf_write(struct file *fp, const char *ubuf, size_t size, loff_t *offset);
long chf_ioctl(struct file *fp, unsigned int cmd, unsigned long value);
unsigned int chf_poll(struct file *fp, struct poll_table_struct *wait);
int chf_flush(struct file *file, fl_owner_t id);
int chf_close(struct inode *inode, struct file *fp);
int chread(struct connection *conn, char *ubuf, int size);
int chwrite(struct connection *conn, const char *ubuf, int size);

/*
 * Glue between generic file descriptor and chaos connection code
 */
struct file_operations chfileops = {
	.read = chf_read,
	.write = chf_write,
	.poll = chf_poll,
	.unlocked_ioctl = chf_ioctl,
	.flush = chf_flush,
	.release = chf_close,
};

ssize_t
chf_read(struct file *fp, char *ubuf, size_t size, loff_t *offset)
{
	int ret;

	printk("chf_read(inode=%p, fp=%p)\n", file_inode(fp), fp);
	ASSERT(fp->f_op == &chfileops, "chf_read ent");
	ret = chread((struct connection *)fp->f_data, ubuf, size);
	ASSERT(fp->f_op == &chfileops, "chf_read exit");
	return ret;
}

ssize_t
chf_write(struct file *fp, const char *ubuf, size_t size, loff_t *offset)
{
	int ret;

	printk("chf_write(inode=%p, fp=%p)\n", file_inode(fp), fp);
	ASSERT(fp->f_op == &chfileops, "chf_write ent");
	ret = chwrite((struct connection *)fp->f_data, ubuf, size);
	ASSERT(fp->f_op == &chfileops, "chf_write exit");
	return ret;
}

long
chf_ioctl(struct file *fp, unsigned int cmd, unsigned long value)
{
	int ret;

	printk("chf_ioctl(inode=%p, fp=%p)\n", file_inode(fp), fp);
	ASSERT(fp->f_op == &chfileops, "chf_ioctl ent");
	ret = chioctl((struct connection *)fp->f_data, cmd, (caddr_t)value);
	ASSERT(fp->f_op == &chfileops, "chf_ioctl exit");
	return ret;
}

unsigned int
chf_poll(struct file *fp, struct poll_table_struct *wait)
{
	struct connection *conn = (struct connection *)fp->f_data;
	unsigned int mask;

	ASSERT(fp->f_op == &chfileops, "chf_select ent");

	mask = 0;

	poll_wait(fp, &conn->cn_read_wait, wait);
	poll_wait(fp, &conn->cn_write_wait, wait);

	spin_lock_irq(&chaos_lock);
	if (!chrempty(conn)) {
		mask |= POLLIN | POLLRDNORM;
	} else {
		conn->cn_sflags |= CHIWAIT;
	}
	spin_unlock_irq(&chaos_lock);

	spin_lock_irq(&chaos_lock);
	if (!chtfull(conn)) {
		mask |= POLLOUT | POLLWRNORM;
	} else {
		conn->cn_sflags |= CHOWAIT;
	}
	spin_unlock_irq(&chaos_lock);

	ASSERT(fp->f_op == &chfileops, "chf_poll exit");
	return mask;
}

int
chf_flush(struct file *fp, fl_owner_t id)
{
	return 0;
}

int
chf_close(struct inode *inode, struct file *fp)
{
	struct connection *conn = (struct connection *)fp->f_data;

	printk("chf_close(inode=%p, fp=%p) conn %p\n", inode, fp, conn);

	/*
	 * If this connection has been turned into a tty, then the
	 * tty owns it and we don't do anything.
	 */
	ASSERT(fp->f_op == &chfileops, "chf_close ent");
	if (conn && conn->cn_mode != CHTTY) {
		chclose(conn);
		fp->f_data = 0;
	}
}

char *
chwcopy(char *from, char *to, unsigned count, int uio, int *errorp)
{
	*errorp = verify_area(VERIFY_READ, (int *)to, count);
	if (*errorp)
		return 0;

	memcpy_fromfs(to, from, count);
	return to + count;
}

char *
chrcopy(char *from, char *to, unsigned count, int uio, int *errorp)
{
	*errorp = verify_area(VERIFY_WRITE, (int *)to, count);
	if (*errorp)
		return 0;

	memcpy_tofs(to, from, count);
	return to + count;
}

enum { NOTFULL = 1, EMPTY };	/* used by chwaitforoutput() */

int
chwaitforoutput(struct connection *conn, int state)
{
	int retval = 0;

	DECLARE_WAITQUEUE(wait, current);

	spin_lock_irq(&chaos_lock);
	add_wait_queue(&conn->cn_write_wait, &wait);
	while (1) {
		if ((state == NOTFULL && !chtfull(conn)) || (state == EMPTY && chtempty(conn)))
			break;

		conn->cn_sflags |= CHOWAIT;

		current->state = TASK_INTERRUPTIBLE;
		if (signal_pending(current)) {
			retval = -ERESTARTSYS;
			break;
		}
		schedule();
		/*
		 * sleep((caddr_t)&conn->cn_thead, CHIOPRIO); 
		 */
	}
	remove_wait_queue(&conn->cn_write_wait, &wait);
	current->state = TASK_RUNNING;
	spin_unlock_irq(&chaos_lock);
	return retval;
}

int
chwaitforflush(struct connection *conn, int *pflag)
{
	int retval = 0;

	DECLARE_WAITQUEUE(wait, current);

	spin_lock_irq(&chaos_lock);
	add_wait_queue(&conn->cn_write_wait, &wait);
	while ((*pflag = ch_sflush(conn)) == CHTEMP) {
		conn->cn_sflags |= CHOWAIT;

		current->state = TASK_INTERRUPTIBLE;
		if (signal_pending(current)) {
			retval = -ERESTARTSYS;
			break;
		}
		schedule();
		/*
		 * sleep((char *)&conn->cn_thead, CHIOPRIO); 
		 */
	}
	remove_wait_queue(&conn->cn_write_wait, &wait);
	current->state = TASK_RUNNING;
	spin_unlock_irq(&chaos_lock);
	return retval;
}

int
chwaitfornotstate_conn(struct connection *conn, int state)
{
	int retval = 0;

	DECLARE_WAITQUEUE(wait, current);

	printk("chwaitfornotstate_conn(%p, state=%d)\n", conn, state);
	spin_lock_irq(&chaos_lock);
	add_wait_queue(&conn->cn_state_wait, &wait);
	while (conn->cn_state == state) {
		current->state = TASK_INTERRUPTIBLE;
		if (signal_pending(current)) {
			retval = -ERESTARTSYS;
			break;
		}
		schedule();
		/*
		 * sleep((caddr_t)conn, CHIOPRIO); 
		 */
	}
	remove_wait_queue(&conn->cn_state_wait, &wait);
	current->state = TASK_RUNNING;
	spin_unlock_irq(&chaos_lock);
	printk("chwaitfornotstate_conn(state=%d) exit %d\n", state, retval);
	return retval;
}

/*
 * character driver access to the Chaos NCP
 * opened for one of two reasons:
 *	1. To perform basic NCP control functions, via minor device CHURFCMIN
 *	2. To open a connection via CHAOSMIN
 * The actual opening of the connection happens later via the CHIOCOPEN ioctl.
 */

int
chropen(struct inode *inode, struct file *file)
{
	unsigned int minor = MINOR(inode->i_rdev);
	int errno = 0;

	printk("chropen(inode=%p, fp=%p) minor=%d\n", inode, file, minor);

	ch_bufalloc();

	/*
	 * initialize the NCP somewhere else? 
	 */
	if (!initted) {
		chreset();	/* Reset drivers */
		chtimer_running++;
		chtimeout(0);	/* Start clock "process" */
		initted++;
	}

	if (minor == CHURFCMIN)
		if (Chrfcrcv == 0)
			Chrfcrcv++;
		else {
			errno = -ENXIO;
		}

	printk("chropen() returns %d\n", errno);
	return errno;
}

/*
 * Return a connection or return NULL and set *errnop to any error.
 */
struct connection *
chopen_conn(struct chopen *c, int wflag, int *errnop)
{
	struct connection *conn;
	struct packet *pkt;
	int rwsize, length;
	struct chopen cho;

	printk("chopen_conn(wflag=%d)\n", wflag);

	/*
	 * get main structure 
	 */
	*errnop = verify_area(VERIFY_READ, (int *)c, sizeof(struct chopen));
	if (*errnop) {
		printk("NOCONN\n");
		return NOCONN;
	}
	memcpy_fromfs((void *)&cho, (char *)c, sizeof(struct chopen));
	c = &cho;

	length = c->co_clength + c->co_length + (c->co_length ? 1 : 0);
	if (length > CHMAXPKT || c->co_clength <= 0) {
		*errnop = -E2BIG;
		printk("NOCONN (E2BIG)\n");
		return NOCONN;
	}
#if 1
	printk("c->co_length %d, c->co_clength %d, length %d\n", c->co_length, c->co_clength, length);
#endif

	pkt = pkalloc(length, 0);
	if (pkt == NOPKT) {
		*errnop = -ENOBUFS;
		printk("NOCONN (ENOBUFS)\n");
		return NOCONN;
	}
	if (c->co_length)
		pkt->pk_cdata[c->co_clength] = ' ';

	memcpy_fromfs(pkt->pk_cdata, c->co_contact, c->co_clength);
	if (c->co_length)
		memcpy_fromfs(&pkt->pk_cdata[c->co_clength + 1], c->co_data, c->co_length);

	rwsize = c->co_rwsize ? c->co_rwsize : CHDRWSIZE;
	SET_PH_LEN(pkt->pk_phead, length);
	conn = c->co_host ? ch_open(c->co_host, rwsize, pkt) : ch_listen(pkt, rwsize);
	if (conn == NOCONN) {
#if 1
		printk("NOCONN (ENXIO)\n");
#endif
		*errnop = -ENXIO;
		return NOCONN;
	}
#if 1
	printk("c->co_async %d\n", c->co_async);
#endif
	printk("conn %p\n", conn);
	if (!c->co_async) {
		/*
		 * We should hang until the connection changes from
		 * its initial state.
		 * If interrupted, flush the connection.
		 */

		/*
		 * current->timeout = (unsigned long) -1; 
		 */

		*errnop = chwaitfornotstate_conn(conn, c->co_host ? CSRFCSENT : CSLISTEN);
		if (*errnop) {
			rlsconn(conn);
			return NOCONN;
		}

		/*
		 * If the connection is not open, the open failed.
		 * Unless is got an ANS back.
		 */
		if (conn->cn_state != CSOPEN && (conn->cn_state != CSCLOSED || (pkt = conn->cn_rhead) == NOPKT || pkt->pk_op != ANSOP)) {
#if 1
			printk("open failed; cn_state %d\n", conn->cn_state);
#endif
			rlsconn(conn);
			*errnop = -EIO;
			return NOCONN;
		}
	}

	if (wflag)
		conn->cn_sflags |= CHFWRITE;
	conn->cn_sflags |= CHRAW;
	conn->cn_mode = CHSTREAM;
	printk("chopen_conn() done\n");
	return conn;
}

int
chrclose(struct inode *inode, struct file *file)
{
	unsigned int minor = MINOR(inode->i_rdev);

	printk("chrclose(inode=%p, fp=%p) minor=%d\n", inode, file, minor);

	if (minor == CHURFCMIN) {
		Chrfcrcv = 0;
		freelist(Chrfclist);
		Chrfclist = NOPKT;
	}
}

void
chclose(struct connection *conn)
{
	struct packet *pkt;

	printk("chclose(%p)\n", conn);
	switch (conn->cn_mode) {
	case CHTTY:
		panic("chclose on tty");
	case CHSTREAM:
		spin_lock_irq(&chaos_lock);
#if 0
		if (setjmp(&u.u_qsave)) {
			pkt = pktstr(NOPKT, "User interrupted", 16);
			if (pkt != NOPKT)
				pkt->pk_op = CLSOP;
			ch_close(conn, pkt, 0);
			goto shut;
		}
#endif
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
			chwaitforoutput(conn, NOTFULL);

			if (conn->cn_state == CSOPEN || conn->cn_state == CSRFCRCVD) {
				if (conn->cn_toutput) {
					ch_sflush(conn);
					goto checkfull;
				}
				if (conn->cn_state == CSOPEN)
					(void)ch_eof(conn);
			}

			chwaitforoutput(conn, EMPTY);
		} else if (conn->cn_state == CSOPEN) {
			/*
			 * If we are only reading then we should read the EOF
			 * before closing and wait for the other end to close.
			 */
			if (conn->cn_flags & CHEOFSEEN) {
				chwaitfornotstate_conn(conn, CSOPEN);
			}
		}
	      recclose:
		spin_unlock_irq(&chaos_lock);
		/*
		 * Fall into... 
		 */
	case CHRECORD:		/* Record oriented close is just sending a CLOSE */
		if (conn->cn_state == CSOPEN) {
			pkt = pkalloc(0, 0);
			if (pkt != NOPKT) {
				pkt->pk_op = CLSOP;
				SET_PH_LEN(pkt->pk_phead, 0);
			}
			ch_close(conn, pkt, 0);
		}
	}
      shut:
	spin_unlock_irq(&chaos_lock);
	ch_close(conn, NOPKT, 1);
	ch_buffree();
}

/*
 * Raw read routine.
 */
ssize_t
chrread(struct file *fp, char *buf, size_t count, loff_t *offset)
{
	struct connection *conn = (struct connection *)fp->f_data;
	unsigned int minor = iminor(file_inode(fp));
	struct packet *pkt;
	int errno = 0;

	printk("chrread(inode=%p, fp=%p) minor=%d\n", file_inode(fp), fp, minor);

	/*
	 * only CHURFCMIN is readable as char device 
	 */
	if (minor == CHURFCMIN) {
		if ((errno = chwaitforrfc(conn, &pkt)) != 0)
			return errno;

		if (count < PH_LEN(pkt->pk_phead))
			errno = -EIO;
		else {
			memcpy_tofs(buf, (caddr_t)pkt->pk_cdata, PH_LEN(pkt->pk_phead));
			errno = PH_LEN(pkt->pk_phead);
		}
	} else
		errno = -ENXIO;

	return errno;
}

int
chwaitforrfc(struct connection *conn, struct packet **ppkt)
{
	int retval = 0;

	DECLARE_WAITQUEUE(wait, current);

	spin_lock_irq(&chaos_lock);
	add_wait_queue(&Rfc_wait_queue, &wait);
	while (1) {
		if ((*ppkt = ch_rnext()) != NOPKT)
			break;
		Rfcwaiting++;
		current->state = TASK_INTERRUPTIBLE;
		if (signal_pending(current)) {
			retval = -ERESTARTSYS;
			break;
		}
		/*
		 * sleep((caddr_t)&Chrfclist, CHIOPRIO); 
		 */
		schedule();
	}
	remove_wait_queue(&Rfc_wait_queue, &wait);
	current->state = TASK_RUNNING;
	spin_unlock_irq(&chaos_lock);
	return retval;
}

int
chwaitfordata(struct connection *conn)
{
	int retval = 0;

	DECLARE_WAITQUEUE(wait, current);

	printk("chwaitfordata(%p)\n", conn);
	spin_lock_irq(&chaos_lock);
	add_wait_queue(&conn->cn_read_wait, &wait);
	while (chrempty(conn)) {
		conn->cn_sflags |= CHIWAIT;

		current->state = TASK_INTERRUPTIBLE;
		if (signal_pending(current)) {
			retval = -ERESTARTSYS;
			break;
		}
		schedule();
		/*
		 * sleep((char *)&conn->cn_rhead, CHIOPRIO); 
		 */
	}
	remove_wait_queue(&conn->cn_read_wait, &wait);
	current->state = TASK_RUNNING;
	spin_unlock_irq(&chaos_lock);
	return retval;
}

/*
 * Return an errno on error
 */
int
chread(struct connection *conn, char *ubuf, int size)
{
	struct packet *pkt;
	int count, errno;

	printk("chread(%p)\n", conn);
	switch (conn->cn_mode) {
	case CHTTY:
		return -ENXIO;
	case CHSTREAM:
		printk("chread() CHSTREAM\n");
		if (conn->cn_state == CSRFCRCVD)
			ch_accept(conn);
		for (count = size; size != 0;) {
			int error, n;

			n = ch_sread(conn, ubuf, (unsigned)size, 0, &error);
			if (error)
				return error;
			switch (n) {
			case 0:	/* No data to read */
				if (count != size)
					return 0;
				if ((errno = chwaitfordata(conn)) != 0)
					return errno;
				continue;
			case CHEOF:
				return 0;
			default:
				if (n < 0)
					return -EIO;
				return n;
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
		printk("chread() CHRECORD size %d\n", size);
		if ((errno = chwaitfordata(conn)) != 0)
			return errno;
#ifdef DEBUG_CHAOS
		if ((pkt = conn->cn_rhead) != NOPKT)
			printk("chread() CHRECORD pk_len %d, size %d\n", PH_LEN(pkt->pk_phead), size);
#endif
		if ((pkt = conn->cn_rhead) == NOPKT || PH_LEN(pkt->pk_phead) + 1 > size)	/* + 1 for opcode */
			return -EIO;
		else {
			int errno;

			errno = verify_area(VERIFY_WRITE, (int *)ubuf, 1 + PH_LEN(pkt->pk_phead));
			if (errno == 0) {
				memcpy_tofs(ubuf, &pkt->pk_op, 1);
				memcpy_tofs(ubuf + 1, pkt->pk_cdata, PH_LEN(pkt->pk_phead));
				errno = PH_LEN(pkt->pk_phead) + 1;

				spin_lock_irq(&chaos_lock);
				ch_read(conn);
				spin_unlock_irq(&chaos_lock);
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
ssize_t
chrwrite(struct file *file, const char *buf, size_t count, loff_t *offset)
{
	unsigned int minor = iminor(file_inode(file));
	printk("chrwrite(inode=%p, fp=%p) minor=%d\n", file_inode(file), file, minor);

	return -ENXIO;
}

/*
 * Return an errno or 0
 */
int
chwrite(struct connection *conn, const char *ubuf, int size)
{
	struct packet *pkt;
	int errno;

	printk("chwrite(%p)\n", conn);
	if (conn->cn_state == CSRFCRCVD)
		ch_accept(conn);
	switch (conn->cn_mode) {
	case CHTTY:
		/*
		 * Fall into (on RAW mode only) 
		 */
	case CHSTREAM:
		while (size != 0) {
			int n;

			n = ch_swrite(conn, ubuf, (unsigned)size, 0, &errno);
			if (errno)
				return errno;
			switch (n) {
			case 0:
			case CHTEMP:	/* output window is full */
				if ((errno = chwaitforoutput(conn, NOTFULL)) != 0)
					return errno;
				continue;
			default:
				if (n < 0)
					return -EIO;
				return n;
			}
		}
		return 0;
	case CHRECORD:		/* One write call -> one packet */
		if (size < 1 || size - 1 > CHMAXDATA || conn->cn_state == CSINCT)
			return -EIO;

		printk("chwrite() tlast %d, tacked %d, twsize %d\n", conn->cn_tlast, conn->cn_tacked, conn->cn_twsize);

		if ((errno = chwaitforoutput(conn, NOTFULL)) != 0)
			return errno;

#if 0
		while ((pkt = pkalloc((int)(size - 1), 0)) == NOPKT) {
			/*
			 * block if we can't get a packet 
			 */
			sleep((caddr_t)&lbolt, CHIOPRIO);
		}
#else
		if ((pkt = pkalloc((int)(size - 1), 0)) == NOPKT)
			return -EIO;
#endif

		memcpy_fromfs(&pkt->pk_op, ubuf, 1);
		SET_PH_LEN(pkt->pk_phead, size - 1);
		if (size)
			memcpy_fromfs(pkt->pk_cdata, ubuf + 1, size - 1);

		spin_lock_irq(&chaos_lock);
		if (ch_write(conn, pkt))
			errno = -EIO;
		else
			errno = size;
		spin_unlock_irq(&chaos_lock);

		return errno;
	}
	/*
	 * NOTREACHED 
	 */
}

/*
 * Raw ioctl routine - perform non-connection functions, otherwise call down
 * errno holds error return value until "out:"
 */
long
chrioctl(struct file *fp, unsigned int cmd, unsigned long addr)
{
	unsigned int minor = iminor(file_inode(fp));
	int errno = 0;

	printk("chrioctl(inode=%p, fp=%p) minor=%d\n", file_inode(fp), fp, minor);
	if (minor == CHURFCMIN) {
		switch (cmd) {
			/*
			 * Skip the first unmatched RFC at the head of the queue
			 * and mark it so that ch_rnext will never pick it up again.
			 */
		case CHIOCRSKIP:
			spin_lock_irq(&chaos_lock);
			ch_rskip();
			spin_unlock_irq(&chaos_lock);
			break;

		case CHIOCETHER:
			errno = cheaddr(addr);
			break;

		case CHIOCNAME:
			errno = verify_area(VERIFY_READ, (int *)addr, CHSTATNAME);
			if (errno == 0)
				memcpy_fromfs(Chmyname, (char *)addr, CHSTATNAME);
			break;

			/*
			 * Specify my own network number.
			 */
		case CHIOCADDR:
			Chmyaddr = addr;
			break;
		}
	} else {
		int fd;
		struct connection *chdata;

		if (cmd != CHIOCOPEN)
			return -ENXIO;

		chdata = chopen_conn((struct chopen *)addr, fp->f_flags & (O_WRONLY | O_RDWR), &errno);
		if (chdata == NULL)
			printk("chdata == NULL!!!\n");
		fd = anon_inode_getfd("chaos", &chfileops, chdata, 0);
		if (IS_ERR(fd)) {
			printk("no file\n");
			return -ENFILE;
		}
		errno = fd;
	}
	printk("errno = %d\n", errno);
	return errno;
}

/*
 * Returns an errno
 */
int
chioctl(struct connection *conn, int cmd, caddr_t addr)
{
	struct packet *pkt;
	int flag, retval;

	printk("chioctl(%p)\n", conn);
	switch (cmd) {
		/*
		 * Read the first packet in the read queue for a connection.
		 * This call is primarily intended for those who want to read
		 * non-data packets (which are normally ignored) like RFC
		 * (for arguments in the contact string), CLS (for error string) etc.
		 * The reader's buffer is assumed to be CHMAXDATA long.
		 *
		 * An error results if there is no packet to read.
		 * No hanging is currently provided for.
		 * The normal mode of operation for reading such packets is to
		 * first do a CHIOCGSTAT call to find out whether there is a packet
		 * to read (and what kind) and then make this call - except for
		 * RFC's when you know it must be there.
		 */
	case CHIOCPREAD:
		printk("CHIOCPREAD\n");
		if ((pkt = conn->cn_rhead) == NULL)
			return -ENXIO;

		retval = verify_area(VERIFY_WRITE, (int *)addr, PH_LEN(pkt->pk_phead));
		if (retval)
			return retval;

		memcpy_tofs((int *)addr, (caddr_t)pkt->pk_cdata, PH_LEN(pkt->pk_phead));

		spin_lock_irq(&chaos_lock);
		ch_read(conn);
		spin_unlock_irq(&chaos_lock);
		return 0;

		/*
		 * Change the mode of the connection.
		 * The default mode is CHSTREAM.
		 */
	case CHIOCSMODE:
		printk("CHIOCSMODE conn %p\n", conn);
		switch ((int)addr) {
		case CHTTY:
#if NCHT > 0
			if (conn->cn_state == CSOPEN && conn->cn_mode != CHTTY && chttyconn(conn) == 0)
				return 0;
#endif
			return -ENXIO;
		case CHSTREAM:
		case CHRECORD:
			if (conn->cn_mode == CHTTY)
				return -EIO;
			conn->cn_mode = (int)addr;
			return 0;
		}
		return -ENXIO;

		/*
		 * Like (CHIOCSMODE, CHTTY) but return a tty unit to open.
		 * For servers that want to do their own "getty" work.
		 */
	case CHIOCGTTY:
		printk("CHIOCGTTY\n");

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
		return -ENXIO;

		/*
		 * Flush the current output packet if there is one.
		 * This is only valid in stream mode.
		 * If the argument is non-zero an error is returned if the
		 * transmit window is full, otherwise we hang.
		 */
	case CHIOCFLUSH:
		printk("CHIOCFLUSH\n");
		if (conn->cn_mode == CHSTREAM) {
			if (addr) {
				flag = ch_sflush(conn);
			} else {
				if ((retval = chwaitforflush(conn, &flag)) != 0)
					return retval;
			}
			return flag ? -EIO : 0;
		}
		return -ENXIO;

		/*
		 * Wait for all output to be acknowledged.  If addr is non-zero
		 * an EOF packet is also sent before waiting.
		 * If in stream mode, output is flushed first.
		 */
	case CHIOCOWAIT:
		printk("CHIOCOWAIT\n");
		if (conn->cn_mode == CHSTREAM) {
			if ((retval = chwaitforflush(conn, &flag)) != 0)
				return retval;
			if (flag)
				return -EIO;
		}
		if (addr) {
			if ((retval = chwaitforoutput(conn, NOTFULL)) != 0)
				return retval;
			spin_lock_irq(&chaos_lock);
			flag = ch_eof(conn);
			spin_unlock_irq(&chaos_lock);
			if (flag)
				return -EIO;
		}
		if ((retval = chwaitforoutput(conn, EMPTY)) != 0)
			return retval;
		return conn->cn_state != CSOPEN ? EIO : 0;

		/*
		 * Return the status of the connection in a structure supplied
		 * by the user program.
		 */
	case CHIOCGSTAT:
		printk("CHIOCGSTAT conn %p\n", conn);
		if (conn == 0)
			return -ENXIO;

		{
			struct chstatus chst;
			int errno;

			chst.st_fhost = CH_ADDR_SHORT(conn->cn_faddr);
			chst.st_cnum = conn->cn_ltidx;
			chst.st_rwsize = conn->cn_rwsize;
			chst.st_twsize = conn->cn_twsize;
			chst.st_state = conn->cn_state;
			chst.st_cmode = conn->cn_mode;
			chst.st_oroom = conn->cn_twsize - (conn->cn_tlast - conn->cn_tacked);
			if ((pkt = conn->cn_rhead) != NOPKT) {
				printk("pkt %p\n", pkt);
				chst.st_ptype = pkt->pk_op;
				chst.st_plength = PH_LEN(pkt->pk_phead);
			} else {
				chst.st_ptype = 0;
				chst.st_plength = 0;
			}

			errno = verify_area(VERIFY_WRITE, (int *)addr, sizeof(struct chstatus));
			if (errno)
				return errno;

			memcpy_tofs((int *)addr, (caddr_t)&chst, sizeof(struct chstatus));

			printk("done\n");
			return 0;
		}

		/*
		 * Wait for the state of the connection to be different from
		 * the given state.
		 */
	case CHIOCSWAIT:
		printk("CHIOCSWAIT\n");
		if ((retval = chwaitfornotstate_conn(conn, (int)addr)) != 0)
			return retval;
		return 0;

		/*
		 * Answer an RFC.  Basically this call does nothing except
		 * setting a bit that says this connection should be of the
		 * datagram variety so that the connection automatically gets
		 * closed after the first write, whose data is immediately sent
		 * in an ANS packet.
		 */
	case CHIOCANSWER:
		printk("CHIOCANSWER\n");
		flag = 0;
		spin_lock_irq(&chaos_lock);
		if (conn->cn_state == CSRFCRCVD && conn->cn_mode != CHTTY)
			conn->cn_flags |= CHANSWER;
		else
			flag = EIO;
		spin_unlock_irq(&chaos_lock);
		return flag;

		/*
		 * Reject a RFC, giving a string (null terminated), to put in the
		 * close packet.  This call can also be used to shut down a connection
		 * prematurely giving an ascii close reason.
		 */
	case CHIOCREJECT:
		printk("CHIOCREJECT\n");
		{
			struct chreject cr;

			retval = verify_area(VERIFY_READ, (int *)addr, sizeof(struct chreject));
			if (retval)
				return retval;

			memcpy_fromfs(&cr, (int *)addr, sizeof(struct chreject));

			pkt = NOPKT;
			flag = 0;
			spin_lock_irq(&chaos_lock);
			if (cr.cr_length != 0 && cr.cr_length < CHMAXPKT && cr.cr_length >= 0 && (pkt = pkalloc(cr.cr_length, 0)) != NOPKT) {
				retval = verify_area(VERIFY_READ, (int *)cr.cr_reason, cr.cr_length);
				if (retval) {
					ch_free((char *)pkt);
					return retval;
				}

				memcpy_fromfs(pkt->pk_cdata, cr.cr_reason, cr.cr_length);

				pkt->pk_op = CLSOP;
				SET_PH_LEN(pkt->pk_phead, cr.cr_length);
			}
			ch_close(conn, pkt, 0);
			spin_unlock_irq(&chaos_lock);
			return flag;
		}

		/*
		 * Accept an RFC causing the OPEN packet to be sent
		 */
	case CHIOCACCEPT:
		printk("CHIOCACCEPT conn %p\n", conn);
		if (conn->cn_state == CSRFCRCVD) {
			ch_accept(conn);
			return 0;
		}
		return -EIO;

		/*
		 * Count how many bytes can be immediately read.
		 */
	case FIONREAD:
		printk("FIONREAD\n");
		if (conn->cn_mode != CHTTY) {
			off_t nread = 0;
			int nr, errno;

			for (pkt = conn->cn_rhead; pkt != NOPKT; pkt = pkt->pk_next)
				if (ISDATOP(pkt))
					nread += PH_LEN(pkt->pk_phead);
			if (conn->cn_rhead != NOPKT)
				nread -= conn->cn_roffset;

			errno = verify_area(VERIFY_WRITE, (int *)addr, sizeof(int));
			if (errno)
				return errno;

			nr = nread;
			memcpy_tofs((int *)addr, (caddr_t)&nr, sizeof(int));
			printk("FIONREAD returns %d bytes\n", nr);
			return 0;
		}
	}
	return -ENXIO;
}

/*
 * Timeout routine that implements the chaosnet clock process.
 * (called periodically)
 */
void
chtimeout(unsigned long t)
{
	spin_lock_irq(&chaos_lock);
	ch_clock();

	if (chtimer_running) {
		del_timer(&chtimer);
		chtimer.expires = jiffies + 1;
#if 0 /*---!!! <linux/timer.h> has changed its API. */
		chtimer.function = chtimeout;
		chtimer.data = (unsigned long)0;
#endif
		add_timer(&chtimer);
	}

	spin_unlock_irq(&chaos_lock);
}

void
chtimeout_stop()
{
	spin_lock_irq(&chaos_lock);
	chtimer_running = 0;
	del_timer(&chtimer);
	spin_unlock_irq(&chaos_lock);
}

void
praddr(struct seq_file *m, short h)
{
	seq_printf(m, "%02d.%03d", ((chaddr *) & h)->subnet & 0377, ((chaddr *) & h)->host & 0377);
}

void
pridle(struct seq_file *m, chtime n)
{
	int idle;

	idle = Chclock - n;
	if (idle < 0)
		idle = 0;
	if (idle < Chhz)
		seq_printf(m, "%3dt", idle);
	else {
		idle += Chhz / 2;
		idle /= Chhz;
		if (idle < 100)
			seq_printf(m, "%3ds", idle);
		else if (idle < 60 * 99)
			seq_printf(m, "%3dm", (idle + 30) / 60);
		else
			seq_printf(m, "%3dh", (idle + 60 * 30) / (60 * 60));
	}
}

void
prconn(struct seq_file *m, struct connection *conn, int i)
{
	/*
	 * /---!!! See chstat for what to do. 
	 */
}

void
prrfcs(struct seq_file *m)
{
	/*
	 * /---!!! See chstat for what to do. 
	 */
}

char *xstathead = " Received Transmitted CRC(xcvr) CRC(buff) Overrun Aborted Length Rejected";

void
prxcvr(struct seq_file *m, char *name, struct chxcvr *xcvr, int nx)
{
	struct chxcvr *xp = xcvr;
	int i;

	for (i = 0; i < nx; i++, xp++) {
		if (xp->xc_etherinfo.bound_dev == 0)
			break;
		seq_printf(m, "%-8s %2d Netaddr: ", name, i);
		praddr(m, CH_ADDR_SHORT(xcvr->xc_addr));
		seq_printf(m, " Devaddr: %x\n", xp->xc_devaddr);
		seq_printf(m, "%s\n%9ld%12ld%10ld%8ld%8ld%8ld%8ld%8ld\n", xstathead, xp->xc_rcvd, xp->xc_xmtd, xp->xc_crcr, xp->xc_crci, xp->xc_lost, xp->xc_abrt, xp->xc_leng, xp->xc_rej);
		seq_printf(m, " Tpacket Ttime Rpacket Rtime");
		seq_printf(m, "\n%6x ", xp->xc_tpkt);
		pridle(m, xp->xc_ttime);
		seq_printf(m, " %6x ", xp->xc_rpkt);
		pridle(m, xp->xc_rtime);
		seq_printf(m, "\n");
	}
}

/* This behaves like the chstat command.  */
int
chaos_proc_show(struct seq_file *m, void *v)
{
	int i;

	seq_printf(m, "Myaddr is 0%o", Chmyaddr);
	seq_printf(m, "\nConnections:\n # T St  Remote Host  Idx Idle Tlast Trecd Tackd Tw Rlast Rackd Rread Rw Flags\n");
	for (i = 0; i < CHNCONNS; i++)
		if (Chconntab[i] != NOCONN)
			prconn(m, Chconntab[i], i);
	if (Chrfclist)
		prrfcs(m);
	prxcvr(m, "ETHER", chetherxcvr, NCHETHER);
	return 0;
}

int
chaos_proc_open(struct inode *inode, struct file *file)
{
	printk("inode=%p, file=%p\n", inode, file);
	return single_open(file, chaos_proc_show, NULL);
}

struct file_operations chaos_proc_fops = {
	.open = chaos_proc_open,
	.read = seq_read,
	.llseek = seq_lseek,
	.release = single_release,
};

struct file_operations choas_fops = {
	.read = chrread,
	.write = chrwrite,
	.unlocked_ioctl = chrioctl,
	.open = chropen,
	.release = chrclose
};

int __init
chaos_init(void)
{
	int ret = 0;

	printk("chaos_init()\n");

	if (register_chrdev(CHRMAJOR, "chaos", &choas_fops)) {
		printk("chaos: unable to get chaos major %d\n", CHRMAJOR);
		return -EIO;
	}

      /*---!!! Create the proc entry in /proc/net/chaos. */
	if (!proc_create("chaos", 0, NULL, &chaos_proc_fops)) {
		printk(KERN_ALERT "failed to create proc entry\n");
		ret = -ENOMEM;
	}
	printk("created /proc/chaos\n");

	printk("chaos_init() ok\n");
	return 0;
      err:
	printk("chaos_init() FAIL:\n", ret);
	return ret;
}

void __exit
chaos_deinit(void)
{
	printk("chaos_deinit()\n");

	chtimeout_stop();
	chdeinit();
	unregister_chrdev(CHRMAJOR, "chaos");
	remove_proc_entry("chaos", NULL);
}

/*
 * loadable module initialization
 */

MODULE_LICENSE("GPL");
MODULE_VERSION("0");

module_init(chaos_init);
module_exit(chaos_deinit);
