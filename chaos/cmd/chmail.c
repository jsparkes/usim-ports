#include	<stdio.h>
#include	<sys/types.h>
#include	<sysexits.h>
#include	<pwd.h>
/*
**  CHAOS MAILER
**
**	The standard input is stuck away in the outgoing
**	mail queue for delivery by the chaos mailer.
**	The three arguments are written as separate lines in the
**	spool file before the message.
**
**	Usage:
**		/usr/lib/mailers/chmail from host user
**
**	Positional Parameters:
**		from -- the person sending the mail.
**		host -- the host to send the mail to.
**		user -- the user to send the mail to.
**
**	Files:
**		/usr/spool/netmail/temp* -- the queue file before it is
**					    complete.
**		/usr/spool/netmail/mail* -- the queue file after it is
**					    complete.
**
**	Return Codes:
**		EX_OK --  message successfully mailed.
**		EX_NOHOST -- host unknown.
**		EX_OSERR -- random system error.
**		EX_SOFTWARE -- wrong number of arguments.
**
*/

#define SPOOLDIR	"/usr/spool/netmail"

main(argc, argv)
	int argc;
	char **argv;
{

	char spoolfile[50];
	char tempfile[50];
	char file[50];
	time_t now;
	register FILE *sfp;
	register int c;
	register struct tm *tm;

	if (argc != 4)
		exit(EX_SOFTWARE);
	/*
	 * In case we are invoked as root, we must change our userid
	 * to "network" so we can keep the mail queue protected without
	 * having to run the daemon as root.
	 */
	if (getuid() == 0) {
		struct passwd *getpwnam(), *pwd;

		if ((pwd = getpwnam("network")) == NULL)
			return (EX_SOFTWARE);
		setuid(pwd->pw_uid);
	}
	/*
	 * We check only for existence in the host table, not for being
	 * on any particular network, since this allows us to localize
	 * knowledge of what networks we can reach in the daemon itself
	 * where some of that knowledge must reside anyway.
	 */
	if (host_name(argv[2]) == NULL)
		return EX_NOHOST;
	sprintf(tempfile, "%s/temp.%d", SPOOLDIR, getpid());
	if ((sfp = fopen(tempfile, "w")) == NULL)
		return EX_OSERR;
	chmod(tempfile, 0400);
	fprintf(sfp, "%s\n%s\n%s\n\n", argv[1], argv[2], argv[3]);
	while ((c = getchar()) != EOF)
		putc(c, sfp);
	fflush(sfp);
	if (ferror(sfp))
		return (EX_OSERR);
	fclose(sfp);
	time(&now);
	sprintf(file, "%s/M%010D", SPOOLDIR, now);

	for (c = '0'; c <= '9'; c++) {
		sprintf(spoolfile, "%s%c", file, c);

		if (link(tempfile, spoolfile) == 0) {
			unlink(tempfile);
			return (EX_OK);
		}
	}
	unlink(tempfile);
	return EX_OSERR;
}
