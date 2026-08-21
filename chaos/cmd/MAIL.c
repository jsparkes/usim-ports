/*
 * Chaosnet server for contact name MAIL
 *
 * NOTES
 *
 * markl@thyme : removed verify code since it's broken and I don't have time
 * to fix it.
 * Marty, 16 Oct 84 (marty@HTVAX.ARPA)
 *  Removed all 4.1BSD code and conditionals.
 * 
 * 1/6/85 dove
 *	pass host as -oMsHOST
 * 
 * 10/29/85 dove 
 *  setup $r (route) macro so received line in sendmail reflects chaos
 *  route.
 *
 * For 4.2BSD
 *
 * Modified for 4.2BSD to do minimal work and let sendmail do maximal
 * work.  We punt on real time recipient verification since we can't
 * really parse addresses perfectly anyway and any quick hack we try will
 * have bugs.  The only loses in that workstations won't receive error
 * message immediately for misspelled recipients, but will just get a
 * message from the mailer sometime later indicating the error.
 *
 * $Header: MAIL.c,v 1.5 85/01/07 00:29:30 dove Exp $
 * $Locker:  $
 * $Log:	MAIL.c,v $
 * Revision 1.5  85/01/07  00:29:30  dove
 * use -bv arg properly in verify()
 * 
 * Revision 1.4  85/01/07  00:00:55  dove
 * don't sendmail if any addrs don't verify
 * 
 * Revision 1.3  85/01/06  23:16:58  dove
 * verify users as entered
 * 
 * Revision 1.2  85/01/06  18:27:14  dove
 * fix sendmail options
 * 
 * Revision 1.1  85/01/06  16:53:13  dove
 * Initial revision
 * 
 */

#include <stdio.h>
#include <sys/types.h>
#include <sys/timeb.h>
#include <signal.h>
#include <sys/chaos.h>
#include <pwd.h>
#include <sys/time.h>
#include <sys/wait.h>
#include <sysexits.h>
#include <ctype.h>
#include <hosttab.h>

#define LINESIZE 132
#define MAXLIST 500

int goteof;

struct chstatus chst;
struct passwd *getpwnam();

char *upcase(), *copytext(), *savemail();

char *rcptlist[MAXLIST] = {
	"chaos-mail",		/* process name */
	"-ba",			/* arpa mode */
	"-odb",			/* deliver in background */
	"-oi",			/* ignore dots in msg   */
	"-om",			/* send to me too if aliased    */
	"-oee",			/* give zero error status and mail errors */
	"-oMQCHAOS",		/* specify network as CHAOS */
	"-oMrMAIL",		/* specify protocol as MAIL */
	0
};

/* the host argument is appended later as the switch "-oMsHOST" */
char **rcpt;

main(argc, argv)
	int argc;
	char *argv[];
{
	struct passwd *pwd;
	int rcount;
	char *error;
	char fromhost[50];
	char *malloc();

	if (ioctl(0, CHIOCGSTAT, &chst) < 0)
		exit(1);
	/*
	 * Do all the things that might cause us errors so we can punt right
	 * away and avoid hairy error processing.
	 */
	if ((pwd = getpwnam("network")) == NULL)
		error = "-Can't find uid for user: network";
	else
		setuid(pwd->pw_uid);
	rcpt = rcptlist;

	while (*rcpt)
		rcpt++;
	strcpy(fromhost, chaos_name(chst.st_fhost));
	upcase(fromhost);
	*rcpt = malloc(strlen(fromhost) + sizeof("-oMs") + 1);
	strcpy(*rcpt, "-oMs");
	strcat(*rcpt, fromhost);
	rcpt++;

	error = NULL;
	rcount = 0;
	for (;;) {
		register int c;
		register char *p;
		char line[LINESIZE];

		for (p = line; (c = getchar()) != CHNL; p++)
			if (c == EOF) {
				chreject(fileno(stdin), "Protocol error - Unexpected EOF");
				exit(1);
			} else if (p < &line[LINESIZE])
				*p = c;
		if (p == line)
			break;
		else if (p >= &line[LINESIZE])
			printf("-Recipient name too long%c", CHNL);
		else {
			*p = 0;
			if (rcpt >= &rcptlist[MAXLIST - 1])
				printf("%%Too many recipients%c", CHNL);
			else if ((*rcpt = malloc(strlen(line) + 1)) == NULL)
				printf("%%Not enough memory for recipients%c", CHNL);
			else {
				char *ret, *verify();

				strcpy(*rcpt, line);
				rcpt++;
				ret = verify(line);
				printf("%s%c", ret, CHNL);
				if (ret[0] == '+')
					rcount++;
				else
					error = ret;
			}
		}
		fflush(stdout);
		ioctl(fileno(stdout), CHIOCFLUSH, 0);
	}
	if (rcount == 0)
		error = "-No valid recipients for this message";
	*rcpt = 0;
	if (error == 0)
		error = savemail();
	/*
	 * If we haven't read through the EOF, so do now.
	 */
	if (goteof == 0)
		while (getchar() != EOF);	/* swallow input */
	if (error)
		printf("%s%c", error, CHNL);
	else
		printf("+%c", CHNL);
	exit(0);
}

