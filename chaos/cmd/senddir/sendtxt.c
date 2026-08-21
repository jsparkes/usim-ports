/*

These routines are used to parse the sends file.  They are used by 
reply and what.

*/

#include <stdio.h>
#include "sendsys.h"

char *getlogin();
char *strcat();
char *malloc();


char *
mfrom(message)
	struct txtblk *message;
{
	char *from = malloc(MAXUNM);

	sscanf(message, "[Message from %s %s", from, malloc(BLKSIZ));
	return (from);
}

char *
lfrom()
{
	struct txtblk **sends, **get_messages();
	char *from = "                            ";

	if ((sends = get_messages()) == 0) {
		fprintf(stderr, "Can't open sends file");
		exit(1);
	}
	return (mfrom(sends[(int)sends[0] - 1]));
}

struct txtblk **
get_messages()
{
	struct txtblk **msgs;
	struct txtblk *message;
	char *mchar;
	char *sends;
	char c;
	FILE *fp, *fopen();
	int mno = 1;

	msgs = (struct txtblk **)malloc(MAXMSG + 1);
	sends = "                                         ";
	sprintf(sends, "%s/%s", SPOOL, getlogin());
	if ((fp = fopen(sends, "r")) == NULL)
		return (NULL);
	message = (struct txtblk *)malloc(sizeof(struct txtblk));
	msgs[1] = message;
	mchar = message->text;
	while ((c = getc(fp)) != EOF)
		if (c != '\004') {
			*mchar++ = c;
			if (mchar - message->text >= BLKSIZ) {
				message->successor = (struct txtblk *)malloc(sizeof(struct txtblk));
				message = message->successor;
				mchar = message->text;
			}
		} else {
			message = (struct txtblk *)malloc(sizeof(struct txtblk));
			msgs[++mno] = message;
			mchar = message->text;
		}
	msgs[0] = (struct txtblk *)mno;
	return (msgs);
}
