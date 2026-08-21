#include <stdio.h>
#include <sys/ioctl.h>
#include <sys/file.h>
#include <signal.h>
/*
 *	chtty server
 */

#include <sys/chaos.h>

#define SCPYN(a, b)	strncpy(a, b, sizeof(a))
#define SCMPN(a, b)	strncmp(a, b, sizeof(a))
#define BANNER		"\r\n\r\n4.3 BSD UNIX (%s)\r\n\r\r\n\r%s"

char chtty[] = "/dev/tty\0\0\0";
char term[50] = "TERM=";
char *envp[] = { term, 0 };

struct chpacket packet;

char remote_hostname[40], local_hostname[40];

int onchild();

#define NJOBS 128
struct job {
	int pid;
} jobs[NJOBS];

main(argc, argv)
	char *argv[];
{

#define err stderr

	int chfd = -1;
	int f;

	f = open("/dev/tty", O_RDWR);
	ioctl(f, TIOCNOTTY);
	close(f);
	close(0);
	close(1);
	close(2);
	freopen("/usr/tmp/chttyserv", "w", stderr);
	setbuf(stderr, NULL);
	signal(SIGCHLD, onchild);
	signal(SIGHUP, SIG_IGN);

	for (;;) {
		/*
		 * listen for contacts 
		 */
		int signalmask;

		close(chfd);
		chfd = chlisten(argv[1], 2, 1, 0);
/*	  signalmask = sigblock(sigmask(SIGCHLD));*/
		if (chfd < 0)
			perror("chopen"), fflush(err);
		else {
			if (ioctl(chfd, CHIOCSWAIT, CSLISTEN) == 0)
				connect(chfd);
		}
		fflush(stderr);
/*	  sigsetmask(signalmask);*/
	}
}

connect(fd)
{
	struct chstatus chstatus;
	int pid;
	register char *p;
	int unit;
	int f;


	ioctl(fd, CHIOCGSTAT, &chstatus);
	if (chstatus.st_state != CSRFCRCVD) {
		fprintf(err, "? conn state %d\n", chstatus.st_state);
		return (-1);
	}
	gethostname(local_hostname, sizeof(local_hostname));
	p = chaos_name(chstatus.st_fhost);
	if (p)
		SCPYN(remote_hostname, p);
	else
		sprintf(remote_hostname, "chaos %o", chstatus.st_fhost);
	fprintf(err, remote_hostname);
	if ((unit = open("/dev/tty", O_RDWR | O_NDELAY)) != -1) {
		ioctl(unit, TIOCNOTTY);
		close(unit);
	}
	unit = -1;
	ioctl(fd, CHIOCSMODE, CHRECORD);
	read(fd, &packet, sizeof packet);
	if (packet.cp_op != RFCOP) {
		chreject(fd, "Couldnt read RFC packet");
		fflush(err);
		return -1;
	}
	sscanf(packet.cp_data, "%*s %s", &term[strlen("TERM=")]);
	if (ioctl(fd, CHIOCGTTY, &unit)) {
		chreject(fd, "All chttys in use");
		perror("chiocgtty");
		fflush(err);
		return (-1);
	} else
		fprintf(err, " unit %x\n", unit);
/*	chtty[strlen(chtty) - 1] = unit + '0';*/
	sprintf(&chtty[strlen("/dev/tty")], "%c%c", (unit / 10) + 'A', (unit % 10) + '0');
	fflush(err);

	pid = fork();
	if (pid) {
		/*
		 * associate pid with chtty 
		 */
		jobs[unit].pid = pid;
		close(fd);
		return 0;
	}

	/*
	 * child 
	 */

	unit = open(chtty, O_RDWR);
/*	setpgrp(0, getpid());*/
	if (unit < 0) {
		chreject(fd, "Cant open chtty");
		exit(1);
	}

	close(fd);
	getty(chtty, unit);
}

