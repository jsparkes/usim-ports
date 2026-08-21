/*
 * Chaosnet RFC handler for contact name SPICE
 * Arguments are:
 *	Input filename (if not the network channel)
 *	Output (ascii) filename (if not the network channel)
 *	Binary output filename or contact name (if no slashes)
 */
#include <stdio.h>
#include <signal.h>
#include <sys/chaos.h>

char *fargv[50], *sprintf();

main(argc, argv)
	register int argc;
	register char *argv[];
{

	register char **t;
	int spice, innet = 0, outnet = 0;
	int ifd, ofd;
	int ip[2], op[2];
	char contact[CHMAXRFC];
	struct chstatus cst;
	char *index();

	ioctl(0, CHIOCGSTAT, &cst);
	t = &fargv[0];
	*t++ = "spice";
	if (argc >= 4)
		if (index(argv[3], '/') == 0) {
/*
			(void)sprintf(contact, "%s/%d/%s", CHRFCDEV,
				      cst.st_fhost, argv[3]);
				      *t++ = contact;
*/
		} else
			*t++ = argv[3];
	*t = 0;
	if (argc < 2 || argv[1][0] == '-') {
		innet++;
		if (pipe(ip) < 0) {
			chreject(0, "Can't create input pipe");
			return (1);
		}
		ifd = ip[0];
	} else if ((ifd = open(argv[1], 0)) < 0) {
		chreject(0, "Can't open input file");
		return (1);
	}
	if (argc < 3 || argv[2][0] == '-') {
		outnet++;
		if (pipe(op) < 0) {
			chreject(0, "Can't create output pipe");
			return (1);
		}
		ofd = op[1];
	} else if ((ofd = creat(argv[2], 0666)) < 0) {
		chreject(0, "Can't create output file");
		return (1);
	}
	ioctl(0, CHIOCACCEPT, 0);
	switch (spice = fork()) {
	case -1:
		chreject(0, "Can't create fork for name server");
		return (1);

	case 0:
		close(0);
		dup(ifd);
		close(ifd);
		close(1);
		close(2);
		dup(ofd);
		dup(ofd);
		close(ofd);
		if (innet)
			close(ip[1]);
		if (outnet)
			close(op[0]);
		execv("/usr/local/spice", fargv);
		return (1);
	default:
		close(ifd);
		close(ofd);
		if (innet && outnet)
			switch (fork()) {
			case -1:
				chreject(0, "Can't create process");
				kill(spice, SIGKILL);
				return (1);
			case 0:
				close(ip[1]);
				tonet(op[0]);
				return (0);
			default:
				close(op[0]);
				fromnet(ip[1]);
				return (0);
		} else if (innet)
			fromnet(ip[1]);
		else if (outnet)
			tonet(op[0]);
		else
			wait(0);
	}
	return 0;
}

tonet(fd)
	register int fd;
{
	register FILE *f;
	register int c;

	f = fdopen(fd, "r");
	while ((c = getc(f)) != EOF) {
		switch (c) {
		case 010:
		case 011:
		case 014:
			c += 0200;
			break;
		case '\n':
			c = CHNL;
			break;
		}
		if (putchar(c) == EOF)
			return;
	}
}

fromnet(fd)
	register int fd;
{
	register FILE *f;
	register int c;

	f = fdopen(fd, "w");
	while ((c = getchar()) != EOF) {
		switch (c) {
		case CHNL:
			c = '\n';
			break;
		case 0211:
		case 0210:
		case 0214:
			c &= 0177;
			break;
		}
		if (putc(c, f) == EOF)
			return;
	}
}
