/*
 * chmailer - chaosnet mail daemon
 * Check for undelivered netmail and mail it.  Periodically
 * invoked by /etc/cron.  This mailer is just for hosts on the chaosnet.
 * It does have the option of knowing about a host on the chaosnet
 * which will forward mail to the arpanet.  If this host was on both this
 * program could be haired up to also send mail over the arpa net.
 *
 * Since delivermail uses syntax (:, @, ! etc) to determine the network,
 * it can't distinguish between networks thus always calls one mailer
 * for addresses with at-signs.  This program should be set-uid network.
 * This allows random people to run it if they want to get mail sent out
 * ASAP.  The program that spools for this mailer is also set-uid
 * network.  This means that the mail in the spool directory is protected,
 * but that normal users can cause it to get sent quickly.  Because the
 * locking mechanism uses links instead of creat("foo", 0444), the super user
 * can also run this program without worrying about the locks working,
 * thus running this from cron is no problem.
 *
 * This program should be put into /usr/lib/crontab to run every 5 minutes
 * with the line: 0,5,10,15,20,25,30,35,40,45,50,55 * * * * /etc/chmailer
 *
 * The lock used by this program and temporary files created by the spooler
 * should be removed at boot time in /etc/rc
 * by the line: rm -f /usr/spool/netmail/temp.* > /dev/console
 *
 * The message files contain three lines (sender, to-host, to-user) before
 * the message.  The attempt file contains the number of times the
 * message has been unsuccessfully attempted.
 * The "sender" should be enough to send a message back to when the
 * message can't be sent.  The "to-user" should not contain "to-host".
 *
 * Written by JEK, Symbolics
 *
 * If there is no arpa gateway on the chaosnet this can be anything,
 */
#define ARPAHOST	"mit-mc"

#include <stdio.h>
#include <ctype.h>
#ifdef BSD42
#include <sys/time.h>
#else
#include <time.h>
#endif
#include <signal.h>
#include <sys/types.h>
#include <sys/timeb.h>
#include <sys/stat.h>
#include <sys/dir.h>
#include <sys/ioctl.h>
#include <setjmp.h>
#include <hosttab.h>
#include <sys/chaos.h>

/*
 * This program will read in and sort up to MAXMESS filenames.
 * If there are more than this many messages, it sorts them in a
 * subprocess.
 */
#define MAXMESS		100
#define SPOOLDIR	"/usr/spool/netmail"	/* where messages are */
#define LOCKBASE	"Lock.Base"	/* Base file for link locking */
#define LOCKTEMP	"temp.lock"	/* Link created as a lock */
#define LOGFILE		"chmailer.log"	/* Log file for strange events */
#define MLENGTH		12	/* Length of file names after M */
#define LINEOK		0	/* From getline - line was read ok */
#define LINELONG	1	/* " - line was longer than buffer */
#define LINEEOF		2	/* " EOF when reading line */
#define RCPTSIZE	2000
#define MAXRCPTS	200
#define MAXLINE		133
#define UDATESIZE	sizeof("Sun Sep 19 11:22:33 1982")
#define ATSTRING	" at "
#define TIMEOUT		120	/* Seconds before we punt sendmail */
#define nputc(c, fp)	putc((c) == '\n' ? chnl : \
			     (c) == '\t' ? CHTAB : \
			     (c) == '\b' ? CHBS : \
			     (c) == '\f' ? CHFF : (c), fp)
/*
 * This array represents the timing of retransmissions.
 */
int times[] = { 5, 5, 5, 5, 5, 60, 60, 60, 60, 240, 240, 240, 720, 720,
	24 * 60, 24 * 60, 24 * 60, 0
};

#define DIRSIZ 14
extern int errno;
jmp_buf timejump;		/* Tag for net timeouts */
int timedout;			/* Have we timed out? */
FILE *log;			/* File pointer for log file */
char file[DIRSIZ + 1];		/* File name of current message */
char attempt[DIRSIZ + 1];	/* File name of attempt file for message */
char host[132];			/* Host current message is destined for */
char rcptbuf[RCPTSIZE];		/* Place to read recipients into */
char from[132];			/* Sender of current message */
char myself[132];		/* Name of this mail daemon for sendbacks */
char *me;			/* Name of this host */
int melength;			/* strlen(me) */
struct host_entry *myhost;	/* My host data */
char *atstring = ATSTRING;	/* String to use when appending host names */
char *gateway;			/* Gateway in use to reach other network */
int chnl = CHNL;
char *argv0;

/*
 * NFIXED is the number of fixed arguments to delivermail for local delivery.
 */
