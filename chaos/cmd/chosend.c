#include <stdio.h>
#include <ctype.h>
#include <hosttab.h>
#include <sys/types.h>
#include <sys/dir.h>
#ifdef BSD42
#include <sys/time.h>
#else
#include <time.h>
#endif
#include <sys/chaos.h>
/*
 * chsend user host
 */
#define SPOOL "/usr/spool/chsend"
char sends[sizeof(SPOOL) + 1 + 20 + 1];

char *getlogin();

main(argc, argv)
	int argc;
	char **argv;
{
	char *user, *host;
	int fd, c, addr;
	struct chstatus chst;
	long now;
	struct tm *tm, *localtime();
	char *index(), *ucase();
	FILE *out;
	char *me;
	char rfcbuf[CHMAXRFC + 1];
	extern char _sobuf[];

	if ((me = getlogin()) == NULL) {
		fprintf(stderr, "I can't get your login name!\n");
		exit(1);
	}
	if (argc == 1) {
		sprintf(sends, "%s/%s", SPOOL, me);
		execl("/usr/ucb/more", "more", sends, 0);
		fprintf(stderr, "I can't exec /usr/ucb/more!\n");
		exit(1);
	}
	if (argc == 2) {
		if ((host = index(argv[1], '@')) == NULL)
			goto usage;
		*host++ = '\0';
	} else if (argc != 3) {
	      usage:
		fprintf(stderr, "chsend: usage is: chsend user host\n");
		exit(1);
	} else
		host = argv[2];
	user = argv[1];
	if ((addr = chaos_addr(host, 0)) <= 0) {
		fprintf(stderr, "chsend: host %s unknown\n", host);
		exit(1);
	}
	if ((fd = chopen(addr, CHAOS_SEND, 1, 1, ucase(user), 0, 0)) < 0) {
		fprintf(stderr, "chsend: can't open connection\n");
		exit(1);
	}
	ioctl(fd, CHIOCSWAIT, CSRFCSENT);
	ioctl(fd, CHIOCGSTAT, &chst);
	if (chst.st_state != CSOPEN) {
		if (chst.st_ptype == CLSOP) {
			ioctl(fd, CHIOCPREAD, rfcbuf);
			rfcbuf[chst.st_plength] = '\0';
			fprintf(stderr, "chsend: %s\n", rfcbuf);
		} else
			fprintf(stderr, "chsend: connection refused.\n");
		exit(1);
	}
	time(&now);
	tm = localtime(&now);
	out = fdopen(fd, "w");
	setbuf(out, _sobuf);
	fprintf(out, "%s@%s %d/%d/%d %02d:%02d:%02d%c", getlogin(), host_me(), tm->tm_mon + 1, tm->tm_mday, tm->tm_year, tm->tm_hour, tm->tm_min, tm->tm_sec, CHNL);
	if (isatty(0))
		fprintf(stderr, "Msg:\n");
	while ((c = getchar()) != EOF)
		putc(c == '\n' ? CHNL : c, out);
}

char *
ucase(s)
	register char *s;
{
	static char name[20];
	register char *cp = name;

	while (*s)
		if (islower(*s))
			*cp++ = toupper(*s++);
		else
			*cp++ = *s++;
	*cp++ = '\0';
	return name;
}
