/*
 * $Header: TTYLINK.c,v 1.2 85/01/12 16:43:31 dove Exp $
 * 	Server TTYLINK crock.
 *	The standard input is initially a connection which has been
 *	listened to, but neither accepted nor rejected.
 *	This disaster by mbm.
 * 1/20/84 dove
 *	nice(-100)
 *	remove alarm in wr()
 *	use TIOCOUTQ in wr()
 *
 * 1/23/84 dove
 * 	add a count to the "dc=" spec
 *
 * $Locker:  $
 * $Log:	TTYLINK.c,v $
 * Revision 1.2  85/01/12  16:43:31  dove
 * start searching pty's at "ptyqa"
 * 
 * Revision 1.1  85/01/12  16:42:32  dove
 * Initial revision
 * 
 */

#include <stdio.h>
#include <ctype.h>
#include <errno.h>
#include <signal.h>
#include <sgtty.h>
#include <sys/chaos.h>


#define TRUE -1
#define FALSE 0




int ppipe[2];			/* Pipe from net reader to net writer for parameters */

int conn = -1;			/* chaos connection */
int him;			/* process id of the other process */

struct chpacket pkt;
char *ptr = pkt.cp_data;
int cnt = 0;
char pty[20];
char sdfile[20];

main(argc, argv)
	int argc;
	char *argv[];
{
	int i, c, f, pid;

	nice(-100);
	pid = getpid();
	if (pipe(ppipe) < 0) {
		chreject(0, "Can't create pipe for second process");
		return (1);
	}
	for (c = 'q'; c < 'w'; c++)
		for (i = 10; i < 16; i++) {
			sprintf(pty, "/dev/pty%c%x", c, i);
			if ((f = open(pty, 2)) >= 0)
				goto win;
		}
	chreject(0, "?All PTYs in use");
	return (1);
      win:
	ioctl(0, CHIOCACCEPT, 0);
	conn = dup(0);
	close(0);
	close(1);
	dup(f);
	dup(f);			/* PTY now standard output/input */
	close(f);
	ioctl(conn, CHIOCSMODE, CHRECORD);
	sprintf(sdfile, "/tmp/sd-tty%s", rindex(pty, '/') + 4);
	him = fork();
	if (him == 0) {
		him = pid;
/*		iparams();	/* get the initial tty parameters */
		rd();
		shut();
	}
/*	gparams();		/* receive parameters */
	greet();		/* send greeting */
	wr();
	ioctl(conn, CHIOCOWAIT, 1);
	shut();
}

shut()
{
	kill(him, SIGKILL);	/* kill HIM !! */
	unlink(sdfile);
	exit(1);
}

#ifdef SUPDUP
/*
 * Read the parameters from the net and send them to the net writer.
 */

iparams()
{
	char temp[6];
	register int nwords;

	/*
	 * Read count 
	 */
	rd36(temp);
	nwords = -((-1 << 6) | temp[2]);
	if (nwords--) {
		/*
		 * Read and flush terminal type - TCTYP 
		 */
		rd36(0);
		if (nwords--) {
			rd36(ttyopt);	/* Read TTYOPT word */
			if (nwords--) {
				/*
				 * Read TCMXV word - number of lines 
				 */
				rd36(temp);
				tcmxv = (temp[5] & 0377) | ((temp[4] & 0377) << 6);
				if (nwords--) {
					/*
					 * Read TCMXH word - number of columns 
					 */
					rd36(temp);
					tcmxh = (temp[5] & 0377) | ((temp[4] & 0377) << 6);
					if (nwords--) {
						/*
						 * Read TTYROL word - scroll amount 
						 */
						rd36(temp);
						ttyrol = temp[5] & 077;
						while (nwords--)
							rd36(0);
					}
				}
			}
		}
	}
	if (write(ppipe[1], ttyopt, 6) != 6 || write(ppipe[1], &tcmxv, sizeof(tcmxv)) != sizeof(tcmxv) || write(ppipe[1], &tcmxh, sizeof(tcmxh)) != sizeof(tcmxh) || write(ppipe[1], &ttyrol, sizeof(ttyrol)) != sizeof(ttyrol))
		shut();
}

rd36(cp)
	register char *cp;
{
	register int i;

	for (i = 0; i < 6; i++)
		if (cp)
			*cp++ = rdconn();
		else
			rdconn();
}