char *
savemail()
{
	register FILE *out;
	static char message[LINESIZE + 1];
	int from[2], to[2];
	int pid, wpid, status;
	char *error = 0;

	if (pipe(to) < 0 || pipe(from) < 0)
		return "%Can't make pipe for delivery process";
	switch (pid = fork()) {
	case -1:
		return "%Can't fork delivery process";
	case 0:
		close(to[1]);
		close(from[0]);
		close(0);
		close(1);
		close(2);
		dup(to[0]);
		dup(from[1]);
		dup(1);
		close(to[0]);
		close(from[1]);
		execv("/usr/lib/sendmail", rcptlist);
		write(1, "Can't exec sendmail!\n", 21);
		exit(1);
		/*
		 * NOTREACHED 
		 */
	default:
		close(to[0]);
		close(from[1]);
		if ((out = fdopen(to[1], "w")) == NULL)
			error = "-Can't fdopen";
		else {
			error = copytext(out);
			fclose(out);
		}
		if (error) {
			kill(pid, SIGTERM);
			return error;
		}
#ifdef notdef
		while ((wpid = wait(&status)) >= 0 && wpid != pid);
		if (wpid != pid)
			return "-Lost sendmail process";
		if (status != 0) {
			register char *cp = message;
			register int room, n;

			sprintf(cp, "%cError queueing message: ", status == EX_TEMPFAIL ? '%' : '-');
			while (*cp)
				cp++;
			room = LINESIZE - strlen(message);
			while (room > 0 && (n = read(from[0], cp, room)) > 0) {
				room -= n;
				cp += n;
			}
			*cp = 0;
			return message;
		}
#endif	/* notdef */
		return (char *)0;
	}
	/*
	 * NOTREACHED 
	 */
}

char *				/* return - for fail, + for ok, % for temp-err */
verify(user)
	char *user;
{
#ifdef notdef
	int pid, wpid, status;
	char *error = 0;

	switch (pid = fork()) {
	case -1:
		return ("%Can\`t fork");
	case 0:
		close(0);
		close(1);
		close(2);
		open("/dev/null");
		open("/dev/null");
		open("/dev/null");
		execl("/usr/lib/sendmail", "chaos-verify", "-bv", user, 0);
		exit(1);
		/*
		 * NOTREACHED 
		 */
	default:
		while ((wpid = wait(&status)) >= 0 && wpid != pid);
		if (wpid != pid)
			return ("%lost verify process");
		if (status != 0)
			return ("-no such user");
		else
			return ("+deliverable");
	}
	/*
	 * NOTREACHED 
	 */

	int s = open_tcp("localhost.", "smtp");
	char buf[512];

	if (s == -1)
		return ("%no smtp service");
	write(s, "HELO\n", 5);
	read(s, buf, sizeof buf);
	write(s, "VRFY ", 5);
	write(s, user, strlen(user));
	write(s, "\n", 1);
	read(s, buf, sizeof buf);
	close(s);
	if (strncmp(buf, "250", 3) == 0)
		return ("+deliverable");
	else
		return ("-no such user");
#endif	/* notdef */
	return ("+deliverable");
}

char *
copytext(to)
	register FILE *to;
{
	register int nnl, c;

	nnl = 0;
	while ((c = getchar()) != EOF) {
		switch (c) {
		case 0210:
		case 0211:
		case 0214:
			c -= 0200;
			break;
		case 0215:
			c = '\n';
			break;
		}
		if (c == '\n')
			nnl++;
		else
			nnl = 0;
		putc(c, to);
	}
	goteof++;
	/*
	 * Ensure a new line at the end of the message
	 */
	if (nnl == 0)
		putc('\n', to);
	fflush(to);
	return ferror(to) ? "%Cannot write spool file" : 0;
}

char *
upcase(string)
	char *string;
{
	register char *cp;

	for (cp = string; *cp; cp++)
		if (islower(*cp))
			*cp = toupper(*cp);
	return string;
}
