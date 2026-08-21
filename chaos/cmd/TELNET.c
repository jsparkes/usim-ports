/* $Header$ */
/*
 *	Server TELNET program.
 *	Usable on the Chaosnet (or the Arpanet via a Chaos/Arpa gateway),
 *	although it should be easy to change for any other protocol supporting
 *	reliable bytestreams.
 *	The standard input is initially a connection which has been
 *	listened to, but neither accepted nor rejected.
 *	Author: T. J. Teixeira <TJT@MIT-XX>
 *
 * 12/29/81 tony -- Check for LOS packets, and clear errno before doing
 *			read on connection.
 * 8/3/84 Bruce Nemnich -- Hacked for efficiency.
 * 8/30/84 Bruce Nemnich -- Hacked for more efficiency, forks a getty
 * 5/8/85 Bruce Nemnich -- Uses select() call to save a process.
 * 10/30/86 Bill Nesheim -- switched to login from getty (save an exec)
 *			--  pass host name to login
 *			--  added TIOCNOTTY to avoid anti social effects
 *			--  of vhangup()
 */

#include <stdio.h>
#include <ctype.h>
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <sgtty.h>
#include <sys/types.h>
#include <sys/chaos.h>
#define BSD4_3
#define DEBUG
#define ctrl(x) ((x)&037)


char BANNER1[] = "\r\n", BANNER2[] = "\r\n";

/*
 * telnet protocol command characters
 */
#define SE	240
#define NOP	241
#define DMARK	242
#define BRK	243
#define IP	244
#define AO	245
#define AYT	246
#define EC	247
#define EL	248
#define GA	249
#define SB	250
#define WILL	251
#define WONT	252
#define DO	253
#define DONT	254
#define IAC	255

/*
 * telnet option codes
 */
#define TN_TRANSMIT_BINARY 0
#define TN_ECHO 1
#define TN_RCP 2
#define TN_SUPPRESS_GO_AHEAD 3
#define TN_NAME 4
#define TN_STATUS 5
#define TN_TIMING_MARK 6
#define TN_RCTE 7
#define TN_NAOL 8
#define TN_NAOP 9
#define TN_NAOCRD 10
#define TN_NAOHTS 11
#define TN_NAHTD 12
#define TN_NAOFFD 13
#define TN_NAOVTS 14
#define TN_NAVTD 15
#define TN_NAOLFD 16
#define TN_EXTEND_ASCII 17
#define TN_SEND_LOCATION 23
#define TN_EXOPL 255

char *optnames[] = {
	"TRANSMIT BINARY",
	"ECHO",
	"RCP",
	"SUPPRESS GO AHEAD",
	"NAME",
	"STATUS",
	"TIMING MARK",
	"RCTE",
	"NAOL",
	"NAOP",
	"NAOCRD",
	"NAOHTS",
	"NAHTD",
	"NAOFFD",
	"NAOVTS",
	"NAVTD",
	"NAOLFD",
	"EXTEND ASCII",
	"EXOPL",
};

char *optcmd[] = {
	"SE",
	"NOP",
	"DMARK",
	"BRK",
	"IP",
	"AO",
	"AYT",
	"EC",
	"EL",
	"GA",
	"SB",
	"WILL",
	"WONT",
	"DO",
	"DONT",
	"IAC",
};

char hisopts[256], myopts[256];

int bye();

struct host_entry *host;
char contact[80];

/* for ttyloc */
char loc_file[32];
char hisname[64];

int conn;			/* connection */
int isopen;			/* connection open */
int getty;			/* process id of getty process */
int ptyfd;			/* fd of slave tty */
int echomode = -1;
int goahead = 1;

/* special characters */
int t_erase;			/* EC */
int t_kill;			/* EL */
int t_intrc;			/* IP */
int t_flushc;			/* AO */

extern int errno;
extern char *chaos_name();
char errbuf[BUFSIZ];
char tty[20];			/* the control (slave) terminal */
char pty[20];			/* the control (slave) terminal */

