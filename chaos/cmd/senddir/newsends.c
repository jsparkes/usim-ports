/*

newsends - Create new sends file

usage    - newsends user

This routine allows the isolation of the setuid functions of sends to 
something whose security can easily be confirmed.  It only function is
to create an empty send file in the directory specified by the definition 
of spool.  It is called by send if there is not a current file for the
recipient of the message.  Once the file is written, it is writeable to the
world, so this routine need not be called again.  

To protect the privacy of sends, the send file created by the program
is not readble by other than the intended reciepient unless the
reciepient does not correspond to a user of the system in which case it
is set mode 666.

*/

#include "sendsys.h"

main(argc, argv)
	char *argv[];
	int argc;
{
	struct passwd *pwd, *getpwnam();
	char of[sizeof(SPOOL) + MAXUNM + 1];
	int ofd;

	if (argc != 2 || (pwd = getpwnam(argv[1])) == NULL) {
		fprintf(stderr, "Usage: newsends user\n");
		exit(1);
	}
	if ((ofd = open(sprintf(of, "%s/%s", SPOOL, argv[1]), 1)) >= 0 || (ofd = creat(of, 0622)) >= 0)
		if (chown(of, pwd->pw_uid, pwd->pw_gid) < 0)
			chmod(of, 0666);
		else
			chmod(of, 0622);
}
