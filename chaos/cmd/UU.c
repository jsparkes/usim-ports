/*
 *	Uucp Chaos Server
 *	(Can also be extended to provide dumb terminal service for remote hosts)
 *
 *	Mark Plotnick, MIT, March 1984
 */
/* define BSD42 so that the include file behave properly.
  It should probably be set from the command line */
#ifndef BSD42
#define BSD42
#endif
/* improvements to make:
	read in raw packets like TELNET does
	when the net connection goes down, send the uucico process a SIGHUP
*/

#include <signal.h>
#include <sys/chaos.h>
#include <sys/ioctl.h>
#include <stdio.h>
char buf[BUFSIZ];

main(argc, argv)
	char **argv;
{
	int i, c, fmaster, fslave, n;
	int uucicopid, outputpid;
	int zero = 0;
	long waiting;
	char nmaster[50], nslave[50];

	for (c = 'p'; c <= 's'; c++) {
		for (i = 8; i < 16; i++) {	/* 0-7 have gettys on them */
			sprintf(nmaster, "/dev/pty%c%x", c, i);
			sprintf(nslave, "/dev/tty%c%x", c, i);
			if ((fmaster = open(nmaster, 2)) >= 0)
				goto win;
		}
	}
	chreject(0, "?All PTYs in use");
	return (1);
      win:
	ioctl(0, CHIOCACCEPT, 0);
	switch (uucicopid = fork()) {
	case -1:
		shut();
		break;
	case 0:
		close(0);
		open(nslave, 0);
		close(1);
		open(nslave, 1);
		close(2);
		dup(1);
		execl("/usr/lib/uucp/uucico", "uucico", 0);
		exit(1);
	}
	/*
	 * parent 
	 */
	ioctl(fmaster, TIOCREMOTE, &zero);
	switch (outputpid = fork()) {
	case -1:
		kill(uucicopid, SIGHUP);
		shut();
		break;
	case 0:		/* output to the net */
		argv[0][1] = 'o';	/* argv[0] is now "Uo" */
		for (;;) {
			if (ioctl(fmaster, FIONREAD, &waiting) == -1)
				break;
			if (waiting <= 0)
				waiting = 1;
			if (waiting > BUFSIZ)
				waiting = BUFSIZ;
			if ((n = read(fmaster, buf, (int)waiting)) <= 0)
				break;
			write(0, buf, n);
		}
		kill(uucicopid, SIGHUP);
		exit();
	default:		/* input from net */
		argv[0][1] = 'i';	/* argv[0] is now "Ui" */
		for (;;) {
			if (ioctl(0, FIONREAD, &waiting) == -1)
				break;
			if (waiting <= 0)
				waiting = 1;
			if (waiting > BUFSIZ)
				waiting = BUFSIZ;
			if ((n = read(0, buf, (int)waiting)) <= 0)
				break;
			write(fmaster, buf, n);
		}
		kill(uucicopid, SIGHUP);
		kill(outputpid, SIGHUP);
		/*
		 * for some reason, the input-from-pty-and-output-to-net
		 * process does not exit when we send it a SIGHUP.
		 * So let's try harder.
		 */
		sleep(10);
		kill(outputpid, SIGKILL);
		/*
		 * you know, we really shouldn't have to kill the
		 * process at all, because the read on the master
		 * pty should return when the slave side is closed.
		 * A bug in the kernel prevents this.
		 */
		exit(0);
	}
}

shut()
{
	exit(0);
}