#define NFIXED 5
char *rcpts[MAXRCPTS + NFIXED] = { "-chaosmail", "-ee", "-i", "-a", "-m", 0 };

struct rerror {
	char *re_error;
	char *re_rcpt;
	struct rerror *re_next;
}     *rerrors, *retail;
char *sendmail(), *sprintf(), *sendtext(), *ctime(), *strcpy(), *strcat();
int timeout();
char *getdelim(), *getrcpts(), *malloc();
long lseek();
time_t time();

main(argc, argv)
	int argc;
	char **argv;
{
	register FILE *d;

	argv0 = argv[0];
	signal(SIGALRM, timeout);
	if ((myhost = host_here()) == NULL || (me = host_me()) == NULL)
		return (1);
	melength = strlen(me);
	(void)sprintf(myself, "CHAOS-MAILER", me);
	if (chdir(SPOOLDIR) < 0)
		return (1);
	if ((d = fopen(".", "r")) == NULL)
		return (1);
	if (access(LOCKBASE, 0) != 0)
		(void)close(creat(LOCKBASE, 0400));
	if (access(LOGFILE, 0) != 0)
		(void)close(creat(LOGFILE, 0600));
	close(2);
	if ((log = fopen(LOGFILE, "a")) == NULL)
		return (1);
	if (link(LOCKBASE, LOCKTEMP) < 0)
		return (1);
	if (argc == 2) {
		chnl = '\n';
		process();	/* file[0] will be zero so debug */
	} else
		do {
			dodirectory(d);
		} while (domessages());
	fclose(d);
	fclose(log);
	done(timedout);
	/*
	 * NOTREACHED 
	 */
}

dodirectory(d)
	FILE *d;
{
	struct direct dir;

	while (fread((char *)&dir, sizeof(dir), 1, d) == 1)
		if (dir.d_ino != 0 && dir.d_name[0] == 'M')
			dofile(dir.d_name);
}

char messages[MAXMESS][MLENGTH];
char *msp = messages[0];
int msin, msout;
int mspid;

dofile(name)
	char *name;
{
	if (msp < &messages[MAXMESS][0]) {
		strncpy(msp, name, MLENGTH);
		msp += MLENGTH;
		return;
	}
	if (msout == 0) {
		int inp[2], outp[2];
		int i;

		if (pipe(inp) || pipe(outp))
			fatal("Can't create pipes for message sorting");
		msin = inp[0];
		msout = outp[1];
		switch (mspid = fork()) {
		case -1:
			fatal("Can't fork process for sorting");
		 /*NOTREACHED*/ case 0:
			close(0);
			dup(outp[0]);
			close(1);
			dup(inp[1]);
			for (i = 3; i < 20; i++)
				close(i);
			execl("sort", "/bin/sort", 0);
			fatal("Can't exec /bin/sort");
			/*
			 * NOTREACHED 
			 */
		default:
			close(inp[1]);
			close(outp[0]);
		}
		for (msp = messages[0]; msp < messages[MAXMESS]; msp += MLENGTH)
			if (write(msout, msp, MLENGTH) != MLENGTH || write(msout, "\n", 1) != 1)
				fatal("Can't write sort pipe (1)");
	}
	name[MLENGTH] = '\n';
	if (write(msout, name, MLENGTH + 1) != MLENGTH + 1)
		fatal("Can't write sort pipe (2)");
}

mcmp(m1, m2)
	register char *m1, *m2;
{
	register int n, d;

	n = MLENGTH;

	do {
		if (d = *m1++ - *m2++)
			return (d);
	} while (--n);
	return 0;
}

domessages()
{
	int nprocessed = 0;

	if (msout) {
		int n;

		close(msout);
		msout = 0;
		while ((n = read(msin, file, MLENGTH + 1)) == MLENGTH + 1) {
			if (file[MLENGTH] != '\n')
				fatal("Bogus message file name from sort");
			nprocessed += process();
			(void)fflush(log);
			if (timedout)
				return (0);
		}
		if (n != 0)
			fatal("Bogus end record from sort: %d");
		if (mspid && wait(0) != mspid)
			fatal("Wait failed for sort subprocess");
		mspid = 0;
		close(msin);
	} else {
		register char *mp;

		qsort(messages[0], (msp - messages[0]) / MLENGTH, MLENGTH, mcmp);
		for (mp = messages[0]; mp < msp; mp += MLENGTH) {
			strncpy(file, mp, MLENGTH);

			nprocessed += process();
			(void)fflush(log);
			if (timedout)
				return (0);
		}
	}
	msp = messages[0];
	return (nprocessed);
}

