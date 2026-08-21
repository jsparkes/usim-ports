/*
 * user interface to Chaos FILE protocol (Lisp Machine file server).
 * Author: T. J. Teixeira <TJT@MIT-XX>
 */

#include <stdio.h>
#include <ctype.h>
#include <sgtty.h>
#include <signal.h>
#include <sys/types.h>
#include <sys/timeb.h>
#include <sys/ioctl.h>
#include <hosttab.h>
#include <sys/chaos.h>

extern int errno;
struct host_entry *host;
char user[80];
char pass[80];
char localdir[1024];
char fgndir[1024];
char localfn[1024];
char foreignfn[1024];

#define DBSIZE 	1024*8
char databuf[DBSIZE + CHMAXDATA + 1];
char ifh[6];
char ofh[6];

struct conn {			/* packet-level connection structure */
	char file;		/* open file */
	char flag;		/* 0 => not open */
	struct chpacket rpkt;	/* last packet read */
	int rlen;		/* length rpkt (including op) */
	int rcnt;		/* count of unread chars in rpkt */
	char *rptr;		/* pointer to next char in rpkt */
	struct chpacket wpkt;	/* packet to be written */
	int wcnt;		/* count of chars left in wpkt */
	char *wptr;		/* pointer to next char in wpkt */
};

typedef struct conn *CONN;

int debug;
CONN control, data;

/*
 * null handler for timeout (to wake out of CHIOCWAIT)
 */
timeout()
{
}

/*
 * open a Chaos connection to the specified host
 * with the specified RFC arguments
 */
CONN
connopen(host, rfcargs)
	char *host;
	char *rfcargs;
{
	register CONN c;
	struct chstatus chstat;
	char junkbuf[CHMAXPKT];
	char *malloc();

	if ((c = (CONN) malloc(sizeof *c)) == (CONN) NULL)
		return ((CONN) NULL);
	if ((c->file = chopen(chaos_addr(host), rfcargs, 2, 1, 0, 0, 0))<0) {
		printf("ncp error -- cannot open %s\n", rfcargs);
	      nogood:
		free((char *)c);
		return ((CONN) NULL);
	}
	signal(SIGALRM, timeout);
	alarm(15);
	ioctl(c->file, CHIOCSWAIT, CSRFCSENT);

	alarm(0);
	ioctl(c->file, CHIOCGSTAT, &chstat);

	if (chstat.st_state == CSRFCSENT) {
	      lose:close(c->file);

		goto nogood;
	}
	if (chstat.st_state != CSOPEN) {
		if (chstat.st_state == CSCLOSED) {
			printf("Connection refused\n");
			if (chstat.st_plength && (chstat.st_ptype == ANSOP || chstat.st_ptype == CLSOP)) {
				ioctl(c->file, CHIOCPREAD, junkbuf);

				printf("%s\n", junkbuf);
			}
			goto lose;
		}
		goto lose;
	}
	c->flag = 1;
	c->wpkt.cp_op = DATOP;
	c->wcnt = sizeof(c->wpkt.cp_data);
	c->wptr = c->wpkt.cp_data;
	ioctl(c->file, CHIOCSMODE, CHRECORD);

	return (c);
}

/*
 * listen for an RFC with the specified contact name
 */
CONN
connlisten(cname)
	char *cname;
{
	register CONN c;
	char *malloc();

	if ((c = (CONN) malloc(sizeof *c)) == (CONN) NULL)
		return ((CONN) NULL);
	if ((c->file = chlisten(cname, 2, 1, 0))<0) {
		free((char *)c);
		return ((CONN) NULL);
	}
	c->flag = 1;
	c->wpkt.cp_op = DATOP;
	c->wcnt = sizeof(c->wpkt.cp_data);
	c->wptr = c->wpkt.cp_data;
	ioctl(c->file, CHIOCSMODE, CHRECORD);

	return (c);
}

/*
 * accept a connection
 */
chaccept(c)
	register CONN c;
{
	ioctl(c->file, CHIOCSWAIT, CSLISTEN);
	return (ioctl(c->file, CHIOCACCEPT, 0));
}

/*
 * close a connection
 */
chclose(c)
	register CONN c;
{
	chreject(c->file, "");
	close(c->file);

	c->flag = 0;
	free((char *)c);
}

/*
 * read a packet into a connection
 * (throws out current packet)
 * returns the length of the packet
 * including the packet opcode
 */
chrpkt(c)
	register CONN c;
{
	register int n;
	struct chstatus chst;

      loop:
	n = read(c->file, &c->rpkt, sizeof(c->rpkt));

	c->rlen = n;
	c->rcnt = n - 1;
	c->rptr = c->rpkt.cp_data;
	if (debug) {
		if (n > 0)
			if (c == control) {
				printf("Response (%d): '", c->rpkt.cp_op);
				c->rptr[n - 1] = '\0';
				putlms(c->rptr);
				printf("'\n");
			} else
				printf("Read data (%d) %d data bytes\n", c->rpkt.cp_op, n - 1);
		else {
			ioctl(c->file, CHIOCGSTAT, &chst);

			printf("%s (errno:%d) ", c == control ? "Control" : "Data", errno);
		}
		printf("Read return %d\n", n);
		fflush(stdout);
	}
	if (c->rpkt.cp_op == RFCOP)
		goto loop;
	return (n);
}