main(argc, argv)
	char *argv[];
{
	register int i, c, ttyfd;
	struct sgttyb b;
	struct chpacket junk;
	char buf[BUFSIZ], *cp;
	int bye();
	struct chstatus chstat;

	conn = 0;
	i = open("/dev/tty", O_RDWR);
	if (i >= 0) {
		ioctl(i, TIOCNOTTY, 0);
		close(i);
	}
	for (c = 'p'; c <= 'u'; c++)
		for (i = 0; i < 16; i++) {
			sprintf(pty, "/dev/pty%c%x", c, i);
			sprintf(tty, "/dev/tty%c%x", c, i);
			if ((ptyfd = open(pty, O_RDWR)) >= 0) {
				goto win;
			}
		}
	chreject(conn, "can't open pseudo terminal");
	exit(1);
      win:
	signal(SIGTERM, bye);
	signal(SIGCHLD, bye);

	/*
	 * OK, let's open the slave and fork a getty for it 
	 */
	if ((ttyfd = open(tty, O_RDWR)) < 0) {
		chreject(conn, "?opened pty but not tty");
		exit(1);
	}
	ioctl(ttyfd, TIOCGETP, &b);
	b.sg_flags = CRMOD | XTABS | ANYP;
	ioctl(ttyfd, TIOCSETP, &b);
	ioctl(ptyfd, TIOCGETP, &b);
	b.sg_flags &= ~ECHO;
	ioctl(ptyfd, TIOCSETP, &b);

	ioctl(conn, CHIOCACCEPT, 0);
	ioctl(conn, CHIOCSMODE, CHRECORD);
	isopen = 1;

	/*
	 * get other guy's name (or address)
	 */
	ioctl(conn, CHIOCGSTAT, &chstat);
	cp = chaos_name(chstat.st_fhost);
	if (cp != NULL)
		strcpy(hisname, cp);
	else
		sprintf(hisname, "chaos %06o", chstat.st_fhost);
	errno = 0;
	if ((getty = fork()) == 0) {
		char *env[2];
		char hostname[40];

		env[0] = "TERM=network";
		env[1] = NULL;
		close(conn);
		close(ptyfd);
		dup2(ttyfd, 0);
		dup2(ttyfd, 1);
		dup2(ttyfd, 2);
		close(ttyfd);
		sleep(2);
		write(1, BANNER1, sizeof(BANNER1));
		gethostname(hostname, sizeof(hostname));
		write(1, hostname, strlen(hostname));
		write(1, BANNER2, sizeof(BANNER2));
		execle("/bin/login", "login", "-h", hisname, "-p", NULL, env);
		/*
		 * NOTREACHED 
		 */
	}
	close(ttyfd);

	/*
	 * setbuf(stdin, buf);
	 */
	/*
	 * pty is stdout & stderr 
	 */
	dup2(ptyfd, 1);
	dup2(ptyfd, 2);
	start();
	bye();
	exit(0);
}


bye()
{
	(void)unlink(loc_file);
	close(conn);
	rmut();
	vhangup();
	/*
	 * shoot 'em down 
	 */
	kill(0, SIGKILL);
	exit(2);
}

/* borrowed from /usr/src/ucb/telnetd.c */
#include <utmp.h>

struct utmp wtmp;
char wtmpf[] = "/usr/adm/wtmp";
char utmp[] = "/etc/utmp";

#define SCPYN(a, b)	strncpy(a, b, sizeof (a))
#define SCMPN(a, b)	strncmp(a, b, sizeof (a))

rmut()
{
	register f;
	int found = 0;

	f = open(utmp, 2);
	if (f >= 0) {
		while (read(f, (char *)&wtmp, sizeof(wtmp)) == sizeof(wtmp)) {
			if (SCMPN(wtmp.ut_line, tty + 5) || wtmp.ut_name[0] == 0)
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
		f = open(wtmpf, 1);
		if (f >= 0) {
			SCPYN(wtmp.ut_line, tty + 5);
			SCPYN(wtmp.ut_name, "");
			SCPYN(wtmp.ut_host, "");
			time(&wtmp.ut_time);
			lseek(f, (long)0, 2);
			write(f, (char *)&wtmp, sizeof(wtmp));
			close(f);
		}
	}
	chmod(tty, 0222);
	chown(tty, 0, 0);
	chmod(pty, 0222);
	chown(pty, 0, 0);
}


/*
 * save up data to be sent
 */

struct chpacket opkt;		/* current output packet */
char *optr = opkt.cp_data;
int ocnt = sizeof opkt.cp_data;

#define wrconn(c)	{ \
				*optr++ = (c); \
				if (--ocnt <= 0) chflush(DATOP); \
			}

/*
 * flush the connection in a packet with the specified opcode
 */