/*
 * Check whether we should make an attempt and do it.
 * Check for an "attempt" file, indicating when transmission was last
 * attempted.  If none, then make an attempt now.  If there is one, make an
 * attempt if the time is right.
 * Return 1 if we have disposed of the message somehow, 0 else.
 */
process()
{
	FILE *m;
	int afd, i;
	long count = 0;
	char *error;
	time_t now;
	int done = 1;

	file[MLENGTH] = '\0';

	(void)time(&now);
	(void)sprintf(attempt, "A%s", &file[1]);

	if ((afd = open(attempt, 2)) >= 0)
		if (read(afd, (char *)&count, sizeof(count)) != sizeof(count))
			count = 0;
		else {
			struct stat abuf;

			if (fstat(afd, &abuf) < 0)
				fatal("Can't fstat attempt file\n");
			if ((now - abuf.st_mtime) / 60 < times[count]) {
				(void)close(afd);
				return 0;
			}
		}
	if ((m = file[0] ? fopen(file, "r") : fdopen(dup(0), "r")) == NULL) {
		fprintf(log, "Can't open message file: %s\n", file);

		/*
		 * Should we try and rename it then? 
		 */
		if (afd >= 0)
			(void)close(afd);
		fail();
		return 1;
	}
	count++;
	if ((i = getline(m, from, sizeof(from), '\n')) != LINEOK || (i = getline(m, host, sizeof(host), '\n')) == LINEEOF) {
		fail();
		done = 1;
		fprintf(log, "Garbage message file found: %s\n", file);
	} else if (i == LINELONG)
		sendback(m, "Destination host name too long", 1);
	else if (error = getrcpts(m))
		sendback(m, error, 1);
	else if (isme(host))
		sendlocal(m, rcpts, (char *)0);
	/*
	 * If we encounter an error while sending then if we are on the last
	 * attempt or the error was permanent, send the message back.
	 */
	else if ((error = sendmail(m)) && (times[count] == 0 || *error != '%'))
		sendback(m, error, 1);
	else if (error) {
		if (afd < 0) {
			if ((afd = creat(attempt, 0600)) < 0)
				fprintf(log, "Can't create attempt file (%d): %s\n", errno, attempt);
		} else
			(void)lseek(afd, 0L, 0);
		if (afd >= 0) {
			/*
			 * If any part of the attempt log fails we remove the
			 * file so at worst we start transmitting over again.
			 */
			if (write(afd, (char *)&count, sizeof(count)) < 0 || lseek(afd, 0L, 2) == -1L || write(afd, ctime(&now), 26) < 0 || write(afd, error, strlen(error)) < 0 || write(afd, "\n", 1) < 0)
				unlink(attempt);
			(void)close(afd);
		}
		(void)fclose(m);
		done = 0;
		goto out;
	} else if (rerrors)
		sendback(m, "The message did not reach the following recipients:", 0);
	/*
	 * Here either we had a permanent error and sent back a message to
	 * the sender or we succeeded.  In either case we're done with the
	 * message.
	 */
	if (afd >= 0)
		(void)close(afd);
	(void)unlink(attempt);
	(void)fclose(m);
	(void)unlink(file);

      out:
	if (rerrors) {
		register struct rerror *rp;

		for (rp = rerrors; rp;) {
			struct rerror *orp = rp;

			rp = rp->re_next;
			free((char *)orp);
		}
		rerrors = (struct rerror *)0;
	}
	return (done);
}

/*
 * Get the recipients out of the file.
 * One per line, ending on a blank line.
 */
char *
getrcpts(m)
	register FILE *m;
{
	register char *cp = rcptbuf;
	register char **rcp = &rcpts[NFIXED];
	int rcptsize = RCPTSIZE;

	for (;;) {
		switch (getline(m, cp, rcptsize, '\n')) {
		case LINEEOF:
			return "EOF while reading recipients";
		case LINELONG:
			return "Not enough room for recipients";
		default:
			if (*cp == '\0') {
				*rcp = 0;
				return 0;
			}
		}
		if (rcp >= &rcpts[MAXRCPTS + NFIXED - 1])
			return "Too many recipients";
		*rcp = cp;
		while (*cp++);
		rcptsize -= cp - *rcp++;
	}
}


/*
 * Get the next input line into the given buffer of the given length.
 * Use the given character as the line terminator.
 * If the input line is longer than the buffer, read the whole line,
 * but return the LINELONG value.
 * LINEOK is the return value for a normal successful read.
 * LINEEOF means an eof encountered.
 * In all cases make sure the buffer is properly terminated.
 */
