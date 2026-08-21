/* EMACS_MODES: c, !fill
 * NOTES
 * connect from 4.2 bsd to TTYLINK via chaos
 *
 * 10/19/84 dove
 *	first rev (abstracted from pdp11 program by MBM)
 *
 * 10/20/84 dove
 *	fixup half duplex, online help and crmod
 *
 * 2/1/85 dove
 *	accept numeric chaos address
 */

#include <sys/chaos.h>
#include <hosttab.h>
#include <signal.h>
#include <sgtty.h>
#include <stdio.h>
#include <ctype.h>
char buf[512];
char nmbuf[1024];
int child = 0;

#define QUIT_CHAR 'q'
#define PAUSE_CHAR 'p'
#define RAW_CHAR 'r'
#define ESCAPE_CHAR 036
#define HELP_CHAR '?'

int halfp = 0;			/* half duplex */
int crmod = 0;			/* screen \n echos as \n\r */
int raw = 0;			/* raw mode (escape option)     */
int conn;
struct host_entry *host, *lookup();

command()
{
	char buf[80];

      loop:buf[0] = 0;
/*	printf("\n%s p - pause, q - quit, ^~ - ^~ ?", host->host_name); */
/*	fflush(stdout); */
	read(0, buf, sizeof buf);
	switch (buf[0]) {
	case PAUSE_CHAR:
		return PAUSE_CHAR;
	case QUIT_CHAR:
		return QUIT_CHAR;
	case ESCAPE_CHAR:
		return ESCAPE_CHAR;
	case RAW_CHAR:
		raw = raw ? 0 : 1;
		return NULL;	/* toggle raw mode */
	case HELP_CHAR:
		write(1, "p - pause\r\n", 12);
		write(1, "q - quit\r\n", 11);
		write(1, "? - help\r\n", 11);
		write(1, "^^ - send ^^\r\n", 15);
		write(1, "r - toggle raw mode\r\n", 21);
		return NULL;
	}
	goto loop;
}

intr()
{
	kill(child, SIGTSTP);	/* stop child */
	switch (command()) {
	case PAUSE_CHAR:
		resetty();
		kill(0, SIGTSTP);	/* stop */
		setty();	/* continue */
		break;
	case QUIT_CHAR:
		resetty();
		kill(child, SIGTERM);
		quit();		/* no return */
	case ESCAPE_CHAR:
		signal(SIGINT, intr);
		write(conn, "\036", 1);
		break;
	}
	kill(child, SIGCONT);
	signal(SIGINT, intr);
}

static struct sgttyb sg;

setty()
{
	if (sg.sg_ispeed == 0)
		ioctl(0, TIOCGETP, &sg);
	sg.sg_flags &= ~ECHO;
	sg.sg_flags |= RAW;
	ioctl(0, TIOCSETP, &sg);
}

resetty()
{
	sg.sg_flags |= ECHO;
	sg.sg_flags &= ~RAW;
/*	ioctl(0,TIOCLBIC,&litmode);*/
	ioctl(0, TIOCSETP, &sg);

}

quit()
{
	resetty();
	printf("CLOSED\n");
	exit(0);
}

timeout()
{
	printf("timeout\n");
	fflush(stdout);
}

