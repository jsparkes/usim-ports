/* send  - Print a message on a logged in users terminal
   reply - Print a reply on a logged in users terminal

   Usage:  send  [user [message]]
           reply [message]

   If a user is not specified, send will ask for it.  The user field
   must be specified on the command line if the standard input is to be
   redirected.  Reply on the other hand will figure out who to send to by
   looking at the lst message you received.

   If the message is not specified on the command line, it will
   be read from the standard input.  

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

struct tm *localtime();
char *find_usr();
char *malloc();
char *rindex();
char *strcat();
char *index();
char *month_sname();

send(message, user)
	struct txtblk *message;
	char *user;
{
	char *host;

	if ((host = index(user, '@')) == NULL)
		local_send(message, user);
	else {
		*host++ = '\0';
#ifdef CHAOS
		if (chaos_send(message, user, host) == NULL)
#endif
#ifdef INTERNET
			if (internet_send(message, user, host) NULL)
#endif
				printf("Invalid host specified: %s\n", host);
	}
}



/*

find_usr takes a pointer to the target username, and determines where he
is logged in.  If successful, the device name for his terminal is
returned.  If not, find_usr returns NULL.

Future mods:  . If the target is not found, see if someone is logged
                into a tty of the same name, and return that instead.

	      . Check here whether the target is refusing sends, and 
	        warn the user.  If we have privs, ask for confirmation
                before proceeding. 

	      . If the target has two jobs, ask which is to be sent to.
	        "All of them" should be a valid option.  

	      . If all but one job is refusing sends, send to it, but
	        tell the user which tty you are sending to.

Unhandled errors: 
   
              . Error opening wtmp file.

*/

char *
find_usr(user)
	char *user;
{
	struct utmp usr;
	int uf;

	uf = open("/etc/utmp", 0);
	while (read(uf, (char *)&usr, sizeof(usr)) == sizeof(usr))
		if (strncmp(usr.ut_name, user, sizeof usr.ut_name) == 0) {
			close(uf);
			return (strcat(strcat(malloc(MAXTTNM), "/dev/"), usr.ut_line));
		}
	close(uf);
	printf("%s %s %s", "User", user, "not logged in - Use mail to send to user\n");
	return (NULL);
}


/*

get_msg will read the text to be sent from the standard input and return
a "pointer" to it.  The text is stored using the txtblk structure with
blocks of BLKSIZ characters.  The blocks form a linked list, and it is a
pointer to the first block which is really returned by this procedure.

Unhandled errors::

	. Unable to read the standard input

*/

struct txtblk *
get_msg()
{
	struct txtblk *txtb = (struct txtblk *)malloc(sizeof(struct txtblk));
	char *text = txtb->text;
	char c;

	while ((text - txtb->text) < BLKSIZ && (c = getchar()) != EOF) {
		*(text++) = c;
		if (c == '\012')
			*(text++) = '\015';
	}
	if (text - txtb->text < BLKSIZ)
		return (txtb);
	txtb->successor = get_msg();
	return (txtb);
}

/*

to_tty takes a pointer to the message to be sent, and the 
device name of the destination terminal.  It writes the
message to the destination and returns SUCCESS if successful.  
If it is unable to write the messge (e.g. the user is refusing
sends) it will print a message on the controlling terminal and
signal an ERROR.

*/

to_tty(message, line)
	struct txtblk *message;
	char *line;
{
	FILE *rterm, *fopen();
	char *utty, *ctime();
	long time(), now;
	struct tm *tm;

	utty = rindex(ttyname(2), '/') + 1;
	time(&now);
	tm = localtime(&now);
	rterm = fopen(line, "a");
	if (rterm == NULL) {
		printf("%s", "User is refusing sends - Use mail to send to user\n");
		return (ERROR);
	}
	fprintf(rterm, "\n\007[Message from %s (%s) %d-%s-%d %02d:%02d:%02d]\n\r", getlogin(), utty, tm->tm_mday, month_sname(tm->tm_mon + 1), tm->tm_year, tm->tm_hour, tm->tm_min, tm->tm_sec);
	while (message != NULL) {
		fprintf(rterm, "%s", message->text);
		message = message->successor;
	}
	fprintf(rterm, "\n");
	fclose(rterm);
	return (SUCCESS);
}