getline(f, buf, length, term)
	FILE *f;
	register char *buf;
{
	register int c;

	while ((c = getc(f)) != term)
		if (c == EOF) {
			*buf = '\0';
			return LINEEOF;
		} else if (--length > 0)
			*buf++ = c;
	*buf++ = '\0';
	return length <= 0 ? LINELONG : LINEOK;
}

/*
 * Fatal error - blow out and log reason
 */
fatal(s)
	char *s;
{
	time_t now = time((time_t *) 0);

	fprintf(log, "%s", ctime(&now));
	fprintf(log, "Fatal error: %s\n", s);
	done(0);
}

/*
 * Exit cleanly, fixing up locks.
 * If arg is non zero, exec ourselves again, closing any files that may
 * be open.
 */
done(n)
{
	(void)unlink(LOCKTEMP);
	if (n != 0) {
		register int i;

		for (i = 3; i < 20; i++)
			close(i);
		execl(argv0, argv0, 0);
	}
	exit(n);
}

/*
 * Send mail back to sender informing him of error
 * M is the file the message is in.
 * Errors here are mostly fatal since we don't want to drop anything on the
 * floor.
 */
sendback(m, error, bad)
	FILE *m;
	char *error;
{
	static char *argv[] = { "-errormail", "-f", myself, "-eq", "-i", from, 0 };

	sendlocal(m, argv, error, bad);
}

sendlocal(m, argv, error, bad)
	FILE *m;
	char **argv;
	char *error;
{
	int p[2], n, status;
	register int pid, dmpid;
	register FILE *o;

	if (pipe(p) < 0)
		fatal("Can't make a pipe");
	if ((dmpid = fork()) == 0) {
		(void)close(0);
		(void)dup(p[0]);
		for (n = 3; n < 20; n++)
			(void)close(n);
		execv("/etc/delivermail", argv);
		exit(1);
	} else if (dmpid == -1)
		fatal("Can't fork for delivermail");
	(void)close(p[0]);
	if ((o = fdopen(p[1], "w")) == NULL)
		fatal("Can't open buffered io to delivermail");
	if (error) {
		fprintf(o, "To: %s\n", from);
		fprintf(o, "Subject: Unable to deliver mail\n\n");
		if (bad) {
			fprintf(o, "A message to host '%s' could not be sent.\n", host);
			fprintf(o, "The reason is: %s\n", error);
		} else
			fprintf(o, "While sending to host: %s\n%s\n", host, error);
		if (rerrors) {
			register struct rerror *rp;

			for (rp = rerrors; rp; rp = rp->re_next)
				fprintf(o, "For recipient \"%s\", the response was: %s\n", rp->re_rcpt, rp->re_error);
		}
		fprintf(o, "---The text of the message follows---\n");
	}
	if (feof(m)) {
		fprintf(o, "The message text itself was lost\n");
		fprintf(log, "Message lost: %s\n", file);
	} else {
		int c;

		while ((c = getc(m)) != EOF)
			putc(c, o);
	}
	if (ferror(o))
		fatal("Error writing message to delivermail");
	(void)fclose(o);
	while ((pid = wait(&status)) >= 0 && mspid != 0 && pid == mspid)
		mspid = 0;
	if (pid < 0 || pid != dmpid || status != 0) {
		fprintf(log, "Wait error sending back to '%s'\n", from);
		fprintf(log, " sending file %s to %s at host '%s'\n", file, rcpts[NFIXED], host);

		fprintf(log, "Dmpid: %d, Status: 0%o, Pid: %d\n", dmpid, status, pid);
		fail();
	} else if (error) {
		time_t now = time((time_t *) 0);

		fprintf(log, "%s", ctime(&now));
		fprintf(log, "Sendback to %s. Was to '%s' at %s. Error: %s\n", from, rcpts[NFIXED], host, error);
	}
}

/*
 * Dump mail we can't return to anyone into the
 * failed subdirectory.
 */
fail()
{
	char newfile[sizeof("failed/") + DIRSIZ + 1];

	sprintf(newfile, "failed/%s", file);
	if (link(file, newfile) == 0)
		unlink(file);
}

/*
 * Send mail using the ChaosNet mail protocol
 */