gparams()
{
	char termbuf[BUFSIZ];
	int f;

	if (read(ppipe[0], ttyopt, 6) != 6 || read(ppipe[0], &tcmxv, sizeof(tcmxv)) != sizeof(tcmxv) || read(ppipe[0], &tcmxh, sizeof(tcmxh)) != sizeof(tcmxh) || read(ppipe[0], &ttyrol, sizeof(ttyrol)) != sizeof(ttyrol))
		shut();

	sprintf(termbuf, "SD|supdup|SUPDUP virtual terminal:co#%d:li#%d:", tcmxh, tcmxv);
	strcat(termbuf, "am:vb=\\177\\23:nd=\\177\\20:MT:");
	strcat(termbuf, "cl=\\177\\22:so=\\177\\31:se=\\177\\32:pt:");
	if (TOERS)
		strcat(termbuf, "ce=\\177\\05:ec=\\177\\06:cd=\\177\\04:");
	if (TOMVB)
		strcat(termbuf, "bs:");
	if (TOOVR)
		strcat(termbuf, "os:");
	if (TOMVU)
		strcat(termbuf, "up=\\177\\41:cm=\\177\\21%.%.:");
	if (TOLID)
		strcat(termbuf, "al=\\177\\25\\01:dl=\\177\\26\\01:");
	if (TOCID) {
		strcat(termbuf, "mi:im=:ei=:ic=\\177\\27\\01:");
		strcat(termbuf, "dc=\\177\\30\\01:dm=:ed=:");
	}
	if (!TOROL)
		strcat(termbuf, "ns:");
	if ((f = creat(sdfile, 0644)) >= 0) {
		write(f, termbuf, strlen(termbuf));
		close(f);
	}
}
#endif	/* SUPDUP */
greet()
{
	register char *cp;
	char buf[100];

	sprintf(buf, "\r\nUNIX TTYLINK Server\r\n");

	for (cp = buf; *cp; wrconn(*cp++ & 0377));
	chflush(DATOP);
	sleep(2);
/*	wrsup(TDCLR);*/
}

/* Flush the packet on alarm timers */
timer()
{
#ifndef BSD42
	signal(SIGALRM, timer);
#endif
	chflush(DATOP);
}

/* read from the user's tty and write to the network connection */
wr()
{
	register int c;
	struct chstatus foo;
	long waiting;
	extern int errno;

	signal(SIGALRM, timer);
      loop:
	while (errno = 0, (c = getchar()) != EOF) {
/*		wrsup(c);*/
		wrconn(c);
		/****************************************
		 * DEPENDS ON UNIX STDIO IMPLEMENTATION *
		 ****************************************/
		if (stdin->_cnt <= 0 && ioctl(fileno(stdin), TIOCOUTQ, &waiting) >= 0 && waiting == 0)	/* nothing more to read */
			chflush(DATOP);
	}
	if (errno == EINTR) {
		clearerr(stdin);
		goto loop;
	}
	return (0);
}

/* write a character to the connection */
wrconn(c)
	int c;
{
	*ptr++ = c;
	if (--cnt <= 0)
		chflush(DATOP);
}

/* flush the current packet to the network with specified opcode */
chflush(op)
	int op;
{
	if (ptr == pkt.cp_data)
		return;
	pkt.cp_op = op;
	write(conn, &pkt, ptr - pkt.cp_data + 1);
	ptr = pkt.cp_data;
	cnt = sizeof(pkt.cp_data);
}

/* read packets from the chaos net and stuff them into the unix pty */

rd()
{
	register int c, bucky;

	while (1) {
		if (emptyconn())
			fflush(stdout);
		c = rdconn();
		putchar(c);
	}
}

/* read a character from the network connection */
rdconn()
{
	char c;

	if (cnt-- > 0)
		return (*ptr++ & 0377);
	if ((cnt = read(conn, &pkt, sizeof(pkt))) <= 0)
		shut();
	switch (pkt.cp_op & 0377) {
	case DATOP:
		break;
	case CLSOP:
	case EOFOP:
	default:
		shut();
	}
	cnt--;			/* skip past the opcode */
	ptr = pkt.cp_data + 1;
	cnt--;
	return (pkt.cp_data[0] & 0377);
}

/* return true if nothing can be read form the connection */
emptyconn()
{
	long waiting;

	if (cnt <= 0 || ioctl(conn, FIONREAD, &waiting) < 0 || waiting == 0)
		return (TRUE);
	return (FALSE);
}

