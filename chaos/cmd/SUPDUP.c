/*
 *    Server SUPDUP program.
 *    The standard input is initially a connection which has been
 *    listened to, but neither accepted nor rejected.
 */

/* NOTES
 *
 *    First pass DCP, current code with cursor hacking JEK.
 *    
 *    Hacked by CJL so much that it will only work in 4.2BSD.
 *    Creates it's own login.
 *    
 * 1/19/85 dove
 *	force meta chars tolower for 3600 supdup
 *	accept ctrl(' ') as null for 3600 supdup
 *	set vc#200 when ns for 3600 supdup
 *
 * 1/23/85 dove
 *	skip '/dev/' from tty when searching utmp
 *
 * 2/27/85 dove
 *  don't admit to reverse video if "TOROL" is not set (lispm)
 *
 * 3/4/85 dove
 *  add delay timing to noscroll al and dl (for lispm)
 *  add LCRTBS to modes
 *  ignore nulls on output
 *  add :km: for 3600
 *
 * 3/26/85 dove
 *  correct wr() to unsigned char *p
 *
 * 7/12/85 dove (old file is SUPDUP.c3)
 *  add AL,DL for lispm, change pad on those and al,dl to constant value
 */

#include <stdio.h>
#include <ctype.h>
#include <errno.h>
#include <signal.h>
#include <sgtty.h>
#include <utmp.h>
#include <sys/time.h>
#include <sys/resource.h>
#include <sys/file.h>
#include <sys/wait.h>
#include <sys/chaos.h>

/*
 * Characters output from user programs are checked for \b, \r, and \n.
 * These are translated into SUPDUP cursor movement.  Thus this disallows
 * the use of these 3 SAIL characters.
 * SUPDUP display codes are passed through, but looked at to determine
 * the current cursor position.  This is necessary for simulating \b, \n, \r
 * and the TDBS and TDUP fake SUPDUP codes supplied in the termcap entries.
 * Input from the network is checked for ITP escapes, mainly for bucky
 * bits and terminal location string.
 */

#define TRUE -1
#define FALSE 0

/*
 * Since various parts of UNIX just can't hack the 0200 bit, we define
 * a fake lead-in escape - 0177, to avoid a sail graphic that is used
 * sometimes...
 */
#define SUPDUP_ESCAPE 0177

/* ITP bits (from the 12-bit character set */
#define ITP_ASCII    00177	/* ascii part of character */
#define ITP_CTL      00200	/* CONTROL key depressed */
#define ITP_MTA      00400	/* META key depressed */
#define ITP_TOP      04000	/* TOP key depressed */
#define CTL_PREFIX   00237	/* c-m-_ is control prefix */
#define ITP_QUOTE    00034	/* quoting character */
#define ITP_CHAR(char1,char2) (((char1 & 037) << 7) + char2)

#define ASCII_CTL_MASK ~(0177-037)
#define ASCII_ESCAPE    033
#define ASCII_PART(char) (char & ITP_ASCII)

#define ITP_ESCAPE    0300
#define ITP_LOGOUT    0301
#define ITP_LOCATION  0302

#define TDMOV    0200
#define TDMV1    0201
#define TDEOF    0202
#define TDEOL    0203
#define TDDLF    0204
#define TDCRL    0207
#define TDNOP    0210
#define TDBS     0211
#define TDLF     0212
#define TDCR     0213
#define TDORS    0214
#define TDQOT    0215
#define TDFS     0216
#define TDMV0    0217
#define TDCLR    0220
#define TDBEL    0221
#define TDILP    0223
#define TDDLP    0224
#define TDICP    0225
#define TDDCP    0226
#define TDBOW    0227
#define TDRST    0230
#define TDGRF    0231
#define TDSCU    0232		/* Scroll region up */
#define TDSCD    0233		/* Scroll region down */
#define TDUP     0237		/* Interpreted locally, not in supdup at all */
int currcol, currline;		/* Current cursor position */

/* These variables are set at initial connection time */
char ttyopt[6];

#define TOERS    (ttyopt[0] & 4)	/* Terminal can erase */
#define TOMVB    (ttyopt[0] & 1)	/* can move backwards */
#define TOOVR    (ttyopt[1] & 010)	/* Over printing */
#define TOMVU    (ttyopt[1] & 4)	/* can move up */
#define TOLID    (ttyopt[2] & 2)	/* Line insert/delete */
#define TOCID    (ttyopt[2] & 1)
#define TOSA1    (ttyopt[1] & 020)	/* send SAIL characters direct */
#define TOMOR    (ttyopt[1] & 2)	/* Do more processing */
#define TOROL    (ttyopt[1] & 1)	/* Do scrolling */
#define TPPRN    (ttyopt[4] & 2)	/* Swap parans and brackets */
short ttyrol;			/* How much the terminal scrolls by */
short tcmxv;			/* Number of lines */
short tcmxh;			/* Number of columns */

