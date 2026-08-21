#include <stdio.h>
#include <utmp.h>
#include <ctype.h>
#include <hosttab.h>
#include <sys/time.h>
#include <sys/chaos.h>
#include <pwd.h>

#define BLKSIZ 512		/* Size of blocks to be read from stdin   */
#define MAXTTNM 20		/* Max length for tty name including /dev */
#define MAXUNM 100		/* Max length for username (inc null)     */
#define MAXMSG 500		/* Max # of message in ones sends file    */
#define SUCCESS 0		/* Hide the control coupling              */
#define ERROR -1		/* Used to signal unsuccessful completion */
#define SPOOL "/usr/spool/sends"	/* Location of sends files       */

struct txtblk *get_msg();
struct txtblk **get_messages();
struct txtblk *chaos_send();
struct txtblk *local_send();
char *find_usr();
char *month_sname();

struct txtblk {			/* Used to store message */
	char text[BLKSIZ + 2];	/* Text itself           */
	struct txtblk *successor;
};				/* Next block of text    */