/* stolen from Web Dove supdup server */
getty(tty, ftty)
{
	struct sgttyb b;
	struct tchars tc;
	short mw;
	int f;
	extern errno;

#ifdef notdef
	if ((f = open("/dev/tty", O_RDWR)) >= 0) {	/* flush my cntrl tty */
		ioctl(f, TIOCNOTTY, 0);
		close(f);
	};
	if ((f = open(tty, O_RDWR | O_NDELAY)) < 0)
		exit(1);
	close(ftty);
	signal(SIGHUP, SIG_IGN);
	vhangup();
	close(f);
/*    sleep(1);*/
	signal(SIGHUP, SIG_DFL);
	if ((f = open("/dev/tty", O_RDWR)) >= 0) {	/* flush my cntrl tty */
		ioctl(f, TIOCNOTTY, 0);
		close(f);
	};
	if ((ftty = open(tty, O_RDWR | O_NDELAY)) < 0)
		exit(1);
#endif
	errno = 0;
	chown(tty, 0, 0);
	chmod(tty, 0622);	/* make the tty reasonable */
	ioctl(ftty, TIOCNXCL, 0);	/* turn off excl use */
	ioctl(ftty, TIOCSETD, NTTYDISC);	/* new line discipline */
	b.sg_ispeed = B9600;	/* input speed */
	b.sg_ospeed = B9600;	/* output speed */
	b.sg_erase = 0177;	/* erase character */
	b.sg_kill = 'U' - 0100;	/* kill character */
	b.sg_flags = ECHO | CRMOD | ANYP;	/* mode flags */
	ioctl(ftty, TIOCSETP, &b);
	tc.t_intrc = 'C' - 0100;	/* interrupt */
	tc.t_quitc = '\\' - 0100;	/* quit */
	tc.t_startc = 'Q' - 0100;	/* start output */
	tc.t_stopc = 'S' - 0100;	/* stop output */
	tc.t_eofc = 'D' - 0100;	/* end-of-file */
	tc.t_brkc = -1;		/* input delimiter (like nl) */
	ioctl(ftty, TIOCSETC, &tc);
	mw = LCRTERA | LCRTBS | LCRTKIL | LCTLECH | LPENDIN | LDECCTQ;
	ioctl(ftty, TIOCLSET, &mw);
	dup2(ftty, 0);
	close(ftty);
	dup2(0, 1);
	dup2(0, 2);		/* Connect to tty */
	printf(BANNER, local_hostname, "");
	execle("/bin/login", "login", "-p", "-h", remote_hostname, 0, envp);
	perror("failed to exec login!\n\r");
	fflush(err);
	exit(1);
}

#include <sys/wait.h>
onchild(sig)
{
	int child;
	int i;

	fprintf(err, "signal %d\n", sig);
      loop:
	child = wait3(0, WNOHANG, 0);
	fprintf(err, "wait returned %D\n", child);
	fflush(err);
	if (child <= 0)
		return;
	for (i = 0; i < NJOBS; i++)
		if (jobs[i].pid == child) {
			char *line = "ttyxx";
			char *dev = "/dev/ttyxx";
			int f;

			sprintf(line, "tty%c%c", (i / 10) + 'A', (i % 10) + '0');
			rmut(line);
			fprintf(err, "rmut %s\n", line);

/* driver must support O_NDELAY open for this to work */
			sprintf(dev, "/dev/tty%c%c", (i / 10) + 'A', (i % 10) + '0');

			if ((f = open("/dev/tty", O_RDWR | O_NDELAY)) >= 0) {
				ioctl(f, TIOCNOTTY);
				close(f);
			}

			if ((f = open(dev, O_RDWR | O_NDELAY)) >= 0) {
				int f1;

				vhangup();
				if ((f1 = open("/dev/tty", O_RDWR | O_NDELAY)) >= 0) {
					ioctl(f1, TIOCNOTTY);
					close(f1);
				}
				sleep(1);
				close(f);
			}
/* */
		}
	goto loop;
}

ontimer()
{
}


#include <utmp.h>
rmut(line)
	char *line;
{
	struct utmp wtmp;
	register int f;
	int found = 0;

	f = open("/etc/utmp", O_RDWR);
	if (f >= 0) {
		while (read(f, (char *)&wtmp, sizeof(wtmp)) == sizeof(wtmp)) {
			if (SCMPN(wtmp.ut_line, line) || wtmp.ut_name[0] == 0)
				continue;
			lseek(f, -(long)sizeof(wtmp), 1);
			SCPYN(wtmp.ut_name, "");
			SCPYN(wtmp.ut_host, "");
			time(&wtmp.ut_time);
			write(f, (char *)&wtmp, sizeof(wtmp));
			found++;
		}
		close(f);
	}
	if (found) {
		f = open("/usr/adm/wtmp", O_WRONLY | O_APPEND);
		if (f >= 0) {
			SCPYN(wtmp.ut_line, line);
			SCPYN(wtmp.ut_name, "");
			SCPYN(wtmp.ut_host, "");
			time(&wtmp.ut_time);
			write(f, (char *)&wtmp, sizeof(wtmp));
			close(f);
		}
	}
}
