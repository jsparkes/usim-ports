#ifndef _CHSYS_
#define _CHSYS_

/*
 * Operating system dependent definitions for UNIX (currently on vax)
 * This file contains definitions which must be supplied to the system
 * independent parts of the NCP.
 * It should be minimal.
 */

#ifdef vax
#include "cht.h"
#endif

#define CHSTRCODE		/* UNIX interface needs stream code */

/*
 * OP Sys dependent part of connection structure
 */
struct csys_header {
#if NCHT > 0
	struct tty *csys_ttyp;	/* tty struct ptr if there is one */
#endif
	char csys_mode;		/* How we use this connection */
	char csys_flags;	/* System dependent flags */
	struct wait_queue *csys_state_wait;	/* state change wait queue */
	struct wait_queue *csys_write_wait;	/* select wait queue */
	struct wait_queue *csys_read_wait;	/* select wait queue */
};

/*
 * cn_sflags definitions.
 */
#define CHIWAIT		01	/* Someone waiting on input */
#define CHOWAIT		02	/* Someone waiting to output */
#define CHRAW		04	/* this channel is open by raw driver */
#define CHCLOSING	010	/* Top end is closing output.  Any input
				 * should abort the connection */
#define CHFWRITE	020	/* Connection is open for writing */

#define cn_mode		cn_syshead.csys_mode
#define cn_sflags	cn_syshead.csys_flags
#define cn_ttyp		cn_syshead.csys_ttyp
#define cn_state_wait	cn_syshead.csys_state_wait
#define cn_rfc_wait	cn_syshead.csys_rfc_wait
#define cn_write_wait	cn_syshead.csys_write_wait
#define cn_read_wait	cn_syshead.csys_read_wait

#if NCHT ==  0
#define chtnstate(a)
#define chtxint(a)
#define chtrint(a)
#endif

/*
 * macro definitions for process wakeup
 */
#define NEWSTATE(x)
#define INPUT(x) server_input(x)
#define OUTPUT(x)
#define RFCINPUT

/*
 * These should be lower if software interrupts are used.
 */
#ifdef BSD42
#define CHLOCK		(void) splimp()
#define CHUNLOCK	(void) spl0()
#else
#define CHLOCK
#define CHUNLOCK
#endif

#define NOINPUT(conn)
#define NOOUTPUT(conn)

extern int Rfcwaiting;
extern struct wait_queue *Rfc_wait_queue;	/* rfc input wait queue */

#define CHWCOPY(from, to, count, arg, errorp) \
		(char *)chwcopy(from, to, count, arg, errorp)
#define CHRCOPY(from, to, count, arg, errorp) \
		(char *)chrcopy(from, to, count, arg, errorp)

#endif