#define WRCONN(c)	{*ptr++ = (c); if (--cnt <= 0) chflush(DATOP);}
#define WCURPOS		{WRCONN(TDMV0); WRCONN(currline); WRCONN(currcol);}
#define RDCONN 		((cnt-- > 0) ? (*ptr++ & 0377) : rdpkt())
#define SCPYN(a, b)	strncpy(a, b, sizeof(a))
#define SCMPN(a, b)	strncmp(a, b, sizeof(a))
#define BANNER		"\r\n%s\r\n%s"

int conn = -1;			/* chaos connection */
int him = 0;			/* process id of the other process */
int job_pid = 0;		/* process id of the user job */

extern int errno;
extern char **environ;
char termbuf[BUFSIZ];
char *envinit[] = { "TERM=supdup", termbuf, 0 };

struct chpacket pkt;
char *ptr = pkt.cp_data;
int cnt = 0;
char pty[40], tty[40];
int fpty, ftty;
char remote_hostname[40], local_hostname[40];

rmut(line)
	char *line;
{
	struct utmp wtmp;
	register int f;
	int found = 0;

	f = open("/etc/utmp", O_RDWR);
	if (f >= 0) {
		while (read(f, (char *)&wtmp, sizeof(wtmp)) == sizeof(wtmp)) {
			if (SCMPN(wtmp.ut_line, line) || wtmp.ut_name[0] == 0)
				continue;
			lseek(f, -(long)sizeof(wtmp), 1);
			SCPYN(wtmp.ut_name, "");
			SCPYN(wtmp.ut_host, "");
			time(&wtmp.ut_time);
			write(f, (char *)&wtmp, sizeof(wtmp));
			found++;
		}
		close(f);
	}
	if (found) {
		f = open("/usr/adm/wtmp", O_WRONLY | O_APPEND);
		if (f >= 0) {
			SCPYN(wtmp.ut_line, line);
			SCPYN(wtmp.ut_name, "");
			SCPYN(wtmp.ut_host, "");
			time(&wtmp.ut_time);
			write(f, (char *)&wtmp, sizeof(wtmp));
			close(f);
		}
	}
}

shut()
{
	register int f;

	kill(him, SIGHUP);
	kill(job_pid, SIGHUP);
	rmut(tty + sizeof("/dev/") - 1);
/*    rmut(tty);*/
	sleep(1);
	exit(1);
}

main(argc, argv)
	int argc;
	char *argv[];
{
	int pid, i;
	struct chstatus chstatus;

	pid = getpid();
	if (!get_pty()) {
		chreject(0, "?All ptys are in use. Try again later.");
		exit(1);
	};
	ioctl(0, CHIOCACCEPT, 0);
	ioctl(0, CHIOCGSTAT, &chstatus);
	gethostname(local_hostname, sizeof(local_hostname));
	SCPYN(remote_hostname, chaos_name(chstatus.st_fhost));
	conn = dup(0);
	ioctl(conn, CHIOCSMODE, CHRECORD);
	iparams();
	if ((job_pid = fork()) == 0)
		run_job();
	setpriority(PRIO_PROCESS, pid, -10);
	dup2(fpty, 0);
	close(fpty);
	dup2(0, 1);		/* make pty now std out/in */
	i = -1;
	ioctl(0, TIOCPKT, &i);	/* and put it in packet mode */
	signal(SIGHUP, shut);
	signal(SIGINT, shut);
	signal(SIGQUIT, shut);
	signal(SIGILL, shut);
	signal(SIGTERM, shut);
	signal(SIGBUS, shut);
	signal(SIGSEGV, shut);
	signal(SIGCHLD, shut);
	if ((him = fork()) == 0) {
		him = pid;
		rd();
		shut();
	}
	ptr = pkt.cp_data;	/* initialize output pkt pointer */
	greet();		/* send greeting */
	wr();			/* start writing to the network */
	ioctl(conn, CHIOCOWAIT, 1);
	shut();
}

get_pty()
{
	register int i, c;

	for (c = 'p'; c < 'w'; c++)
		for (i = 0; i < 16; i++) {
			sprintf(pty, "/dev/pty%c%x", c, i);
			sprintf(tty, "/dev/tty%c%x", c, i);
			if ((fpty = open(pty, O_RDWR | O_NDELAY)) >= 0)
				return (TRUE);
		}
	return (FALSE);
};

