#include <stdio.h>
#include <signal.h>
#include <sysexits.h>
#include <sys/chaos.h>

/*
 * Chaos MAIL user side.  Run this program once to make an attempt at delivery.
 * First arg is host name, rest are recipients.
 */
int conn;

#define TIMEOUT 300		/* If a response takes longer than 5 minutes, punt */
timeout()
{
	finish(EX_TEMPFAIL, "Timeout - your responses are too slow");
}

finish(code, message)
	char *message;
{
	if (conn > 0)
		chreject(conn, message);
	exit(code);
}

main(argc, argv)
	int argc;
	char **argv;
{
	register FILE *infp, *outfp;
	register int c;
	char **ap;
	int conn, addr, good = 0, temporary = 0, bad = 0;

	if ((addr = chaos_addr(argv[1])) == 0)
		finish(EX_NOHOST, 0);
	signal(SIGALRM, timeout);
	alarm(TIMEOUT);
	if ((conn = chopen(addr, "MAIL", 2, 0, 0, 0, 0)) < 0) {
		alarm(0);
		finish(EX_TEMPFAIL, 0);
	}
	alarm(0);
	if ((infp = fdopen(conn, "r")) == NULL || (outfp = fdopen(dup(conn), "w")) == NULL)
		finish(EX_SOFTWARE, "Software error");
	/*
	 * Send the recipients and check out the response.
	 */
	for (ap = &argv[2]; *ap; ap++) {
		fprintf(outfp, "%s%c", *ap, CHNL);
		(void)fflush(outfp);
		if (ferror(outfp) || ioctl(fileno(outfp), CHIOCFLUSH, 0) < 0 || (c = getc(infp)) == EOF)
			finish(EX_TEMPFAIL, "Connection disappeared");
		if (c == '+')
			good++;
		else if (c == '%')
			temporary++;
		else
			bad++;
		while ((c = getc(infp)) != CHNL)
			if (c == EOF)
				finish(EX_TEMPFAIL, "Connection disappeared");
	}
	if (bad + temporary > 0)
		finish(bad ? EX_NOUSER : EX_TEMPFAIL, "No valid recepients, punting");
	/*
	 * Put out the blank line for the end of recipients
	 * And send the rest of the message followed by an
	 * EOF, and wait for it to be acknowledged
	 */
	putc(CHNL, outfp);
	while ((c = getchar()) != EOF)
		putc((c) == '\n' ? CHNL : (c) == '\t' ? CHTAB : (c) == '\b' ? CHBS : (c) == '\f' ? CHFF : (c), outfp);
	(void)fflush(outfp);
	if (ferror(outfp) || ioctl(fileno(outfp), CHIOCOWAIT, 1) < 0 || (c = getc(infp)) == EOF)
		finish(EX_TEMPFAIL, "Connection disappeared");
	{
		char buf[800], *bp = buf;

		*bp++ = c;
		*bp = 0;
		while ((*bp = getc(infp)) != EOF && bp - buf < 798)
			*++bp = 0;
		printf("%s\n", buf);
	}
	finish(c == '+' ? 0 : c == '%' ? EX_TEMPFAIL : EX_UNAVAILABLE, 0);
}
