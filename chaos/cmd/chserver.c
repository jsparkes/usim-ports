/* EMACS_MODES: c, !fill
 *
 * $Header: chserver.c,v 1.3 85/02/27 11:06:46 dove Exp $
 * $Locker:  $
 * Chaos server dispatcher program.
 * It processes the unmatched RFC queue and starts programs running
 * which it finds in DESTSERVERS and whose file names match the
 * contact string in the RFC.
 *
 * 8/15/84 Cory Myers
 *	Change parsing in word to use CHLF if input is from a LISPM
 *
 * 8/17/84 dove
 *	adjust syslog priorities
 *	openlog(argv[0], 0)
 *	get hostname on bad rfc's
 *
 * Cory Myers 9/24/84
 *	server name can not containing '/'
 * $Log:	chserver.c,v $
 * Revision 1.3  85/02/27  11:06:46  dove
 * Correct bug in rejecting servers that can't be exec'd
 * 
 * Revision 1.2  84/12/13  19:39:15  dove
 * Don't syslog refusals of WHO-AM-I
 * 
 * Revision 1.1  84/12/13  19:26:11  dove
 * Initial revision
 * 
 */

#include <stdio.h>
#include <ctype.h>
#include <errno.h>
#include <signal.h>
#include <sgtty.h>
#include <sys/ioctl.h>
#include <sys/types.h>
#include <sys/dir.h>
#include <sys/stat.h>
#include <sys/param.h>
#ifndef pdp11
#ifdef BSD42
#include <sys/wait.h>
#else
#include <wait.h>
#endif
#endif
#include <sys/chaos.h>
#ifdef SYSLOG
#include <syslog.h>
#else
#define LOG_INFO 0
/* VARARGS 2 */
syslog(a, b, c, d, e, f, g, h)
	char *b;
{
	fprintf(stderr, b, c, d, e, f, g, h);
}
#endif

int is_lispm;

unsigned char *word();

/* 
 * This is the unmatched RFC server.
 */
#define SERVERDIR	DESTSERVERS	/* For unknown servers */

main(argc, argv)
	char **argv;
{
	register int rfcfd;
	register unsigned char *cname;
	register struct contact *conp;
	unsigned char *args;
	extern int errno;
	unsigned char rfcbuf[CHMAXRFC + 1];
	int child();
	int i;
	int tt;

	openlog(argv[0], 0);
	if ((rfcfd = open(CHURFCDEV, 0)) < 0) {
		syslog(LOG_ERR, "chserver: Cant open unmatched rfc handler: %s\n", CHURFCDEV);
		exit(1);
	}
	umask(0);
	chdir(SERVERDIR);
#ifndef pdp11
#ifdef BSD42
	signal(SIGCHLD, child);
#else
	sigset(SIGCHLD, child);
#endif
#endif
	/*
	 * flush control tty and run in background 
	 */
	if (tt = open("/dev/tty", 0)) {
		ioctl(tt, TIOCNOTTY, 0);
		close(tt);
	}
	if (fork())
		exit(0);


	for (;;) {
		while ((i = read(rfcfd, rfcbuf, sizeof(rfcbuf))) < 0)
			if (errno != EINTR) {
				syslog(LOG_ERR, "chserver: read failed errno:%d,i:%d\n", errno, i);
				exit(1);
			}
		rfcbuf[i] = '\0';
		syslog(LOG_INFO, "chserver: Received RFC: '%s'\n", rfcbuf);
		is_lispm = 0;
		cname = word(rfcbuf, &args);
		is_lispm = index(args, CHLF);
		if (cname[0] == '\0')
			syslog(LOG_NOTICE, "chserver: empty rfc!\n");
		docontact(cname, args);
	}
}

refuse(cname, error)
	unsigned char *cname, *error;
{
	int chfd;

	if ((chfd = chlisten(cname, 0, 1)) < 0)
		syslog(LOG_NOTICE, "chserver: cannot 'listen' on %s\n", cname);
	else {
		unsigned char clserror[CHMAXDATA];
		struct chstatus chst;
		char *chaos_name();

		if (ioctl(chfd, CHIOCGSTAT, &chst) < 0)
			syslog(LOG_NOTICE, "Bad STAT on rejected RFC");
		sprintf(clserror, "%s for contact name: %s", error, cname);
		if (strcmp(cname, "WHO-AM-I") != 0)
			syslog(LOG_NOTICE, "Host %s rejected: %s\n", chaos_name(chst.st_fhost), clserror);
		chreject(chfd, clserror);
		close(chfd);
	}
}