#ifdef SUPDUP
wrsup(c)
	register int c;
{
	register int i, j;
	static int state, count, bytes[4], wrapped, graphics;

      top:
	switch (state) {
	case SUPDUP_ESCAPE:
		c += 0176;
		state = 0;
	case 0:
		count = 0;
		if (c == SUPDUP_ESCAPE) {
			state = SUPDUP_ESCAPE;
			return;
		}
		if (c < 0200) {
			switch (c) {
			case '\7':
				wrconn(TDBEL);
				break;
			case '\b':
				if (currcol) {
					currcol--;
					wrconn(TDBS);
				} else if (currline == 0) {
					if (!TOROL) {
						currcol = tcmxh - 1;
						currline = tcmxv - 1;
						wcurpos();
					}
				} else {
					currcol = tcmxh - 1;
					currline--;
					wcurpos();
				}
				break;
			case '\r':
				if (currcol) {
					currcol = 0;
					wcurpos();
				}
				break;
			case '\n':
				if (wrapped) {
					c = TDCRL;
					goto top;
				}
				if (currline != tcmxv - 1)
					currline++;
				else
					goto scroll;
				wcurpos();
				break;
			case '\t':
				do {
					currcol++;
				} while (currcol & 7);
				if (currcol >= tcmxh) {
					i = currcol - tcmxh;
					wrsup('\r');
					wrsup('\n');
					currcol = i;
					while (i--)
						wrconn(TDFS);
				} else
					wcurpos();
				break;
			case ' ':
				wrconn(TDDLF);
				wrconn(TDFS);
			default:
				if (c <= 037 && !(ttyopt[1] & TOSA1)) {
					wrsup('^');
					c |= 0100;
				}
				if (c != ' ')
					wrconn(c);
				currcol++;
				if (currcol >= tcmxh) {
					wrsup('\r');
					wrsup('\n');
				}
			}
			return;
		} else
			switch (c) {
			case TDUP:
				if (currline) {
					currline--;
					wcurpos();
				}
				wrapped = 0;
				return;
			case TDBS:
				if (currcol) {
					currcol--;
					break;
				}
				return;
			case TDMOV:
			case TDMV1:
			case TDMV0:
			case TDILP:
			case TDDLP:
			case TDICP:
			case TDDCP:
			case TDGRF:
				state = c;
			case TDNOP:
			case TDEOF:
			case TDEOL:
			case TDDLF:
			case TDORS:
			case TDBEL:
			case TDBOW:
			case TDRST:
				break;
			case TDCRL:
				currcol = 0;
				if (currline != tcmxv - 1)
					currline++;
				else {
				      scroll:if (TOROL && ttyrol) {
						if (ttyrol > 1)
							currline -= ttyrol - 1;
						wrconn(TDCRL);
						if (currcol)
							wcurpos();
					} else {
						wrapped = 1;
						currline = 0;
						i = currcol;
						currcol = 0;
						wcurpos();
						if (TOERS)
							wrconn(TDEOL);
						if (i) {
							currcol = i;
							wcurpos();
						}
					}
					return;
				}

				break;
			case TDFS:
				currcol++;	/* Check for end of line? */
				break;
			case TDCLR:
				wrapped = 0;
				currcol = currline = 0;
				break;
			case TDQOT:	/* A gross hack to make getty work! */
			default:
				c &= 0177;	/* Send out the ascii */
				goto top;
			}
		break;
	case TDMOV:
		if (count == 4) {
			currline = bytes[2];
			currcol = bytes[3];
			state = 0;
			wrapped = 0;
			goto top;
		} else
			bytes[count++] = c;
		break;
	case TDMV0:
	case TDMV1:
		if (count == 2) {
			currline = bytes[0];
			currcol = bytes[1];
			state = 0;
			wrapped = 0;
			goto top;
		} else
			bytes[count++] = c;
		break;
	case TDQOT:
	case TDILP:
	case TDDLP:
		wrapped = 0;
	case TDICP:
	case TDDCP:
		state = 0;
		break;
	case TDGRF:
		if (c & 0200) {
			state = 0;
			goto top;
		}
	}
	wrconn(c);
}

wcurpos()
{
	wrconn(TDMV0);
	wrconn(currline);
	wrconn(currcol);
}
#endif	/* SUPDUP */