/*
 * write a packet to a connection using the specified opcode
 */
chwpkt(op, c)
	int op;
	register CONN c;
{
	register int n;

	n = c->wptr - c->wpkt.cp_data + 1;
	c->wpkt.cp_op = op;
	n = write(c->file, &c->wpkt, n);

	if (debug) {
		if (c == control) {
			c->wptr[0] = '\0';
			printf("Command (%d & 0377): ", c->wpkt.cp_op);
			putlms(c->wpkt.cp_data);
		} else
			printf("Data write (%d) of %d data bytes.\n", c->wpkt.cp_op, c->wptr - c->wpkt.cp_data);
		printf("Returned %d\n", n);
		fflush(stdout);
	}
	c->wcnt = sizeof(c->wpkt.cp_data);
	c->wptr = c->wpkt.cp_data;
	return (n);
}

/*
 * return the opcode of the current read packet
 */
#define chrop(c) (((c)->rpkt.cp_op)&0377)

/*
 * return the length of the current read packet
 */
#define chrlen(c) ((c)->rlen)

/*
 * set the opcode for write packets
 */
#define chwop(op,c) ((c)->wpkt.cp_op = (op))

/*
 * read the next data byte from a connection
 * returns EOF on either a UNIX EOF on the file (i.e. error)
 * or a packet type that is neither a data op or UNCOP
 */
chread(c)
	register CONN c;
{
	if (--c->rcnt >= 0)
		return (*c->rptr++ & 0377);
	if (chrpkt(c) <= 0 || (chrop(c) != UNCOP && (chrop(c) & 0200) == 0))
		return (EOF);
	c->rcnt--;
	return (*c->rptr++ & 0377);
}

/*
 * write a character to a connection
 */
chwrite(d, c)
	int d;
	register CONN c;
{
	if (--c->wcnt >= 0)
		*c->wptr++ = d;
	else {
		if (chwpkt(c->wpkt.cp_op, c) <= 0)
			return (EOF);
		c->wcnt--;
		*c->wptr++ = d;
	}
	return (d);
}

/*
 * write a string to a connection
 */
chswrite(s, c)
	register char *s;
	register CONN c;
{
	while (*s != '\0')
		chwrite(*s++, c);
}

char *prompt;

long to_raw();
long from_raw();
long to_ascii();
long from_ascii();
long to_image();
long from_image();
long to_binary();
long from_binary();

char *mode = "CHARACTER";	/* file transfer mode */
int bytesize;
long (*to_mode)() = to_ascii;
long (*from_mode)() = from_ascii;


struct timeb before, after;

int loggedin = 0;

int connect();
int login();
int get();
int directory();
int send();
int quit();
int bye();
int help();
int delete();
int rename();
int probe();
int ascii();
int bytes();
int binary();
int raw();
int image();
int status();
int verbose();
int brief();
int ldir();
int fdir();

#define HELPINDENT (sizeof("disconnect"))

struct cmd {
	char *name;
	int (*handler)();
} cmdtab[] = {
	"connect", connect,
	"open", connect,
	"login", login,
	"ldir", ldir,
	"fdir", fdir,
	"get", get,
	"send", send,
	"directory", directory,
	"probe", probe,
	"delete", delete,
	"rename", rename,
	"ascii", ascii,
	"raw", raw,
	"image", image,
	"bytes", bytes,
	"binary", binary,
	"status", status,
	"verbose", verbose,
	"brief", brief,
	"quit", quit,
	"exit", quit,
	"disconnect", bye,
	"bye", bye,
	"help", help,
	"?", help,
	0
};

/*
 * N.B.: makearg start filling at margv[1]
 * so a default command can be stashed in margv[0]
 * by the command loop.
 */
int margc;
char *margv[20];

char line[132];

struct cmd *
getcmd(name)
	register char *name;
{
	register char *p, *q;
	register struct cmd *c, *found;
	register int nmatches, longest;

	longest = 0;
	nmatches = 0;
	found = 0;
	for (c = cmdtab; p = c->name; c++) {
		for (q = name; *q == *p++; q++)
			if (*q == 0)	/* exact match? */
				return (c);
		if (!*q) {	/* the name was a prefix */
			if (q - name > longest) {
				longest = q - name;
				nmatches = 1;
				found = c;
			} else if (q - name == longest)
				nmatches++;
		}
	}
	if (nmatches > 1)
		return ((struct cmd *)-1);
	return (found);
}