char *
sendmail(m)
	register FILE *m;
{
	register FILE *infp = NULL, *outfp = NULL;
	int conn = -1;
	int addr, good = 0, temporary = 0;
	static char line[MAXLINE];	/* Must be static - we return its contents */
	char *error = NULL;
	char **rcp;

	gateway = 0;
	if ((addr = chaos_addr(host)) == 0) {
		if (arpa_addr(host) != 0) {
			if ((addr = chaos_addr(ARPAHOST)) == 0)
				return sprintf(line, "-Unusable arpanet gateway: %s", ARPAHOST);
			else
				gateway = ARPAHOST;
		} else
			return sprintf(line, "-Unknown destination host: %s", host);
	}
	if (setjmp(timejump) != 0) {
		sprintf(line, "%%Timed out (2 minutes) sending to host: %s", gateway ? ARPAHOST : host);
		error = line;
		goto out;
	} else
		alarm(TIMEOUT);

	if ((conn = file[0] ? chopen(addr, CHAOS_MAIL, 2, 0, 0, 0, 0) : dup(2))<0) {
		alarm(0);
		return (gateway ? sprintf(line, "%%Couldn't connect to gateway host: %s", ARPAHOST) : sprintf(line, "%%Couldn't connect to destination host: %s", host));
	}
	if ((infp = fdopen(conn, "r")) == NULL || (outfp = fdopen(dup(conn), "w")) == NULL)
		fatal("Couldn't allocate network buffered i/o");
	/*
	 * Send the recipients and check out the response.
	 */
	for (rcp = &rcpts[NFIXED]; *rcp; rcp++) {
		if (gateway)
			fprintf(outfp, "%s@%s%c", *rcp, host, chnl);
		else
			fprintf(outfp, "%s%c", *rcp, chnl);
		(void)fflush(outfp);
		if (ferror(outfp) || (file[0] && ioctl(fileno(outfp), CHIOCFLUSH, 0) < 0)||getline(infp, line, sizeof(line), chnl) == LINEEOF)
			goto neterr;
		if (line[0] != '+') {
			struct rerror *ep;

			if (line[0] == '%')
				temporary++;
			if ((ep = (struct rerror *)malloc(sizeof(struct rerror))) == NULL || (ep->re_error = malloc((unsigned)(strlen(line) + 1))) == NULL)
				fatal("Can't allocate memory for error");
			strcpy(ep->re_error, line);
			ep->re_rcpt = *rcp;
			ep->re_next = 0;
			if (rerrors) {
				retail->re_next = ep;
				retail = ep;
			} else
				rerrors = retail = ep;
		} else
			good++;
	}
	if (good == 0) {
		error = temporary != 0 ? "%There are temporary errors for some recipients" : "-There are no valid recipients";
		goto out;
	} else {
		register int c;

		/*
		 * Put out the blank line for the end of recipients
		 * And send the rest of the message followed by an
		 * EOF, and wait for it to be acknowledged
		 */
		putc(chnl, outfp);
		if ((error = sendtext(m, outfp, line)) != 0) {
			chreject(conn, error);
			goto out;
		}
		(void)fflush(outfp);
		if (ferror(outfp) || (file[0] && ioctl(fileno(outfp), CHIOCOWAIT, 1) < 0)||getline(infp, line, sizeof(line), chnl) == LINEEOF)
			goto neterr;
		if (line[0] != '+') {
			if (strlen(line) + sizeof("(From host: )") + strlen(gateway ? ARPAHOST : host) < MAXLINE) {
				strcat(line, "(From host: ");
				strcat(line, gateway ? ARPAHOST : host);
				strcat(line, ")");
			}
			error = line;
		}
	}
	goto out;
      neterr:
	(void)sprintf(line, "%%%s connection to host %s died unexpectedly", gateway ? "Gateway" : "Network", gateway ? ARPAHOST : host);
	error = line;
      out:
	alarm(0);
	if (conn >= 0)
		chreject(conn, 0);
	if (infp != NULL)
		(void)fclose(infp);
	if (outfp != NULL)
		(void)fclose(outfp);
	return error;
}

/*
 * Send the text of the message to the network, making the necessary
 * transformations.
 */