to_file(message, user)
	struct txtblk *message;
	char *user;
{
	FILE *txtfl, *fopen();
	char *utty, *ctime();
	long time(), now;
	struct tm *tm;

	utty = rindex(ttyname(2), '/') + 1;
	time(&now);
	tm = localtime(&now);
	system(strcat("newsends \000123456789", user));
	txtfl = fopen(strcat("/usr/spool/sends/\00012345678", user), "a");
	if (txtfl == NULL) {
		printf("%s", "Unable to write sends file\n");
		return (ERROR);
	}
	fprintf(txtfl, "[Message from %s (%s) %d-%s-%d %02d:%02d:%02d]\n", getlogin(), utty, tm->tm_mday, month_sname(tm->tm_mon + 1), tm->tm_year, tm->tm_hour, tm->tm_min, tm->tm_sec);
	while (message != NULL) {
		fprintf(txtfl, "%s", message->text);
		message = message->successor;
	}
	fprintf(txtfl, "\n\004");
	fclose(txtfl);
	return (SUCCESS);
}

/*

local_send - This procedure handles sending 
messages to local users.

form should be [Message from bcn (tty02) 23-Jul-84  6:31PM]
*/

struct txtblk *
local_send(message, user)
	struct txtblk *message;
	char *user;
{
	char *line;

	if ((line = find_usr(user)) != NULL) {
		if (message == NULL) {
			if (isatty(0))
				fprintf(stderr, "Msg:\n");
			message = get_msg();
		}
		if (to_tty(message, line))
			return (message);
		else
			to_file(message, user);
		exit(0);
	} else
		return (NULL);
}

/*

Chaosnet support - These routines handle the sending of 
messages to ChaosNet sites.

*/

#ifdef CHAOS

struct txtblk *
chaos_send(message, user, host)
	struct txtblk *message;
	char *user;
	char *host;
{
	int addr;
	int fd;
	long now;
	FILE *out;
	extern char _sobuf[];
	struct tm *tm;
	struct chstatus chst;
	char rfcbuf[CHMAXRFC + 1];
	char *ucase();

	if ((addr = chaos_addr(host, 0)) <= 0)
		return (NULL);
	if ((fd = chopen(addr, CHAOS_SEND, 1, 1, ucase(user), 0, 0)) < 0) {
		printf("send: Can't open connection\n");
		return (NULL);
	}
	ioctl(fd, CHIOCSWAIT, CSRFCSENT);
	ioctl(fd, CHIOCGSTAT, &chst);
	if (chst.st_state != CSOPEN) {
		if (chst.st_ptype == CLSOP) {
			ioctl(fd, CHIOCPREAD, rfcbuf);
			rfcbuf[chst.st_plength] = '\0';
			printf("send: %s\n", rfcbuf);
		} else
			printf("send: connection refused\n");
		exit(1);
	}
	time(&now);
	tm = localtime(&now);
	out = fdopen(fd, "w");
	if (out == NULL) {
		perror("fdopen");
		exit(1);
	}

	setbuf(out, _sobuf);
	if (message == NULL) {
		if (isatty(0))
			fprintf(stderr, "Msg:\n");
		message = get_msg();
	}
	fprintf(out, "%s@%s %d-%s-%d %02d:%02d:%02d%c", getlogin(), host_me(), tm->tm_mday, month_sname(tm->tm_mon + 1), tm->tm_year, tm->tm_hour, tm->tm_min, tm->tm_sec, CHNL);
	while (message != NULL) {
		fprintf(out, "%s", message->text);
		message = message->successor;
	}
	fprintf(out, "\n");
	fclose(out);
	exit(0);
}

#endif

/*

Internet support - These routines handle the sending of 
messages to InterNet sites.

*/

#ifdef INTERNET

#endif

/*

All things below this line should really be in some kind
of library.  In fact, they may be and I just haven't found
it.

*/

char *
ucase(s)
	register char *s;
{
	static char name[20];
	register char *cp = name;

	while (*s)
		if (islower(*s))
			*cp++ = toupper(*s++);
		else
			*cp++ = *s++;
	*cp++ = '\0';
	return name;
}

char *
month_sname(n)
	int n;
{
	static char *name[] = { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
		"Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
	};
	return ((n < 1 || n > 12) ? NULL : name[n - 1]);
}
