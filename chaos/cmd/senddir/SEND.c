/* send - Print a message on a logged in users terminal

   Usage:  send [user [message]]

   If a user is not specified, send will ask for it.  The user field
   must be specified on the command line if the standard input is to
   be redirected.

   If the message is not specified on the command line, it will
   be read from the standard input by which eversend routine is
   used.

   All output (except the message itself) goes to the standard output 
   as there is not much use in redirecting it. 

   Clifford Neuman - July 1984

   Edit History:

      Date  |  By  |  Modification
   ---------+------+-------------------------------------------
1           |      | 
2           |      | 
3           |      | 


Conditionals used by send:

   CHAOS             Include Chaos support
   INTERNET          Include Internet support 
   BSD42             Used by the Chaos library

*/

#include "sendsys.h"

char *find_usr();
char *malloc();
char *rindex();
char *strcat();
char *index();

/*

main handles command line arguments to send, as well as checking 
whether the standard input has been redirected.  If information
is not provided on the command line, it will either be prompted 
for by main (TO:) or a routive will be called to read it from the 
standard input (get_msg).  If the standard input has been redirected,
and the recipient is not included on the command line, main will signal
an error.

To be implemented:

	. Don't prompt "MSG:" if the standard input has been redirected
	. Signal error is standard input is redirected and no recipient
	. If to_tty fails, go into a subcommand mode

*/

main(argc, argv)
	char *argv[];
{
	struct txtblk *message;
	char *user = malloc(MAXUNM);
	char *host;
	int i;

	if (argc >= 2)
		user = argv[1];
	else {
		printf("To: ");
		gets(user, MAXUNM);
	}
	if (argc >= 3) {
		char *text;

		message = (struct txtblk *)malloc(sizeof(struct txtblk));
		text = message->text;
		for (i = 2; i < argc; i++) {
			strcat(text, argv[i]);
			strcat(text, " ");
		}
	} else
		message = NULL;
	send(message, user);
}