main(argc, argv)
	char *argv[];
{
	register struct cmd *c;
	char outbuf[BUFSIZ];

	setbuf(stdout, outbuf);
	prompt = argv[0];
	if (argc > 1 && strcmp("-D", argv[1]) == 0) {
		debug++;
		argc--;
		argv++;
	}
	if (argc >= 2)
		connect(argc, argv);
	while (argc < 2 || control != NULL) {
		printf("%s>", prompt);
		fflush(stdout);
		if (gets(line) == NULL)
			break;
		if (line[0] == 0)
			continue;
		makeargv();
		c = getcmd(margv[1]);
		if (c == (struct cmd *)-1)
			printf("ambiguous command\n");
		else if (c == (struct cmd *)0) {
			if (control == NULL) {
				margv[0] = "connect";
				connect(++margc, &margv[0]);
			} else if (!loggedin) {
				margv[0] = "login";
				login(++margc, &margv[0]);
			} else
				printf("unknown command\n");
		} else
			(*c->handler) (margc, &margv[1]);
	}
	putchar('\n');
	return (0);
}

connect(argc, argv)
	char *argv[];
{
	char name[50];

	if (argc <= 0) {	/* give help */
		printf("Connect to the given site.\n");
		return (0);
	}
	if (control != NULL) {
		printf("already connected to %s!\n", host->host_name);
		return (1);
	}
	if (argc < 2) {
		printf("Host: ");
		fflush(stdout);
		gets(name);
	} else
		strcpy(name, argv[1]);
	if ((host = host_info(name)) == 0) {
		printf("unknown host %s\n", name);
		return (1);
	}
	if ((control = connopen(name, "FILE")) == NULL) {
		printf("host %s not responding\n", host->host_name);
		return (1);
	}
	return (0);
}

/*
 * wait for a response
 */
char *
ctl_receive()
{
	register char *p;
	register int len, i;
	char response[CHMAXDATA + 1];
	char *index();

	if ((len = chrpkt(control)) <= 0) {
		printf("Connection lost(?)\n");
		bye();
		return (0);
	}
	/*
	 * is > 0 rather than >= 0 to compensate for opcode 
	 */
	for (p = response, i = len; --i > 0; *p++ = chread(control));
	*p = 0;
	if (chrop(control) == CLSOP || chrop(control) == LOSOP) {
		printf("Other end closed connection: ");
		putlms(response);
		bye();
		return (0);
	}
	if (chrop(control) == 0203) {	/* Notification packet! */
		printf("Notification: ");
		putlms(response);
		return ctl_receive();
	}
	/*
	 * skip transaction id 
	 */
	if ((p = index(response, ' ')) == 0) {
		printf("Bad response: %o %d ", chrop(control), len);
		putlms(response);
		return (0);
	}
	/*
	 * skip file handle 
	 */
	if ((p = index(++p, ' ')) == 0) {
		printf("Bad response: %o %d ", chrop(control), len);
		putlms(response);
		return (0);
	}
	if (strncmp("ERROR", ++p, 5) == 0) {
		putlms(response);
		return (0);
	}
	return (p);
}

/*
 * format a control packet and send it
 * VARARGS2
 */
ctl_send(fh, format, a, b, c, d)
	char *fh;		/* file handle */
	char *format;		/* of the control packet (after TID & FH) */
{
	char temp[CHMAXDATA + 1];

	if (fh && fh[0])
		sprintf(temp, "TIDNO %s ", fh);
	else
		sprintf(temp, "TIDNO  ");
	sprintf(&temp[strlen(temp)], format, a, b, c, d);
	chswrite(temp, control);
	chwpkt(DATOP, control);
}

login(argc, argv)
	char *argv[];
{
	struct sgttyb old, new;

	if (argc <= 0) {	/* give help */
		printf("login <user>\n");
		return (0);
	}
	if (control == NULL) {
		printf("No connection\n");
		return (1);
	}
	if (argc < 2) {
		printf("Login ID: ");
		fflush(stdout);
		if (gets(user) == NULL)
			return (-1);
	} else
		strcpy(user, argv[1]);
	printf("Password: ");
	fflush(stdout);
	gtty(fileno(stdin), &old);
	new = old;
	new.sg_flags &= ~ECHO;
	stty(fileno(stdin), &new);
	gets(pass);
	putchar('\n');
	stty(fileno(stdin), &old);
	ctl_send("", "LOGIN %s %s ", user, pass);
	if (!ctl_receive())
		return (1);
	if (!opendata())
		return (1);
	loggedin = 1;
	return (0);
}

/*
 * set foreign directory (name prefix)
 */