/*
 * Do the appropriate thing with the contact that has been found
 * acceptable in the table.
 */
docontact(cname, args)
	register unsigned char *cname;
	unsigned char *args;
{
	int chfd, i;
	int mode;
	unsigned char *argv[CHMAXARGS];
	struct chstatus chst;
	struct stat sbuf;

/*
 * Cory Myers 9/24/84
 *	server name can not contain '/' */

	if (index(cname, '/') != NULL) {
		refuse(cname, "Illegal server name");
		return;
	}
	if (stat(cname, &sbuf) < 0) {
		refuse(cname, "No server exists");
		return;
	} else if ((sbuf.st_mode & 0111) == 0) {
		refuse(cname, "Server not executable");
		return;
	}
	/*
	 * Group permissions used to indicate open modes.
	 */
	mode = (sbuf.st_mode & 020 ? (sbuf.st_mode & 040 ? 2 : 1) : 0);
	/*
	 * Open the connection, taking the RFC off the unmatched RFC list.
	 * If it fails, it either timeout aleady or was grabbed by another
	 * listener, both of which are unlikely.
	 */
	if ((chfd = chlisten(cname, mode, 1, 0)) < 0) {
		syslog(LOG_NOTICE, "chserver: listen failed on RFC: '%s %s'\n", cname, args);
		return;
	}
	if (ioctl(chfd, CHIOCGSTAT, &chst) < 0 || chst.st_state != CSRFCRCVD) {
		syslog(LOG_NOTICE, "chserver: bad listen status for: '%s %s'\n", cname, args);
		close(chfd);
		return;
	}
	/*
	 * Fork to do the real work, closing appropriate files.
	 */
	switch (fork()) {
	default:
#ifdef pdp11
		wait(0);
#endif
		close(chfd);
		break;
	case -1:
		syslog(LOG_NOTICE, "chserver: fork failed on '%s %s'\n", cname, args);
		chreject(chfd, "No process available for server.");
		close(chfd);
		break;
	case 0:
#ifdef pdp11
		if (fork())
			exit();
#endif
		for (i = 0; i < NOFILE; i++)
			if (i != chfd)
				close(i);
		if (makeargv(args, argv + 1)) {
			chreject(chfd, "Too many words in RFC.");
			syslog(LOG_NOTICE, "chserver: too many args: '%s %s'\n", cname, args);
			exit(1);
		}
		argv[0] = cname;
		dup2(chfd, 0);
		dup2(chfd, 1);
		dup2(1, 2);
		close(chfd);
		execv(cname, argv);
		chreject(1, "chserver: Can't EXEC server");
		exit(1);
	}
}

/*
 * File writing server - NO PROTECTION CHECKING!! for testing only.
 *
 */
#ifndef pdp11
/*
 * Handle child interrupts - just gobble the status to flush the zombie
 */
child()
{
	union wait w;
	int pid;

	while ((pid = wait3(&w.w_status, WNOHANG, 0)) > 0);
}
#endif
/*
 * Break up the given string into words, filling a pointer array.
 * Returns 0 if ok, !0 if more words than CHMAXARGS.
 */
makeargv(string, argp)
	unsigned char *string;
	register unsigned char **argp;
{

	unsigned char *cp = string;
	register int nargs = 0;

	while (*cp)
		if (++nargs > CHMAXARGS) {
			return (1);
			break;
		} else
			*argp++ = word(cp, &cp);
	*argp++ = NULL;
	return 0;
}

/*
 * Find a word, return a pointer to it, null terminate it, and update
 * *nextcp, to be able to use it to find the next word.
 */
#define space(x) \
    ((!is_lispm && ((x) == ' ' || (x) == '\t')) || (x) == CHLF)
unsigned char *
word(cp, nextcp)
	register unsigned char *cp;
	unsigned char **nextcp;
{
	register unsigned char *startp;

	while (space(*cp))
		cp++;
	startp = cp;
	if (*cp != '\0') {
		while (*++cp != '\0')
			if (space(*cp))
				break;
		if (*cp != '\0')
			*cp++ = '\0';	/* Null terminate that string */
	}
	if (nextcp)
		*nextcp = cp;
	return (startp);
}
