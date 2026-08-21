#include <stdio.h>
#include <ctype.h>
#include <hosttab.h>
#include <sys/chaos.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <signal.h>
#include <utmp.h>
#ifdef BSD42
#include <sys/time.h>
#else
#include <time.h>
#endif
#include <pwd.h>
/*
 * SEND server from another user on the chaosnet
 */


/* $Log:	SEND.c,v $
 * Revision 1.2  86/10/12  13:12:21  mbm
 * Use group write permission on tty for 4.3
 *  */

#define MAXSEND		2000
char sendbuf[MAXSEND];

#define SPOOL "/usr/spool/sends"
char *sptr = sendbuf;
struct utmp ubuf;

#define NMAX sizeof(ubuf.ut_name)
#define LMAX sizeof(ubuf.ut_line)
#define FALSE 0
#define TRUE 1
#define bool int
char of[sizeof(SPOOL) + 1 + NMAX + 1];

char *strcat();
char *strcpy();
int signum[] = { SIGHUP, SIGINT, SIGQUIT, 0 };

char me[10] = "???";
char *him;
char *mytty;
char histty[32];
char *histtya;
char *ttyname();
char *rindex();
int logcnt;
int eof();
int timout();
char *getenv();

main(argc, argv)
	char *argv[];
{
	struct stat stbuf;
	register i;
	register FILE *uf;
	int c1, c2;
	long clock = time(0);
	struct tm *localtime();
	struct tm *localclock = localtime(&clock);
	struct chstatus chst;
	int ofd;
	bool firstnl = TRUE;
	struct passwd *pwd, *getpwnam();

	if (argc < 2) {
		chreject(0, "No user name supplied");
		exit(1);
	}
	him = argv[1];
	lcase(him);
	if (argc > 2)
		histtya = argv[2];
	if ((uf = fopen("/etc/utmp", "r")) == NULL) {
		printf("cannot open /etc/utmp\n");
		goto cont;
	}
	if (histtya) {
		strcpy(histty, "/dev/");
		strcat(histty, histtya);
	}

	while (fread((char *)&ubuf, sizeof(ubuf), 1, uf) == 1) {
		if (ubuf.ut_name[0] == '\0')
			continue;
		if (him[0] != '-' || him[1] != 0)
			for (i = 0; i < NMAX; i++) {
				c1 = him[i];
				c2 = ubuf.ut_name[i];
				if (c1 == 0)
					if (c2 == 0 || c2 == ' ')
						break;
				if (c1 != c2)
					goto nomat;
			}
		logcnt++;
		if (histty[0] == 0) {
			strcpy(histty, "/dev/");
			strcat(histty, ubuf.ut_line);
		}
	      nomat:
		;
	}
      cont:
	if (logcnt == 0 && histty[0] == '\0') {
		chreject(0, "User not logged in.");
		exit(1);
	}
	fclose(uf);
	if (histty[0] == 0) {
		chreject(0, "User not logged in out given tty.");
		exit(1);
	}
	if (access(histty, 0) < 0) {
		chreject(0, "User's tty non-existent.");
		exit(1);
	}
	signal(SIGALRM, timout);
	alarm(5);
	if (freopen(histty, "w", stdout) == NULL)
		goto perm;
	alarm(0);
	if (fstat(fileno(stdout), &stbuf) < 0)
		goto perm;
	if ((stbuf.st_mode & 020) == 0)
		goto perm;
	ioctl(0, CHIOCACCEPT, 0);
	ioctl(0, CHIOCGSTAT, &chst);
	*sptr++ = '\f';
	sptr += strlen(sptr);
	while ((c1 = getchar()) != EOF)
		if (sptr >= &sendbuf[MAXSEND])
			break;
		else if (c1 == CHNL) {
			if (firstnl)
				*sptr++ = ']';
			*sptr++ = '\n';
			firstnl = FALSE;
		} else if (c1 == CHTAB)
			*sptr++ = '\t';
		else
			*sptr++ = c1;

	if (sptr == sendbuf || sptr[-1] != '\n')
		*sptr++ = '\n';
	if (him[0] != '-' && ((ofd = open(sprintf(of, "%s/%s", SPOOL, him), 1)) >= 0 || (ofd = creat(of, 0622)) >= 0)) {
		if ((pwd = getpwnam(him)) == NULL || chown(of, pwd->pw_uid, pwd->pw_gid) < 0)
			chmod(of, 0666);
		lseek(ofd, 0L, 2);
		write(ofd, "[Message from ", 14);
		write(ofd, sendbuf + 1, sptr - sendbuf - 1);
		write(ofd, "\n\004", 2);
	}
	printf("\7\n\r[Message from ");
	*sptr = '\0';
	for (sptr = sendbuf + 1; *sptr;) {
		if (*sptr == '\n')
			putchar('\r');
		putchar(*sptr++);
	}
	printf("\n\r");
	exit(0);
      perm:
	chreject(0, "Permission denied on his tty.");
	exit(1);
}

timout()
{

	chreject(0, "Timeout opening their tty.");
	exit(1);
}

lcase(s)
	register char *s;
{
	while (*s)
		if (isupper(*s))
			*s++ = tolower(*s);
		else
			s++;
}