char *
sendtext(in, out, buf)
	register FILE *in;
	register FILE *out;
	char *buf;
{
	register int i;
	long pos, pos0, ftell();
	char udate[UDATESIZE + 1];	/* +1 for newline from ctime!!! */

	udate[0] = '\0';
	/*
	 * Strip off UNIX From line - no-one else likes it
	 * Remember the date in case we need it later.
	 */
	pos0 = pos = ftell(in);
	if ((i = getline(in, buf, MAXLINE, '\n')) == LINELONG)
		goto headerr;
	if (strncmp("From ", buf, 5) == 0) {
		register char *cp;

		for (cp = &buf[5]; *cp && isspace(*cp); cp++);
		while (*cp && !isspace(*cp++));
		while (*cp && isspace(*cp))
			cp++;
		/*
		 * This wont work with uucp headers ended by "remote from" 
		 */
		if (*cp && strlen(cp) == UDATESIZE - 1)
			strcpy(udate, cp);
		pos = ftell(in);
		if (i == LINEOK && (i = getline(in, buf, MAXLINE, '\n')) == LINELONG)
			goto headerr;
	}
	/*
	 * Check the first line for the date.  If not found,
	 * assume the message is not in arpanet standard format
	 * and output a "Date:" and "From:" header.
	 */
	if (i == LINEEOF || (matchhdr(buf, "date") == NULL && matchhdr(buf, "received") == NULL)) {
		if (udate[0] == '\0') {
			struct stat sbuf;

			if (fstat(fileno(in), &sbuf) == 0) {
				strcpy(udate, ctime(&sbuf.st_mtime));
				udate[UDATESIZE - 1] = '\0';	/* \n! */
			}
		}
		putdate(out, udate[0] ? udate : NULL);
	}
	if (i == LINEEOF)
		return 0;
	fseek(in, pos, 0);
	while (ishdr(in, buf, MAXLINE)) {
		nputs(buf, out);
		if (matchhdr(buf, "to") || matchhdr(buf, "cc") || matchhdr(buf, "from"))
			rewrite(in, out);
		else
			copyhdr(in, out);
	}
	/*
	 * If the first non-header line isn't blank, insert a blank line.
	 */
	if (buf[0] != '\n')
		putc(chnl, out);
	nputs(buf, out);
	while ((i = getc(in)) != EOF)
		nputc(i, out);
	fseek(in, pos0, 0);
	return 0;
      headerr:
	fseek(in, pos0, 0);
	return i == LINELONG ? "-Header line too long" : "-EOF in header";
}

/*
 * Rewrite a header line, putting commas between addresses instead of the
 * spaces UNIX might have used.
 * "cp" points to the beginning of the first line of the header.
 * "in" is the source of further lines
 * "out" is the destination of the rewritten header.
 */
#define WSEP	", "
#define TEOF	0
#define	TAT	1
#define	TWORD	2
#define	MAXTOKENS	100

struct token {
	char *t_addr;
	char *t_end;
	int t_type;
} tokens[MAXTOKENS];

rewrite(in, out)
	register FILE *in;
	FILE *out;
{
	register int c;
	register struct token *t;
	register char *cp;
	int atom, ch;
	char addr[BUFSIZ];

	for (atom = 0, t = tokens, cp = addr;; *cp++ = c) {
		c = getc(in);
		if (atom && (c == EOF || index("\n \t()<>@%,;:\\\"", c))) {
			t->t_end = cp;
			if (cp - t->t_addr == 2 && (cp[-2] == 'a' || cp[-2] == 'A') && (cp[-1] == 't' || cp[-1] == 'T')) {
				atstring = " at ";
				t->t_type = TAT;
			} else
				t->t_type = TWORD;
			t++;
			atom = 0;
		}
		if (c == '\n' && ((ch = peekc(in)) == ' ' || ch == '\t'))
			continue;

		/*
		 * Break is special in this switch - falls through to break 
		 */
		switch (c) {
		case '"':	/* Gobble up quoted string - treat as word */
			t->t_type = TWORD;
			t->t_addr = cp;
			cp = getdelim(cp, in, '"');
			t->t_end = cp + 1;
			t++;
			if (*cp == '\0') {
				t[-1].t_end--;	/* K*L*U*D*G*E */
				goto finish;
			}
			continue;
		case '(':	/* Gobble up comment - no tokens */
			cp = getdelim(cp, in, '(');
			if (*cp == '\0')
				goto finish;
			else
				c = ')';
			continue;
		      finish:
			c = EOF;
		case '\n':	/* Folding above, so this is really the end */
		case EOF:
		case ',':
		case '>':
		case ';':
			*cp = '\0';
			t->t_type = TEOF;
			t->t_end = t->t_addr = cp;
			putaddr(addr, tokens, out);
			if (c == '\n' || c == EOF)
				break;
			t = tokens;
			cp = addr;
			continue;
		case ':':
		case '<':	/* Everything before this is extraneous */
			t = tokens;
			continue;
		case ')':	/* Unbalanced paren - it delimit and ignore */
		case '\\':	/* Outside quotes or comment - do the same */
		case ' ':
		case '\t':
			continue;
		case '%':	/* Not RFC733 but some use it */
			c = '@';
		case '@':
			t->t_type = TAT;
			t->t_addr = cp;
			t->t_end = cp + 1;
			atstring = "@";
			t++;
			continue;
		default:
			if (atom == 0) {
				atom = 1;
				t->t_addr = cp;
			}
			continue;
		}
		break;
	}
	putc(chnl, out);
}