run_job()
{
	int f;
	struct sgttyb b;
	struct tchars tc;
	short mw;

#ifdef TIOCSWINSZ
	struct winsize ws;
#endif	/* TIOCSWINSZ */
	setpriority(PRIO_PROCESS, getpid(), 0);
	close(fpty);
	close(conn);
	if ((f = open("/dev/tty", O_RDWR)) >= 0) {	/* flush my cntrl tty */
		ioctl(f, TIOCNOTTY, 0);
		close(f);
	};
	if ((ftty = open(tty, O_RDWR | O_NDELAY)) < 0)
		exit(1);
	signal(SIGHUP, SIG_IGN);
	vhangup();
	close(ftty);
	sleep(1);
	signal(SIGHUP, SIG_DFL);
	if ((f = open("/dev/tty", O_RDWR)) >= 0) {	/* flush my cntrl tty */
		ioctl(f, TIOCNOTTY, 0);
		close(f);
	};
	if ((ftty = open(tty, O_RDWR | O_NDELAY)) < 0)
		exit(1);
	chown(tty, 0, 0);
	chmod(tty, 0622);	/* make the tty reasonable */
	ioctl(ftty, TIOCNXCL, 0);	/* turn off excl use */
	ioctl(ftty, TIOCSETD, OTTYDISC);	/* old line discipline */
	b.sg_ispeed = B9600;	/* input speed */
	b.sg_ospeed = B9600;	/* output speed */
	b.sg_erase = 0177;	/* erase character */
	b.sg_kill = 'U' - 0100;	/* kill character */
	b.sg_flags = ECHO | CRMOD | ANYP;	/* mode flags */
	ioctl(ftty, TIOCSETP, &b);
	tc.t_intrc = 'C' - 0100;	/* interrupt */
	tc.t_quitc = '\\' - 0100;	/* quit */
	tc.t_startc = 'Q' - 0100;	/* start output */
	tc.t_stopc = 'S' - 0100;	/* stop output */
	tc.t_eofc = 'D' - 0100;	/* end-of-file */
	tc.t_brkc = -1;		/* input delimiter (like nl) */
	ioctl(ftty, TIOCSETC, &tc);
	mw = LCRTERA | LCRTBS | LCRTKIL | LCTLECH | LPENDIN | LDECCTQ;
	ioctl(ftty, TIOCLSET, &mw);
#ifdef TIOCSWINSZ
	/*
	 * for 4.3 
	 */
	ws.ws_row = tcmxv;
	ws.ws_col = tcmxh;
	ws.ws_xpixel = ws.ws_ypixel = 0;
	ioctl(ftty, TIOCSWINSZ, &ws);
#endif	/* TIOCSWINSZ */
	dup2(ftty, 0);
	close(ftty);
	dup2(0, 1);
	dup2(0, 2);		/* Connect to tty */
	environ = envinit;
	printf(BANNER, local_hostname, "");
	execl("/bin/login", "login", "-p", "-h", remote_hostname, 0);
	exit(1);
}

/*
 * Read the parameters from the net.
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
	sprintf(termbuf, "TERMCAP=SD|supdup|SUPDUP virtual terminal:co#%d:li#%d:", tcmxh, tcmxv);
	strcat(termbuf, "am:vb=\\177\\23:nd=\\177\\20:MT:");
	strcat(termbuf, "cl=\\177\\22:pt:");
	if (TOERS)
		strcat(termbuf, "ce=\\177\\05:ec=\\177\\06:cd=\\177\\04:");
	if (TOMVB)
		strcat(termbuf, "bs:");
	if (TOOVR)
		strcat(termbuf, "os:");
	if (TOMVU)
		strcat(termbuf, "up=\\177\\41:cm=\\177\\21%.%.:");
	if (TOLID) {		/* line insert/delete */
		if (TOROL)
			strcat(termbuf, "al=\\177\\25\\01:dl=\\177\\26\\01:");
		else		/* must be a lispm, multiple lines are cost free */
			strcat(termbuf, "al=40\\177\\25\\01:AL=40\\177\\25%.:dl=40\\177\\26\\01:DL=40\\177\\26%.:");
	}
	if (TOCID) {
		strcat(termbuf, "mi:im=:ei=:ic=\\177\\27\\01:");
		strcat(termbuf, "dc=\\177\\30\\01:dm=:ed=:");
	}
	if (!TOROL)		/* lispm */
		strcat(termbuf, "km:ns:vc#40:");
	else
		strcat(termbuf, "so=\\177\\31:se=\\177\\32:");
}

rd36(cp)
	register char *cp;
{
	register int i;

	for (i = 0; i < 6; i++) {
		if (cp)
			*cp++ = RDCONN;
		else
			RDCONN;
	}
}

