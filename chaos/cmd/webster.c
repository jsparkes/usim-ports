/*
 *  webster.c - a simple webster program
 *  hacked up in a moment of weakness -sundar Tue Sep 24,1985 at 04:47:13
 */
#include <stdio.h>
#include <signal.h>
#include <errno.h>!ma
#include <hosttab.h>
#include <sys/chaos.h>

int fd = -1;
int chnl = CHNL;
int onintr();
int spellonly = 0;		/* if set, ask only for spelling */
int wordflag = 0;		/* if we have a word */
int debug = 0;			/* debugflag */

#define LINEOK		0	/* From getline - line was read ok */
#define LINELONG	1	/* " - line was longer than buffer */
#define LINEEOF		2	/* " EOF when reading line */
#define MAXLINE		133

main(argc, argv)
	char **argv;
{
	register FILE *infp = NULL, *outfp = NULL;
	char hostname[80];
	char line[MAXLINE];
	char inbuf[MAXLINE];
	char outbuf[MAXLINE];
	register int i, arg;
	int addr;
	int timeout(), bye();

	if (argc < 2) {
		printf("Usage: \n");
		printf("Flags: -h <host> - uses <host> instead of \"webster\"\n");
		printf("       -d        - turns on debugging\n");
		printf("       -s        - spelling check only\n");
		printf("       <word>    - <word> to access. Wildcards are %c and %c\n", '*', '%');
		exit(0);
	}

	strcpy(hostname, "webster");
	arg = 1;
	while (arg < argc) {
		if (argv[arg][0] == '-') {
			switch (argv[arg][1]) {
			case 'h':
				arg++;
				strcpy(hostname, argv[arg]);
				arg++;
				break;
			case 's':
				spellonly++;
				arg++;
				break;
			case 'd':
				debug++;
				arg++;
				break;
			}
		} else {
			if (wordflag) {
				fprintf(stderr, "Only one word, please.\n");
				exit(0);
			} else {
				strcpy(inbuf, argv[arg++]);
				wordflag++;
			}
		}
	}

	if (wordflag == 0) {
		fprintf(stderr, "No word?\n");
		exit(0);
	}

	if ((addr = chaos_addr(hostname, 0)) == 0) {
		fprintf(stderr, "Unknown chaos host: %s\n", hostname);
		exit(0);
	}
	if (debug)
		printf("Trying %s at address %o ... ", hostname, addr);
	fflush(stdout);
	if ((fd = chopen(addr, "DICTIONARY", 2, 0, 0, 0, 0)) < 0) {
		printf("Cannot open chaos contact.\n");
		return (-1);
	}
	signal(SIGALRM, timeout);
	alarm(15);
	if (debug)
		printf(" Open\n");
	fflush(stdout);

	if ((infp = fdopen(fd, "r")) == NULL || (outfp = fdopen(dup(fd), "w")) == NULL) {
		fprintf(stderr, "Couldnt allocate network buffered i/o?\n");
		exit(1);
	}
	signal(SIGINT, onintr);
	if (spellonly == 0) {
		strcpy(outbuf, "DEFINE ");
		strcat(outbuf, inbuf);
	} else {
		strcpy(outbuf, "SPELL ");
		strcat(outbuf, inbuf);
	}
	if (debug)
		printf("Sending %s\n", outbuf);
	for (i = 0; i < MAXLINE; i++)
		line[i] = '\0';
	fprintf(outfp, "%s%c", outbuf, chnl);
	(void)fflush(outfp);
	if (ferror(outfp))
		goto end;
	switch (getline(infp, line, sizeof(line), chnl)) {
	case LINEOK:
		if (strcmpn(line, "WILD", 4) == 0) {
			if (parse_wild(line, infp) == 0)
				goto end;
		} else if (strcmpn(line, "SPELL", 5) == 0) {
			if (parse_spell(line, infp) == 0)
				goto end;
		} else if (strcmpn(line, "DEFINITION", 10) == 0) {
			if (parse_defun(line, infp) == 0)
				goto end;
		} else if (strcmpn(line, "ERROR", 5) == 0) {
			if (strcmpn(&line[6], "FATAL", 5) == 0)
				printf("Fatal Error: %s\n", &line[11]);
			else
				printf("Recoverable Error: %s\n", &line[17]);
		} else
			printf("Unknown response? %s\n", line);
		break;
	case LINEEOF:
		printf("EOF\n");
		break;
	case LINELONG:
		printf("Long line: \"%s\"\n", line);
		break;
	}
      end:if (infp != NULL) {
		(void)fclose(infp);
	}
	if (outfp != NULL) {
		(void)fclose(outfp);
	}
	call(bye, "bye", 0);
	return (0);
}