fdir(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {	/* give help */
		printf("Change foreign directory.\n");
		if (argc < 0) {
			printf("Actually, the argument is used as a prefix\n");
			printf("to all foreign file names\n");
		}
		return (0);
	}
	if (argc < 2) {
		printf("Change to foreign directory: ");
		fflush(stdout);
		gets(fgndir);
	} else {
		register char *p, *q;

		p = fgndir;
		while (--argc > 0) {
			if (p != fgndir)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	return (0);
}

/*
 * set local directory (name prefix)
 */
ldir(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {	/* give help */
		printf("Change local directory.\n");
		if (argc < 0) {
			printf("Actually, the argument is used as a prefix\n");
			printf("to all local file names\n");
		}
		return (0);
	}
	if (argc < 2) {
		printf("Change to directory: ");
		fflush(stdout);
		gets(localdir);
	} else {
		register char *p, *q;

		p = localdir;
		while (--argc > 0) {
			if (p != localdir)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	return (0);
}

/*
 * copy from foreign file to local file
 */
get(argc, argv)
	int argc;
	char *argv[];
{
	register FILE *t;
	register int c;
	register long bytes;
	register char *response;
	char fn[80], ln[80];

	if (argc <= 0) {	/* give help */
		printf("Transfer file from the foreign host.\n");
		if (argc < 0) {
			printf("The local file name defaults to the foreign\n");
			printf("file name if the local name is empty.\n");
		}
		return (0);
	}
	if (control == NULL) {
		printf("No connection!\n");
		return (1);
	}
	while (!loggedin)
		if (call(login, "login", 0) < 0)
			return (1);
	if (argc < 2) {
		printf("Get foreign file: ");
		fflush(stdout);
		gets(foreignfn);
	} else {
		register char *p, *q;

		p = foreignfn;
		while (--argc > 0) {
			if (p != foreignfn)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	printf("To local file:");
	fflush(stdout);
	gets(localfn);
	if (localfn[0] == 0)
		strcpy(localfn, foreignfn);
	strcpy(fn, fgndir);
	strcat(fn, foreignfn);
	strcpy(ln, localdir);
	strcat(ln, localfn);
	if ((t = fopen(ln, "w")) == NULL) {
		printf("can't open %s for output\n", ln);
		return (0);
	}
	ctl_send(ifh, "OPEN %s\215%s\215", mode, fn);
	if (!(response = ctl_receive()))
		return (0);
	putlms(response);
	fflush(stdout);
	ftime(&before);
	chrpkt(data);
	if (chrop(data) != 0202)
		bytes = (*from_mode) (t);
	else
		async();
	ctl_send(ifh, "CLOSE");
	c = (int)ctl_receive();
	while (1) {
		if (chrop(data) == EOFOP) {
			chrpkt(data);
			continue;
		} else if (chrop(data) == 0201) {
			if (chrlen(data) != 1) {
				printf("bad packet op = 0201 len = %d\n", chrlen(data));
			}
		}
		break;
	}
	ftime(&after);
	fclose(t);
	comptime(bytes);
	return (c);
}

/*
 * copy from local file to foreign file;
 */
send(argc, argv)
	int argc;
	char *argv[];
{
	register FILE *f;
	register int c;
	register long bytes;
	char fn[80], ln[80];

	if (argc <= 0) {	/* give help */
		printf("Transfer a file to the foreign host.\n");
		if (argc < 0) {
			printf("The foreign file name defaults to the local file name\n");
			printf("if the foreign file name is empty.\n");
		}
		return (0);
	}
	if (control == NULL) {
		printf("No connection!\n");
		return (1);
	}
	while (!loggedin)
		if (call(login, "login", 0) < 0)
			return (1);
	if (argc < 2) {
		printf("Send local file: ");
		fflush(stdout);
		gets(localfn);
	} else {
		register char *p, *q;

		p = localfn;
		while (--argc > 0) {
			if (p != localfn)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	printf("To foreign file: ");
	fflush(stdout);
	gets(foreignfn);
	if (foreignfn[0] == 0)
		strcpy(foreignfn, localfn);
	strcpy(fn, fgndir);
	strcat(fn, foreignfn);
	strcpy(ln, localdir);
	strcat(ln, localfn);
	if ((f = fopen(ln, "r")) == NULL) {
		printf("can't open %s for input\n", ln);
		return (0);
	}
	ctl_send(ofh, "OPEN %s\215%s\215", mode, fn);
	if (!ctl_receive()) {
		return (0);
	}
	ftime(&before);
	bytes = (*to_mode) (f);
	chwpkt(data->wpkt.cp_op, data);
	ioctl(data->file, CHIOCOWAIT, 1);	/* sends an EOF and waits */

	chwpkt(0201, data);	/* synchronous mark */
	ctl_send(ofh, "CLOSE");
	c = (int)ctl_receive();
	chwop(0200, data);	/* revert to normal data packets */
	ftime(&after);
	fclose(f);
	comptime(bytes);
	return (c);
}

/*
 * list foreign directory
 */
directory(argc, argv)
	int argc;
	char *argv[];
{
	register int c;
	char fn[80];

	if (argc <= 0) {	/* give help */
		printf("List foreign directory.\n");
		return (0);
	}
	if (control == NULL) {
		printf("No connection!\n");
		return (1);
	}
	while (!loggedin)
		if (call(login, "login", 0) < 0)
			return (1);
	if (argc < 2) {
		printf("List names matching: ");
		fflush(stdout);
		gets(foreignfn);
	} else {
		register char *p, *q;

		p = foreignfn;
		while (--argc > 0) {
			if (p != foreignfn)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	strcpy(fn, fgndir);
	strcat(fn, foreignfn);
	ctl_send(ifh, "DIRECTORY\215%s\215", fn);
	if (!ctl_receive())
		return (0);
	chrpkt(data);
	if (chrop(data) != 0202)
		list_dir();
	else
		async();	/* asyncronous mark */
	ctl_send(ifh, "CLOSE");
	c = (int)ctl_receive();
	while (1) {
		if (chrop(data) == EOFOP) {
			chrpkt(data);
			continue;
		} else if (chrop(data) == 0201) {
			if (chrlen(data) != 1) {
				printf("bad packet op = 0201 len = %d\n", chrlen(data));
			}
		}
		break;
	}
	return (c);
}

/*
 * probe foreign file
 */
probe(argc, argv)
	int argc;
	char *argv[];
{
	register char *p, *q;
	char fn[80];

	if (argc <= 0) {	/* give help */
		printf("Get information on a foreign file\n");
		return (0);
	}
	if (control == NULL) {
		printf("No connection!\n");
		return (1);
	}
	while (!loggedin)
		if (call(login, "login", 0) < 0)
			return (1);
	if (argc < 2) {
		printf("Probe foreign file: ");
		fflush(stdout);
		gets(foreignfn);
	} else {
		p = foreignfn;
		while (--argc > 0) {
			if (p != foreignfn)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	strcpy(fn, fgndir);
	strcat(fn, foreignfn);
	ctl_send("", "OPEN BINARY PROBE\215%s\215", fn);
	if (p = ctl_receive())
		putlms(p);
	return (0);
}

/*
 * delete foreign file
 */
delete(argc, argv)
	int argc;
	char *argv[];
{
	char fn[80];

	if (argc <= 0) {	/* give help */
		printf("Delete a file on the foreign host.\n");
		return (0);
	}
	if (control == NULL) {
		printf("No connection!\n");
		return (1);
	}
	while (!loggedin)
		if (call(login, "login", 0) < 0)
			return (1);
	if (argc < 2) {
		printf("Delete foreign file: ");
		fflush(stdout);
		gets(foreignfn);
	} else {
		register char *p, *q;

		p = foreignfn;
		while (--argc > 0) {
			if (p != foreignfn)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	strcpy(fn, fgndir);
	strcat(fn, foreignfn);
	ctl_send("", "DELETE\215%s\215", fn);
	ctl_receive();
	return (0);
}

/*
 * rename foreign file
 */
rename(argc, argv)
	int argc;
	char *argv[];
{
	char fn[80], ln[80];

	if (argc <= 0) {	/* give help */
		printf("Rename a file on the foreign host.\n");
		return (0);
	}
	if (control == NULL) {
		printf("No connection!\n");
		return (1);
	}
	while (!loggedin)
		if (call(login, "login", 0) < 0)
			return (1);
	if (argc < 2) {
		printf("Rename foreign file: ");
		fflush(stdout);
		gets(foreignfn);
	} else {
		register char *p, *q;

		p = foreignfn;
		while (--argc > 0) {
			if (p != foreignfn)
				p[-1] = ' ';
			q = *++argv;
			while (*p++ = *q++);
		}
	}
	printf("To: ");
	fflush(stdout);
	gets(localfn);
	strcpy(fn, fgndir);
	strcat(fn, foreignfn);
	strcpy(ln, fgndir);
	strcat(ln, localfn);
	ctl_send("", "RENAME\215%s\215%s\215", fn, ln);
	ctl_receive();
	return (0);
}

/*
 * change to ascii mode transfers (uses LISP machine character set)
 */
ascii(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Perform character mode transfers.\n");
		if (argc < 0) {
			printf("Translates to/from the 8-bit LISP machine\n");
			printf("character set to 7-bit ASCII.\n");
			printf("Most general text transfer between unlike systems.\n");
		}
		return (0);
	}
	mode = "CHARACTER";
	to_mode = to_ascii;
	from_mode = from_ascii;
	return (0);
}

/*
 * change to raw mode transfers
 */
raw(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Perform raw character mode transfers.\n");
		if (argc < 0) {
			printf("No character set translation at all.\n");
		}
		return (0);
	}
	mode = "CHARACTER RAW";
	to_mode = to_raw;
	from_mode = from_raw;
	return (0);
}

/*
 * change to super-image mode transfers:
 * doesn't use rubout for quoting
 */
image(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Perform super-image mode transfers.\n");
		if (argc < 0) {
			printf("Only translates \\n to and from 215\n");
			printf("in the LISP machine character set\n");
		}
		return (0);
	}
	mode = "CHARACTER SUPER-IMAGE";
	to_mode = to_image;
	from_mode = from_image;
	return (0);
}

/*
 * change to binary transfers with byte size 16
 * (good for press files)
 */
bytes(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Perform 16-bit binary mode transfers.\n");
		if (argc < 0) {
			printf("Good for transferring press files.\n");
		}
		return (0);
	}
	mode = "BINARY BYTE-SIZE 16";
	bytesize = 16;
	to_mode = to_binary;
	from_mode = from_binary;
	return (0);
}

/*
 * change to binary transfers with specified byte size
 */
binary(argc, argv)
	int argc;
	char *argv[];
{
	static char bmode[50];

	if (argc <= 0) {
		printf("Perform binary mode transfers.\n");
		if (argc < 0) {
			printf("Logical bytes transferred are transferred\n");
			printf("from or to naturally packed logical bytes\n");
			printf("in a short int on the local machine.\n");
			printf("Logical bytes are packed from left to\n");
			printf("right within a short int, and do not\n");
			printf("cross a short int boundary.\n");
		}
		return (0);
	}
	if (argc < 2) {
		printf("Logical byte size: ");
		fflush(stdout);
		gets(localfn);
		bytesize = atoi(localfn);
	} else
		bytesize = atoi(argv[1]);
	sprintf(bmode, "BINARY BYTE-SIZE %d", bytesize);
	mode = bmode;
	to_mode = to_binary;
	from_mode = from_binary;
	return (0);
}

/*
 * print status about the connection
 */
status(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Print status information.\n");
		return (0);
	}
	if (control != NULL) {
		printf("Connected to %s.\n", host->host_name);
		if (loggedin)
			printf("Logged in as %s.\n", user);
		else
			printf("Not logged in.\n");
	} else
		printf("No connection.\n");
	printf("Local directory is \"%s\".\n", localdir);
	printf("Foreign directory is \"%s\".\n", fgndir);
	printf("Transfer mode is %s.\n", mode);
	if (debug)
		printf("Verbose\n");
	return (0);
}

/*
 * enable verbose printout
 */
verbose(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Enable verbose printout.\n");
		return (0);
	}
	debug = 1;
	return (0);
}

/*
 * disable verbose printout
 */
brief(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {
		printf("Disable verbose printout.\n");
		return (0);
	}
	debug = 0;
	return (0);
}

/*
 * open a data connection
 */
opendata()
{
	sprintf(ifh, "I%x", getpid() & 0xFFFF);
	sprintf(ofh, "O%x", getpid() & 0xFFFF);
	ucase(ifh);
	ucase(ofh);
	data = connlisten(ofh);
	ctl_send("", "DATA-CONNECTION %s %s", ifh, ofh);
	if (data == NULL || chaccept(data) < 0) {
		printf("couldn't establish data connection\n");
		return (0);
	}
	ctl_receive();
	return (1);
}

/*
 * translate a string to upper case
 */
ucase(s)
	register char *s;
{
	for (; *s; s++)
		if (islower(*s))
			*s = toupper(*s);
}

/*
 * compute transfer rate
 */
comptime(bytes)
	register long bytes;
{
	register double delta;

	delta = after.time - before.time + (int)(after.millitm - before.millitm) / 1000.;
	printf("%D bytes in %g seconds", bytes, delta);
	if (delta != 0)
		printf(" = %g Kbytes per second", bytes * (1. / 1000.) / delta);
	putchar('\n');
}

/*
 * dump out an asynchronous mark packet
 */
async()
{
	char temp[CHMAXDATA + 1];
	register int i;
	register char *p;

	for (p = temp, i = chrlen(data); --i > 0; *p++ = chread(data));
	*p = 0;
	putlms(temp);
	fflush(stdout);
	chrpkt(data);
}

/*
 * print out a string using the Lisp Machine character set
 */
putlms(s)
	register char *s;
{
	register int c;

	while (c = *s++ & 0377) {

		switch (c) {
		case 0210:
		case 0211:
		case 0212:
		case 0214:
			c -= 0200;
			break;

		case 0215:
			c = '\n';
			break;
		}
		putchar(c);
	}
	putchar('\n');
}

makeargv()
{
	register char *cp;
	register char **argp = &margv[1];

	margc = 0;
	for (cp = line; *cp;) {
		while (isspace(*cp))
			cp++;
		if (*cp == '\0')
			break;
		*argp++ = cp;
		margc += 1;
		while (*cp != '\0' && !isspace(*cp))
			cp++;
		if (*cp == '\0')
			break;
		*cp++ = '\0';	/* Null terminate that string */
	}
	*argp++ = NULL;
}

bye(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {	/* give help */
		printf("Disconnect from the current site.\n");
		return (0);
	}
	if (control != NULL)
		chclose(control);
	if (data != NULL)
		chclose(data);
	control = NULL;
	data = NULL;
	loggedin = 0;
	return (0);
}

quit(argc, argv)
	int argc;
	char *argv[];
{
	if (argc <= 0) {	/* give help */
		printf("leave %s, closing any connection\n", prompt);
		return (0);
	}
	call(bye, "bye", 0);
	exit(0);
}

/*
 * help command -- call each command handler with argc == 0
 * and argv[0] == name
 */
help(argc, argv)
	int argc;
	char *argv[];
{
	register struct cmd *c;
	char *dargv[2];		/* can't use call because of screwy argc required */

	if (argc <= 0) {	/* give help! */
		printf("print help information\n");
		return (0);
	}
	dargv[1] = 0;
	if (argc == 1) {
		printf("Commands may be abbreviated.  Commands are:\n\n");
		for (c = cmdtab; c->name; c++) {
			printf("%-*s", HELPINDENT, c->name);
			dargv[0] = c->name;
			(*c->handler) (0, dargv);
		}
	} else {
		while (--argc > 0) {
			register char *arg;

			arg = *++argv;
			dargv[0] = arg;
			c = getcmd(arg);
			if (c == (struct cmd *)-1)
				printf("help: ambiguous command %s\n", arg);
			else if (c == (struct cmd *)0)
				printf("help: unknown command %s\n", arg);
			else
				(*c->handler) (-1, dargv);
		}
	}
	return (0);
}

/*
 * call routine with argc, argv set from args (terminated by 0).
 * VARARGS1
 */
call(routine, args)
	int (*routine)();
	int args;
{
	register int *argp;
	register int argc;

	for (argc = 0, argp = &args; *argp++ != 0; argc++);
	return ((*routine) (argc, &args));
}

/*
 * File transfer functions.
 * All called with from file or to file,
 * and return the number of bytes moved.
 */

long
to_raw(f)
	register FILE *f;
{
	register int fd, len, op, cnt;
	register char *bufptr;
	register long int bytes;

	bytes = 0;
	fd = fileno(f);
	op = data->wpkt.cp_op;
	while (1) {
		if ((len = read(fd, databuf + 1, (DBSIZE / CHMAXDATA) * CHMAXDATA))
		    != (DBSIZE / CHMAXDATA) * CHMAXDATA) {
			cnt = len / CHMAXDATA;
			len -= cnt * CHMAXDATA;
			bytes += CHMAXDATA * cnt;
			for (bufptr = databuf; cnt > 0; --cnt) {
				*bufptr = op;
				write(data->file, bufptr, CHMAXDATA + 1);

				bufptr += CHMAXDATA;
			}
			bcopy(++bufptr, data->wpkt.cp_data, len);
			bytes += len;
			data->wptr = data->wpkt.cp_data + len;
			break;
		}
		for (bufptr = databuf, cnt = 0; cnt < DBSIZE / CHMAXDATA; cnt++) {
			*bufptr = op;
			write(data->file, bufptr, CHMAXDATA + 1);

			bufptr += CHMAXDATA;
		}
		bytes += (DBSIZE / CHMAXDATA) * CHMAXDATA;
	}
#ifdef notdef
	/*
	 * What used to be here... 
	 */
	while ((c = getc(f)) != EOF) {
		chwrite(c, data);
		bytes++;
	}
#endif
	return (bytes);
}

long
from_raw(t)
	register FILE *t;
{
	register int fd, len, cnt, lastc;
	register char *bufptr;
	register long int bytes;

	fd = fileno(t);

	/*
	 * First bit of data is already in connection's packet
	 */
	bytes = data->rcnt;
	bufptr = databuf + bytes;
	bcopy(data->rptr, databuf + 1, bytes);
#ifdef notdef
	/*
	 * This is what the code above is doing faster 
	 */
	bufptr = databuf + 1;
	bytes = data->rcnt;
	for (cnt = data->rcnt; cnt > 0; cnt--)
		*bufptr++ = *data->rptr++;
	--bufptr;		/* point to last real character */
#endif
	while (1) {
		lastc = *bufptr;	/* save char */
		if ((len = read(data->file, bufptr, CHMAXDATA + 1)) <= 0 || !(*bufptr & 0200)) {
			data->rpkt.cp_op = *bufptr;
			*bufptr = lastc;
			break;
		}
		*bufptr = lastc;	/* put char back */
		bufptr += --len;	/* account for opcode */
		bytes += len;
		if (bufptr > &databuf[DBSIZE - 1]) {
			if (write(fd, databuf + 1, bufptr - databuf) != bufptr - databuf)
				fprintf(stderr, "write botch\n");
			bufptr = databuf;
		}
#ifdef notdef
		/*
		 * Make disk writes the block size the filesystem? 
		 */
		/*
		 * Didn't seem to help much.. 
		 */
		write(fd, databuf, MAXDBSIZE);
		cnt = bufptr - &databuf[MAXDBSIZE - 1];
		for (bufptr = databuf, optr = &databuf[MAXDBSIZE]; cnt > 0; --cnt)
			*bufptr++ = *optr++;
#endif
	}
	if (bufptr - databuf)
		write(fd, databuf + 1, bufptr - databuf);
#ifdef notdef
	/*
	 * What used to be here! 
	 */
	while ((c = chread(data)) != EOF) {
		bytes++;
		putc(c, t);
	}
#endif
	return (bytes);
}

long
to_image(f)
	register FILE *f;
{
	register int c;
	register long int bytes;

	bytes = 0;
	while ((c = getc(f)) != EOF) {
		bytes++;
		chwrite(c, data);
	}
	return (bytes);
}

long
from_image(t)
	register FILE *t;
{
	register int c;
	register long int bytes;

	bytes = 0;
	while ((c = chread(data)) != EOF) {
		bytes++;
		putc(c, t);
	}
	return (bytes);
}

long
to_ascii(f)
	register FILE *f;
{
	register int c, quoted;
	register long int bytes;

	bytes = 0;
	quoted = 0;
	while ((c = getc(f)) != EOF) {
		if (quoted) {
			switch (c) {
			default:
				c |= 0200;

			case 010:
			case 011:
			case 012:
			case 014:
			case 015:
			case 0177:
				quoted = 0;
				chwrite(c, data);
				bytes++;
				break;
			}
		} else if (c == 0177)
			quoted = 1;
		else {
			switch (c) {
			case '\n':
				c = 0215;
				break;
			case 0210:
			case 0211:
			case 0212:
			case 0213:
			case 0214:
			case 0215:
			case 0377:
				c &= 0177;
				break;
			case 015:
				c = 0212;
				break;
			case 010:
			case 011:
			case 013:
			case 014:
				c |= 0200;
			}
			chwrite(c, data);
			bytes++;
		}
	}
	return (bytes);
}

long
from_ascii(t)
	register FILE *t;
{
	register int c;
	register long int bytes;

	bytes = 0;
	while ((c = chread(data)) != EOF) {
		bytes++;
		switch (c) {
		case 010:
		case 011:
		case 012:
		case 013:
		case 014:
		case 015:
		case 0177:
			c |= 0200;
			break;

		case 0212:
			c = 015;
			break;
		case 0210:
		case 0211:
		case 0213:
		case 0214:
			c &= 0177;
			break;

		case 0215:
			c = '\n';
			break;

		default:
			break;
		}
		putc(c, t);
	}
	return (bytes);
}

long
to_binary(f)
	register FILE *f;
{
	register int c;
	register long int bytes;

	bytes = 0;

	chwop(0300, data);
	while ((c = getc(f)) != EOF) {
		chwrite(c, data);
		if (bytesize <= 8)
			chwrite(0, data);
		bytes++;
	}
	return (bytes);
}

long
from_binary(t)
	register FILE *t;
{
	register int c;
	register long int bytes;

	bytes = 0;
	while ((c = chread(data)) != EOF) {
		putc(c, t);
		c = chread(data);
		if (bytesize > 8)
			putc(c, t);
		bytes += 2;
	}
	return (bytes);
}

char *
getline(str, s)
	char *str;
	register int s;
{
	register char *p = str;
	register int c;

	while ((c = chread(data)) != EOF) {
		switch (c) {
		case 010:
		case 011:
		case 012:
		case 014:
		case 015:
		case 0177:
			if (--s > 0)
				*p++ = 0177;
			break;

		case 0210:
		case 0211:
		case 0212:
		case 0214:
			c -= 0200;
			break;

		case 0215:
			c = '\n';
			break;

		default:
			if (c >= 0200) {
				if (--s > 0)
					*p++ = 0177;
				c -= 0200;
			}
			break;
		}
		if (--s > 0)
			*p++ = c;
		if (c == '\n')
			break;
	}
	if (c == EOF && p == str)
		return (NULL);
	if (--s > 0)
		*p = 0;
	return (str);
}

#define propname(x) "x", sizeof("x")-1
struct plist {
	char *pname;		/* property name */
	int psize;		/* length of name */
	char *pscanf;		/* scanf string */
	union {
		long lval;
		char sval[132];
	} pvalue;
} props[] = {
	{propname(LENGTH - IN - BLOCKS), "%D"},
	{propname(LENGTH - IN - BYTES), "%D"},
	{propname(BYTE - SIZE), "%D"},
	{propname(AUTHOR), "%s"},
	{propname(CREATION - DATE), "%[^\n]"},
	{0},
};

/*
 * Read a directory pseudo-file.
 */
list_dir()
{
	char line[132];
	char name[132];
	register int i, c;
	register struct plist *p;

	/*
	 * first read header 
	 */
	i = 0;
	while ((c = chread(data)) != EOF) {
		if (c == 0215) {
			if (i++ > 0)
				break;	/* found a blank line */
		} else
			i = 0;
	}
	while (getline(name, sizeof name) != NULL) {
		i = strlen(name) - 1;
		if (name[i] == '\n')
			name[i] = 0;
		while (getline(line, sizeof line) != NULL) {
			if (line[0] == '\n')
				break;	/* found a blank line */
			for (p = props; p->pname; p++)
				if (strncmp(p->pname, line, p->psize) == 0) {
					sscanf(&line[p->psize + 1], p->pscanf, &p->pvalue);
					break;
				}
		}
		sprintf(line, "%D(%D)", props[1].pvalue.lval, props[2].pvalue.lval);
		printf("%5D %-10s %20s %-10s %s\n", props[0].pvalue.lval, line, props[4].pvalue.sval, props[3].pvalue.sval, name);
	}
}