char *
getdelim(cp, in, open)
	register char *cp;
	register FILE *in;
{
	register int c;
	register int backslash = 0;
	int pcount = 1;

	for (*cp++ = open; (c = getc(in)) != EOF; cp++) {
		/*
		 * A newline just gets stuck in if followed by white space.
		 * Otherwise we end the buffer with it so caller finds out.
		 */
		if (c == '\n')
			if ((c = getc(in)) == ' ' || c == '\t')
				*cp++ = '\n';
			else {
				c = EOF;
				ungetc(c, in);
				break;
			}
		*cp = c;
		if (backslash == 0) {
			if (c == '\\')
				backslash = 1;
			else if (c == '(')
				pcount++;
			else if (c == ')') {
				pcount--;
				if (pcount == 0 && open == '(')
					break;
			} else if (c == '"' && open == '"')
				break;
		} else
			backslash = 0;

	}
	if (c == EOF) {
		if (open == '"')
			*cp++ = '"';
		else
			while (pcount--)
				*cp++ = ')';
		*cp = '\0';
	}
	return cp;
}

/*
 * Given a parsed header - as a string and a set of tokens,
 * put it out, making the required host name transformations.
 * Note that if there are any AT's, we treat random word sequences as
 * single mailboxs as per RFC733.  If there are no AT's, implying all
 * local names, we treat each word as a separate address.  This compensates
 * for UCBMAIL's behavior where if there are local mailing lists that
 * expand into remote recipients, ucbmail doesn't bother with commas
 * between addresses.
 *
 * Actions possible on each token in the address:
 */
#define PRAT		010	/* Put out an AT */
#define PSAVE		020	/* Print saved host */
#define LOCAL		040	/* Put out the local host */
#define MAYBE		0100	/* Maybe make into a separate address */
#define GATE		0200	/* Print gateway if last host wants it */
#define COMMA		0400	/* Insert separator between addresses */
#define	DUMP		01000	/* Dump out stuff before this token */
#define PRINT		02000	/* Print this token and its preceding junk */
#define SAVE		04000	/* Save the current token, print preceding */
#define PPREV		010000	/* Print the previous token */
/*
 * Define states
 */
#define STATE(x)	((x) & 7)
#define START	0
#define USER	1
#define AT	2
#define HOST	3
#define AT2	4
#define END	5
/*	TEOF				TAT		TWORD */
int
    sstart[] = {
	DUMP | END, DUMP | START, PRINT | USER
}, suser[] = {

	PRAT | LOCAL | GATE | DUMP | END, DUMP | AT, MAYBE | PRINT | USER
}, sat[] = {

	PRAT | LOCAL | GATE | DUMP | END, DUMP | AT, PPREV | DUMP | SAVE | HOST
}, shost[] = {

	PSAVE | GATE | DUMP | END, PSAVE | DUMP | AT2, PSAVE | GATE | COMMA | PRINT | USER
}, sat2[] = {

	GATE | DUMP | END, DUMP | AT2, PPREV | DUMP | SAVE | HOST
}

;
int *states[] = { sstart, suser, sat, shost, sat2, 0 };

putaddr(cp, tokens, out)
	register char *cp;
	struct token *tokens;
	register FILE *out;
{
	register struct token *t;
	register int work, c;
	struct host_entry *host;
	struct token *save;
	int *state;
	int atfound;

	for (atfound = 0, t = tokens; t->t_type != TEOF; t++)
		if (t->t_type == TAT) {
			atfound++;
			break;
		}
	for (t = tokens, state = states[START]; state; cp = t->t_end, t++) {
		work = state[t->t_type];
		state = states[STATE(work)];
		if ((work & MAYBE) && !atfound)
			work |= (PRAT | LOCAL | GATE | COMMA);
		if (work & PRAT)
			fputs(atstring, out);
		else if (work & PPREV) {
			t--;
			c = *t->t_end;
			*t->t_end = '\0';
			fputs(t->t_addr, out);
			*t->t_end = c;
			t++;
		}
		if (work & PSAVE) {
			c = *save->t_end;
			*save->t_end = '\0';
/* Disable canonicalization of host names - the user ends, if any, should do it
			if ((host = host_info(save->t_addr)) != NULL)
				fputs(host->host_name, out);
			else
 */
			fputs(save->t_addr, out);

			*save->t_end = c;
		} else if (work & LOCAL) {
			fputs(me, out);
			host = myhost;
		}
		if (work & GATE && host != NULL && gateway != 0 && arpa_host(host) == 0) {
			fputs(atstring, out);
			fputs(gateway, out);
		}
		if (work & COMMA)
			fputs(WSEP, out);
		if (work & DUMP)
			while (cp < t->t_addr) {
				c = *cp++;
				nputc(c, out);
		} else if (work & PRINT)
			while (cp < t->t_end) {
				c = *cp++;
				nputc(c, out);
			}
		if (work & SAVE)
			save = t;
	}
}