onintr()
{
	call(bye, "bye", 0);
	return (0);
}

parse_wild(line, fp)
	char *line;
	FILE *fp;
{
	char buf[MAXLINE];
	register int i;

	switch (line[4]) {
	case ' ':
		if (line[5] == '0')
			printf("Wildcard: No matches found.\n");
		return (0);
	case '\0':
		printf("\tWildcard matches:\n");
		while (getline(fp, buf, sizeof(buf), chnl) != LINEEOF) {
			for (i = 0; buf[i] != ' '; i++)
				putchar(buf[i]);
			putchar('\t');
			printf("%s\n", &buf[i]);
		}
		return (0);
	default:
		printf("Unknown wild response? %s\n", line);
		return (-1);
	}
}

parse_spell(line, fp)
	char *line;
	FILE *fp;
{
	char buf[MAXLINE];
	register int i;

	switch (line[8]) {
	case ' ':
		if (line[9] == '0')
			printf("Spell: No matches found. I cant make any sense out of the word you typed.\n");
		return (0);
	case '\0':
		printf("Spelling Mistake? \n");
		while (getline(fp, buf, sizeof(buf), chnl) != LINEEOF) {
			for (i = 0; buf[i] != ' '; i++)
				putchar(buf[i]);
			putchar('\t');
			printf("%s\n", &buf[i]);
		}
		return (0);
	default:
		printf("Unknown spell response? %s\n", line);
		return (-1);
	}
}


parse_defun(line, fp)
	char *line;
	FILE *fp;
{
	char buf[MAXLINE];
	register int i, idx, j;
	register int c;

	switch (line[10]) {
	case ' ':
		i = atoi(&line[11]);
		if (i > 0)
			printf("There are %d relevant entries.\n", i);
		for (idx = 0; idx < i; idx++) {
			if (getline(fp, buf, sizeof(buf), chnl) == LINEOK) {
				for (j = 0; buf[j] != ' '; j++)
					putchar(buf[j]);
				putchar('\t');
				printf("%s\n", &buf[j]);
			}
		}
		while ((c = getc(fp)) != EOF)
			putchar(c);
		putchar('\n');
		fflush(stdout);
		return (0);
	default:
		printf("Unknown definition response? %s\n", line);
		return (-1);
	}
}

/*
 * call routine with argc, argv set from args (terminated by 0).
 * VARARGS1
 */
call(routine, args)
	int (*routine)();
	int args;
{
	register int *argp;
	register int argc;

	for (argc = 0, argp = &args; *argp++ != 0; argc++);
	return ((*routine) (argc, &args));
}


bye(argc, argv)
	int argc;
	char *argv[];
{
	register int c;

	if (argc <= 0) {	/* give help */
		printf("Disconnect from the current site.\n");
		return (0);
	}
	if (fd >= 0) {
		ioctl(fd, CHIOCOWAIT, 1);	/* sends an EOF first */
		chreject(fd, "");	/* must send a CLS explicitly */
		close(fd);
		fd = -1;
		if (debug)
			printf("Connection closed\n");
	}
	return (0);
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

/*dummy for now*/
timeout()
{
}
