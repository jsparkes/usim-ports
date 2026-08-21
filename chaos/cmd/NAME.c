/*
 * Chaosnet RFC handler for contact name NAME
 */

#include <stdio.h>

char *fargv[50];

main(argc, argv)
	register int argc;
	register char *argv[];
{

	register FILE *f;
	register int c;
	register char **t;
	register char *arg;
	register int lflag = 0;
	char *slash, *index();

	int p[2];

	t = &fargv[0];
	*t++ = "finger";
	while (--argc > 0) {
		arg = *++argv;
		/*
		 * look for a /w in the arg 
		 */
		slash = index(arg, '/');
		if (slash != NULL && (slash[1] == 'w' || slash[1] == 'W')) {
			lflag = 1;
			*t++ = "-l";
			if (slash != arg) {	/* non-null username */
				*slash = '\0';
				*t++ = arg;
			}
		} else {
			if (!lflag)
				*t++ = "-p";	/* short form => no plan file */
			*t++ = arg;
		}
	}
	*t = 0;
	if (pipe(p) < 0) {
		printf("Can't create pipe\215");
		return (2);
	}
	switch (fork()) {
	case -1:
		printf("Can't create fork for name server\215");
		return (1);

	case 0:
		close(p[0]);
		close(1);
		dup(p[1]);
		close(0);
		execv("/usr/local/bin/finger", fargv);
		execv("/usr/local/finger", fargv);
		execv("/usr/ucb/finger", fargv);
		printf("can't exec finger\215");
		return (1);

	default:
		close(p[1]);
		close(0);
		f = fdopen(p[0], "r");
		while ((c = getc(f)) != EOF) {
			switch (c) {
			case 010:
			case 011:
			case 014:
				c += 0200;
				break;

			case '\n':
				c = 0215;
				break;
			}
			putchar(c);
		}
		wait(0);
	}
	return (0);
}