chflush(op)
	int op;
{
	if (optr == opkt.cp_data)
		return;
	opkt.cp_op = op;
	write(conn, &opkt, optr - opkt.cp_data + 1);
	optr = opkt.cp_data;
	ocnt = sizeof(opkt.cp_data);
}


start()
{
	opt(WILL, TN_ECHO);
	opt(DO, TN_SUPPRESS_GO_AHEAD);
	opt(DO, TN_TRANSMIT_BINARY);
	opt(WILL, TN_SUPPRESS_GO_AHEAD);

	rd();
}

int
sel()
{
	int nfds;

#ifdef BSD4_3
	fd_set rset, eset;

	nfds = (conn > ptyfd ? conn : ptyfd) + 1;

	for (;;) {
		FD_ZERO(&eset);
		FD_ZERO(&rset);
		FD_SET(conn, &eset);
		FD_SET(ptyfd, &eset);
		FD_SET(conn, &rset);
		FD_SET(ptyfd, &rset);
		if (select(nfds, &rset, (fd_set *) 0, &eset, 0) < 0) {
			if (errno != EINTR)
				return -1;
		} else {
			if (FD_ISSET(conn, &eset) || FD_ISSET(ptyfd, &eset))
				return -1;
			if (FD_ISSET(ptyfd, &rset))
				rdpty();
			if (FD_ISSET(conn, &rset))
				return 0;
		}
	}
#else
	/*
	 * 4.2BSD select behavior 
	 */
	int rmask, emask, ptymask, connmask;

	ptymask = 1 << ptyfd;
	connmask = 1 << conn;
	nfds = (conn > ptyfd ? conn : ptyfd) + 1;

	for (;;) {
		rmask = emask = (connmask | ptymask);
		if (select(nfds, &rmask, 0, &emask, 0) < 0) {
			if (errno != EINTR)
				return -1;
		} else {
			if (emask)
				return -1;
			if (rmask & ptymask)
				rdpty();
			if (rmask & connmask)
				return 0;
		}
	}
#endif
}



/*
 * read from the user's tty and write to the network connection
 */

rdpty()
{
	register i, count, c, binp;
	char buf[1024];
	static char lastc = 0;

	binp = myopts[TN_TRANSMIT_BINARY];
	count = read(ptyfd, buf, sizeof(buf));
	for (i = 0; i < count; i++) {
		c = buf[i];
		if (!binp)
			c &= 0177;	/* strip the parity bit */
		if (lastc == '\r' && c != '\n')
			wrconn(0);
		wrconn(c);
		lastc = c;
	}
	chflush(DATOP);
}

rd()
{
	register int c, lastc;


	lastc = 0;
	while (1) {
		if (emptyconn()) {	/* nothing to read */
			fflush(stdout);
			if (goahead) {
				wrconn(IAC);
				wrconn(GA);
				chflush(DATOP);
			}
		}
		errno = 0;
		if ((c = rdconn()) == EOF) {
			if (errno == EINTR)
				continue;
			break;
		}
		if (c == IAC && (c = rdconn()) != IAC)
			option(c);
		else {
			if (lastc != '\r' || (c != '\n' && c != 0))
				putchar(c);
			lastc = c;
		}
	}
}