greet()
{
	register char *cp;
	char buf[BUFSIZ];

	sprintf(buf, "\r\n%s SUPDUP from %s\r\n%c", local_hostname, remote_hostname, TDNOP);
	for (cp = buf; *cp;)
		WRCONN(*cp++ & 0377);
	chflush(DATOP);
	sleep(2);
	wrsup(TDCLR);
}

/* read from the user's tty and write to the network connection */
wr()
{
	register int len;
	register unsigned char *p;
	long waiting;
	unsigned char buf[BUFSIZ];
	struct chstatus chstatus;

	for (;;) {
		len = read(0, buf, sizeof(buf));
		if (len > 0) {
			switch (buf[0]) {
			case TIOCPKT_DATA:	/* data packet */
				p = &buf[1];
				while (--len > 0)
					wrsup(*p++);
				if ((ptr != pkt.cp_data) && ((ioctl(0, FIONREAD, &waiting) < 0) || !waiting))
					chflush(DATOP);
				break;
			case TIOCPKT_FLUSHREAD:	/* flush packet */
			case TIOCPKT_FLUSHWRITE:	/* flush packet */
			case TIOCPKT_STOP:	/* stop output */
			case TIOCPKT_START:	/* start output */
			case TIOCPKT_NOSTOP:	/* no more ^S, ^Q */
			case TIOCPKT_DOSTOP:	/* now do ^S ^Q */
			default:
				break;
			}
		} else if ((len == 0) || (errno != EINTR))
			return;
	}
}

/* flush the current packet to the network with specified opcode */
chflush(op)
	int op;
{
	if (ptr == pkt.cp_data)
		return;
	pkt.cp_op = op;
	if (write(conn, &pkt, ptr - pkt.cp_data + 1) < 0)
		shut;
	ptr = pkt.cp_data;
	cnt = sizeof(pkt.cp_data);
}

/* read packets from the chaosnet and stuff them into the unix pty */
rd()
{
	register int c, bucky;
	long waiting;

	for (;;) {
		if ((cnt <= 0) && ((ioctl(conn, FIONREAD, &waiting) < 0) || !waiting))
			fflush(stdout);
		switch (c = RDCONN) {
		case ITP_ESCAPE:
			switch (RDCONN) {
			case ITP_LOGOUT:
				shut();
				break;
			case ITP_LOCATION:
				while (RDCONN);
			}
			break;
		case ITP_QUOTE:
			if ((c = RDCONN) == ITP_QUOTE)
				bucky = 0;
			else {
				bucky = ITP_CHAR(c, RDCONN);
				c = bucky & 0177;
			}
			if (bucky & ITP_TOP)
				c &= 037;
			if (bucky & ITP_CTL)
				if ((c >= 'a' && c <= 'z') || (c >= '@' && c <= 'Z') || (c >= '\\' && c <= '_') || (c == ' '))
					c &= 037;
				else
					putchar(CTL_PREFIX);
			if (bucky & ITP_MTA) {
				/*
				 * meta chars are always lower case 
				 */
				if (isupper(c))
					c = tolower(c);
				c |= 0200;
			}
		default:	/* What others?? */
			putchar(c);
		}
	}
}

/* read a packet from the network connection */
rdpkt()
{
	if (((cnt = read(conn, &pkt, sizeof(pkt)) - 2) < -1) || ((pkt.cp_op & 0377) != DATOP))
		shut();
	ptr = pkt.cp_data;
	return (*ptr++ & 0377);
}

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
				WRCONN(TDBEL);
				break;
			case '\b':
				if (currcol) {
					currcol--;
					WRCONN(TDBS);
				} else if (currline == 0) {
					if (!TOROL) {
						currcol = tcmxh - 1;
						currline = tcmxv - 1;
						WCURPOS;
					}
				} else {
					currcol = tcmxh - 1;
					currline--;
					WCURPOS;
				}
				break;
			case '\r':
				if (currcol) {
					currcol = 0;
					WCURPOS;
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
				WCURPOS;
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
						WRCONN(TDFS);
				} else
					WCURPOS;
				break;
			case '\0':
				break;
			case ' ':
				WRCONN(TDDLF);
				WRCONN(TDFS);
			default:
				if (c <= 037 && !(ttyopt[1] & TOSA1)) {
					wrsup('^');
					c |= 0100;
				}
				if (c != ' ')
					WRCONN(c);
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
					WCURPOS;
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
						WRCONN(TDCRL);
						if (currcol)
							WCURPOS;
					} else {
						wrapped = 1;
						currline = 0;
						i = currcol;
						currcol = 0;
						WCURPOS;
						if (TOERS)
							WRCONN(TDEOL);
						if (i) {
							currcol = i;
							WCURPOS;
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
	WRCONN(c);
}