main(argc, argv)
	char **argv;
{
	int n, addr;
	char *hostname = NULL, *cname = "TTYLINK", *contact = NULL;
	struct chstatus chstat;
	static char junkbuf[CHMAXPKT];

	if (lookup(*argv))
		hostname = *argv;
	argc--, argv++;
	while (0 < argc && **argv == '-') {	/* switches       */
		if (!strcmp(*argv, "-h")) {
			halfp = 1;
			argc--, argv++;
		} else if (!strcmp(*argv, "-r")) {
			crmod = 1;
			argc--, argv++;
		} else {
			printf("bad switch %s\n", *argv);
			exit(2);
		}
	}
	if (hostname == NULL) {
		hostname = *argv;
		argc--, argv++;
	}
	if (0 < argc) {
		char *index();

		cname = *argv;
		if (index(' ', *argv)) {
			char *ptr = index(' ', *argv);

			contact = 1 + ptr;
			*ptr = '\0';
		}
		argc--, argv++;
	}

	if (isdigit(*hostname)) {
		addr = chaos_addr(hostname, 0);
	} else {
		if ((host = lookup(hostname)) == NULL) {
			printf("can\'t find host %s\n", hostname);
			exit(1);
		}

		addr = chaos_addr(host->host_name, 0);
		if (addr == 0) {
			printf("host %s is not on the chaos net\n", host->host_name);
			exit(2);
		}
	}
	printf("Trying(%s)...", hostname);
	fflush(stdout);
	if ((conn = chopen(addr, cname, 2, 1, contact[0] ? contact : 0, 0, 0)) < 0) {
		perror("chopen");
		printf("ncp error -- cannot open %s(%s %s)\n", hostname, cname, contact);
		exit(2);
	}
	signal(SIGALRM, timeout);
	alarm(15);
	ioctl(conn, CHIOCSWAIT, CSRFCSENT);
	signal(SIGALRM, SIG_IGN);
	ioctl(conn, CHIOCGSTAT, &chstat);
	if (chstat.st_state == CSRFCSENT) {
	      nogood:
		printf("host %s not responding (%s)\n", hostname, contact);
		close(conn);
		conn = -1;
		exit(1);
	}
	if (chstat.st_state != CSOPEN) {
		if (chstat.st_state == CSCLOSED) {
			printf(" Refused\n");
			if ((chstat.st_plength) && ((chstat.st_ptype == ANSOP) || (chstat.st_ptype == CLSOP))) {
				ioctl(conn, CHIOCPREAD, junkbuf);
				printf("%s\n", junkbuf);
			}
			close(conn);
			conn = -1;
			exit(1);
		}
		printf("host %s st_state %d\n", hostname, chstat.st_state);
		close(conn);
		conn = -1;
		exit(1);
	} else {
		printf("Connected.  ^~ or ^^ to escape.\n");
		printf("Remote host will need to know your terminal type.\n");
		fflush(stdout);
	}
	fflush(stdout);

	setty();
	nice(-10);
	setuid(getuid());

	if (child = fork()) {
		signal(SIGINT, intr);
		signal(SIGTERM, quit);
		for (;;) {
			int i;

			n = read(0, buf, sizeof buf);
			if (n < 0)
				kill(0, SIGTERM);
			buf[n] = 0;	/* clear the first unused char */
			for (i = 0; i < n; i++)
				if (buf[i] == ESCAPE_CHAR)
					n = i;
			if (halfp & !raw) {
				if (crmod) {
					int next = 0;

					for (i = next; i < n; i++) {
						if (buf[i] == '\n') {
							write(1, buf + next, i - next + 1);
							write(1, "\r", 1);
							next = i + 1;
						}
					}
					if (next < i)
						write(1, buf + next, i - next);
				} else {
					write(1, buf, n);
				}
				n = write(conn, buf, n);
				if (n < 0)
					kill(0, SIGTERM);
			} else {
				n = write(conn, buf, n);
				if (n < 0)
					kill(0, SIGTERM);
			}
			ioctl(conn, CHIOCFLUSH, 0);
			if (buf[n] == ESCAPE_CHAR)
				intr();
		}
	} else {
		signal(SIGINT, SIG_IGN);
		for (;;) {
			n = read(conn, buf, sizeof buf);
			if (n < 0)
				kill(0, SIGTERM);
			if (crmod & !raw) {
				int next = 0, i;

				for (i = next; i < n; i++) {
					if (buf[i] == '\n') {
						write(1, buf + next, i - next + 1);
						write(1, "\r", 1);
						next = i + 1;
					}
				}
				if (next < i)
					write(1, buf + next, i - next);
			} else {
				write(1, buf, n);
			}
		}
	}
}

punt(str)
	char *str;
{
	printf(str);
	fflush(stdout);
	exit(1);
}

struct host_entry *
lookup(name)
	char *name;
{
	struct host_entry *host, *host_info();

	if ((host = host_info(name)) == 0) {
		return (NULL);
	}
	return (host);
}