peekc(fp)
	register FILE *fp;
{
	register int c;

	c = getc(fp);
	if (c != EOF)
		ungetc(c, fp);
	return (c);
}

/*
 * Read enough of the input to decide whether we are at a header line.
 * If it is a header we expect the buffer to contain contents up to and
 * including the colon.
 */
ishdr(in, p, length)
	register FILE *in;
	register char *p;
{
	register int c;

	if (--length && (c = getc(in)) != EOF) {
		*p++ = c;
		if (!isspace(c) && c != ':') {
			while (--length && (c = getc(in)) != EOF) {
				*p++ = c;
				if (c == ':') {
					*p = '\0';
					return 1;
				} else if (c == '\n')
					break;
			}
		}
	}
	*p = '\0';
	return 0;
}

/*
 * Is p a header line of type q?
 * Type is assumed to contain no spaces.
 */
matchhdr(p, q)
	register char *p;
	register char *q;
{
	for (; *q != '\0'; p++, q++)
		if ((isupper(*p) ? tolower(*p) : *p) != *q)
			return (NULL);
	while (isspace(*p))
		p++;
	if (*p != ':')
		return (NULL);
	return 1;
}

char *
arpadate(ud)
	register char *ud;	/* the unix date */
{
	register char *p;
	time_t t;
	struct timeb info;
	static char b[40];
	extern struct tm *localtime();
	extern char *timezone();

	time(&t);
	ftime(&info);
	if (ud == NULL)
		ud = ctime(&t);
	ud[3] = ud[7] = ud[10] = ud[19] = ud[24] = '\0';
	p = &ud[8];		/* 16 */
	if (*p == ' ')
		p++;
	strcpy(b, p);
	strcat(b, " ");
	strcat(b, &ud[4]);	/* Sep */
	strcat(b, " ");
	strcat(b, &ud[20]);	/* 1979 */
	strcat(b, " ");
	strcat(b, &ud[11]);	/* 01:03:52 */
	strcat(b, "-");
	/*
	 * Note well that the timezone is based on NOW, not on the incoming
	 * date!! But the message shouldn't be in the system for too long.
	 */
	strcat(b, timezone(info.timezone, localtime(&t)->tm_isdst));
	return (b);
}

putdate(fp, ud)
	register FILE *fp;
	char *ud;
{
	register char *p;

	fputs("Date: ", fp);
	fputs(arpadate(ud), fp);

	putc(chnl, fp);
	fputs("From: ", fp);
	for (p = from; *p; p++)
		putc(*p == ':' ? '.' : *p, fp);
	for (p = from; *p && *p != '@' && *p != '%'; p++);
	if (*p == 0) {
		fputs(atstring, fp);
		fputs(me, fp);
	}
	if (gateway) {
		fputs(atstring, fp);
		fputs(gateway, fp);
	}
	putc(chnl, fp);
}

/*
 * Copy the remainder of this header line and its continuation lines.
 */
copyhdr(in, out)
	register FILE *in;
	register FILE *out;
{
	register int c;

	while ((c = getc(in)) != EOF) {
		nputc(c, out);
		if (c == '\n')
			if ((c = peekc(in)) != ' ' && c != '\t')
				return;
	}
}

nputs(s, fp)
	register char *s;
	register FILE *fp;
{
	register int c;

	while (c = *s++)
		nputc(c, fp);
}

timeout()
{
	timedout++;
	longjmp(timejump, 1);
}

isme(name)
	char *name;
{
	static struct host_entry *me;
	register char **ap;

	if (!me)
		me = host_here();
	if (strcmp(name, me->host_name) == 0)
		return 1;
	for (ap = me->host_nicnames; *ap; ap++)
		if (strcmp(name, *ap) == 0)
			return 1;
	return 0;
}
