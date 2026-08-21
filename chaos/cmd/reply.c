/* reply - Print a message on a logged in users terminal

   Usage:  reply [message]

   If the message is not specified on the command line, it will
   be read from the standard input.  

   Clifford Neuman - July 1984

   Edit History:

      Date  |  By  |  Modification
   ---------+------+-------------------------------------------
1           |      | 
2           |      | 
3           |      | 


Conditionals used by reply:

   CHAOS             Include Chaos support
   INTERNET          Include Internet support 
   BSD42             Used by the Chaos library

*/

#include "sendsys.h"

char *malloc();
char *rindex();
char *strcat();
char *index();
char *lfrom();

/*

main - handles command line arguments to reply.  If a message is
included on the command line, it is parsed, and sent to the appropriate
send routine.  If not, the message will be prompted for by the send
routine itself after it has verified that the message can be sent.

To be implemented:

	. If to_tty fails, go into a subcommand mode

*/

main(argc, argv)
	char *argv[];
{
	struct txtblk *message;
	char *user = malloc(MAXUNM);
	char *host;
	int i;

	user = lfrom();
	fprintf(stderr, "[Replying to %s]\n", user);
	if (argc >= 2) {
		char *text;

		message = (struct txtblk *)malloc(sizeof(struct txtblk));
		text = message->text;
		for (i = 1; i < argc; i++) {
			strcat(text, argv[i]);
			strcat(text, " ");
		}
	} else
		message = NULL;
	send(message, user);
}