option(c)
	register int c;
{
	register int code;
	struct sgttyb stbuf;
	struct tchars tchars;
	struct ltchars ltchars;

	/*
	 * update local characters 
	 */
	ioctl(fileno(stdout), TIOCGETP, &stbuf);
	ioctl(fileno(stdout), TIOCGETC, &tchars);
	ioctl(fileno(stdout), TIOCGLTC, &ltchars);
	t_erase = stbuf.sg_erase;
	t_kill = stbuf.sg_kill;
	t_intrc = tchars.t_intrc;
	t_flushc = ltchars.t_flushc;
	switch (code = c) {
	case WILL:
	case WONT:
	case DO:
	case DONT:
		c = rdconn();
		switch (code) {
		case WILL:
			if (hisopts[c])	/* already active */
				break;
			if (c == TN_SUPPRESS_GO_AHEAD) {
				hisopts[c] = 1;
				opt(DO, c);
			} else if (c == TN_TRANSMIT_BINARY) {
				hisopts[c] = 1;
				opt(DO, c);
			} else if (c == TN_TIMING_MARK) {
				opt(DONT, c);
			} else if (c == TN_SEND_LOCATION) {
				hisopts[c] = 1;
				opt(DO, c);
			} else {
				opt(DONT, c);
			}
			break;

		case WONT:
			if (!hisopts[c])	/* already inactive */
				break;
			if (c == TN_SUPPRESS_GO_AHEAD) {
				opt(DONT, c);
				hisopts[c] = 0;
			} else if (c == TN_TRANSMIT_BINARY) {
				opt(DONT, c);
				hisopts[c] = 0;
			} else {
				opt(DONT, c);
			}
			break;

		case DO:
			if (myopts[c])	/* already active */
				break;
			if (c == TN_ECHO) {
				opt(WILL, c);
				myopts[c] = 1;
				hisopts[c] = 0;
				stbuf.sg_flags |= ECHO;
				ioctl(fileno(stdout), TIOCSETP, &stbuf);
			} else if (c == TN_TIMING_MARK) {
				opt(WONT, c);
			} else if (c == TN_SUPPRESS_GO_AHEAD) {
				opt(WILL, c);
				myopts[c] = 1;
				goahead = 0;
			} else if (c == TN_TRANSMIT_BINARY) {
				opt(WILL, c);
				myopts[c] = 1;
			} else {
				opt(WONT, c);
			}
			break;

		case DONT:
			if (!myopts[c])	/* already inactive */
				break;
			myopts[c] = 0;
			if (c == TN_ECHO) {
				hisopts[c] = 1;
				opt(WONT, c);
				stbuf.sg_flags &= ~ECHO;
				ioctl(fileno(stdout), TIOCSETP, &stbuf);
			} else if (c == TN_TIMING_MARK) {
				opt(WONT, c);
			} else if (c == TN_SUPPRESS_GO_AHEAD) {
				opt(WONT, c);
				goahead = 1;
			} else if (c == TN_TRANSMIT_BINARY) {
				opt(WONT, c);
			} else {
				opt(WONT, c);
			}
			break;
		}
		chflush(DATOP);
		break;


	case DMARK:
		fflush(stdout);	/* write the output */
		ioctl(fileno(stdout), TIOCFLUSH, 0);
		break;

	case IP:
		putchar(t_intrc);
		break;

	case EC:
		putchar(t_erase);
		break;

	case EL:
		putchar(t_kill);
		break;

	case AO:
		wrconn(IAC);
		wrconn(DMARK);
		chflush(DATOP);
		putchar(t_flushc);
		break;

	case NOP:
	case GA:
		break;

	case SB:
		switch (rdconn()) {
		case TN_SEND_LOCATION:
			while (rdconn() != SE);
			break;
		}
		break;

#ifdef DEBUG
	default:
		fprintf(stderr, "?Received IAC %d\r\n", code);
#endif	/* DEBUG */
	}
}

/*
 * send an option
 */
opt(option, value)
	int option, value;
{
	int mask = sigblock(1 << SIGIO);

	wrconn(IAC);
	wrconn(option);
	wrconn(value);
	chflush(DATOP);
	sigsetmask(mask);
}

static struct chpacket pkt;
static char *ptr = pkt.cp_data;
static cnt = 0;

/*
 * read a character from the network connection
 */
rdconn()
{
	char c;

	if (cnt-- > 0)
		return (*ptr++ & 0377);
	fflush(stdout);
	if (sel() < 0)
		return EOF;
	if ((cnt = read(conn, &pkt, sizeof(pkt))) <= 0)
		return (EOF);
	switch (pkt.cp_op & 0377) {
	case DATOP:
		break;

	case CLSOP:
	case EOFOP:
	case LOSOP:
		return (EOF);

	default:
#ifdef DEBUG
		fprintf(stderr, "?packet op = %o len = %d\r\n", pkt.cp_op & 0377, cnt);
#endif	/* DEBUG */
		break;
	}
	cnt--;			/* skip past the opcode */
	ptr = pkt.cp_data + 1;
	cnt--;
	return (pkt.cp_data[0] & 0377);
}

/*
 * return true if nothing can be read from the connection
 */
emptyconn()
{
	long int waiting;

	if (cnt <= 0 || ioctl(conn, FIONREAD, &waiting) < 0 || waiting == 0)
		return (1);
	return (0);
}

/*
 * send a synch code
 */
synch()
{
	chflush(DATOP);
	wrconn(IAC);
	wrconn(DMARK);
	chflush(0201);
}
