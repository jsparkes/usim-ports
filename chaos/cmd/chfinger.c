#ifndef lint
static char *sccsid = "@(#)finger.c	4.5 (Berkeley) 9/16/83";
#endif
/* NOTES
 *
 * 1/25/85 dove
 *	check for mail forwarding
 *
 * 4/29/85 vanni
 * 	Added the File Transfer Utility (FTU) portion, to check for users
 *	that have login under `cftp' like programs.
 *
 * 5/25/85 dove (was finger.c3)
 * 	accept any office field without assuming it is a phone number or random
 *
 * 6/17/85 vanni
 * 	added TTYPLACE portion, to display tty location, instead of phone#
 *	- extensive edits where done to rest of code
 */
#define TTYPLACE	"/etc/ttyplace"
#define FTU

/* BCN@EDDIE's finger program which handles chaos & internet hosts.
 * Hacked by SAJ@PREP so that local finger gives office field gleaned from
 * /etc/utmp (CJL's suggestion) 2-20-85
 */
#define CHAOS

/*  This is a finger program.  It prints out useful information about users
 *  by digging it up from various system files.  It is not very portable
 *  because the most useful parts of the information (the full user name,
 *  office, and phone numbers) are all stored in the VAX-unused gecos field
 *  of /etc/passwd, which, unfortunately, other UNIXes use for other things.
 *
 *  There are three output formats, all of which give login name, teletype
 *  line number, and login time.  The short output format is reminiscent
 *  of finger on ITS, and gives one line of information per user containing
 *  in addition to the minimum basic requirements (MBR), the full name of
 *  the user, his idle time and office location and phone number.  The
 *  quick style output is UNIX who-like, giving only name, teletype and
 *  login time.  Finally, the long style output give the same information
 *  as the short (in more legible format), the home directory and shell
 *  of the user, and, if it exits, a copy of the file .plan in the users
 *  home directory.  Finger may be called with or without a list of people
 *  to finger -- if no list is given, all the people currently logged in
 *  are fingered.
 *
 *  The program is validly called by one of the following:
 *
 *	finger			{short form list of users}
 *	finger -l		{long form list of users}
 *	finger -b		{briefer long form list of users}
 *	finger -q		{quick list of users}
 *	finger -i		{quick list of users with idle times}
 *	finger namelist		{long format list of specified users}
 *	finger -s namelist	{short format list of specified users}
 *	finger -w namelist	{narrow short format list of specified users}
 *
 *  where 'namelist' is a list of users login names.
 *  The other options can all be given after one '-', or each can have its
 *  own '-'.  The -f option disables the printing of headers for short and
 *  quick outputs.  The -b option briefens long format outputs.  The -p
 *  option turns off plans for long format outputs.
 */

#include	<sys/file.h>
#include	<sys/types.h>
#include	<sys/stat.h>
#include	<sgtty.h>
#include	<utmp.h>
#include	<signal.h>
#include	<pwd.h>
#include	<stdio.h>
#include	<lastlog.h>
#include	<sys/time.h>
#include	<sys/socket.h>
#include	<netinet/in.h>
#include	<netdb.h>

#ifdef CHAOS
#include	<sys/chaos.h>
#include	<hosttab.h>
#endif	/* CHAOS */

struct utmp utmp;		/* for sizeof */

#define NMAX sizeof(utmp.ut_name)
#define LMAX sizeof(utmp.ut_line)
#define HMAX sizeof(utmp.ut_host)
#define StrCpy(Fr)	( (char *)strcpy(malloc( strlen(Fr) + 1), Fr) )

#define		ASTERISK	'*'	/* ignore this in real name */
#define		BLANK		' '	/* blank character (i.e. space) */
#define		CAPITALIZE	0137&	/* capitalize character macro */
#define		COMMA		','	/* separator in pw_gecos field */
#define		COMMAND		'-'	/* command line flag char */
#define		TECHSQ		'T'	/* tech square office */
#define		MIT		'M'	/* MIT office */
#define		LINEBREAK	012	/* line feed */
#define		NULLSTR		""	/* the null string, opposed to NULL */
#define		SAMENAME	'&'	/* repeat login name in real name */
#define		TALKABLE	0222	/* tty is writeable if 222 mode */
#define		MAILDIR		"/usr/spool/mail/"	/* default mail dir */
#define		NO_LOGIN	0
#define		DIRECT_LOGIN	1
#define		FILE_LOGIN	3
#define		REMOTE_LOGIN	4

struct person {			/* one for each person fingered */
	char name[NMAX + 1];	/* login name */
	char tty[LMAX + 1];	/* NULL terminated tty line */
#ifdef TTYPLACE
	char ttywhere[80];	/* NULL terminated tty location */
#endif	/* TTYPLACE */
	long loginat;		/* time of login (possibly last) */
	long idletime;		/* how long idle (if logged in) */
	short int loggedin;	/* flag for being logged in */
	short int writeable;	/* flag for tty being writeable */
	char *realname;		/* pointer to full name */
	char office[HMAX + 1];	/* pointer to office name */
	char *officephone;	/* pointer to office phone no. */
	char *homephone;	/* pointer to home phone no. */
	char *random;		/* for any random stuff in pw_gecos */
#ifdef CHAOS
	char *host;		/* Chaos host for file server */
#endif	/* CHAOS */
	struct passwd *pwd;	/* structure of /etc/passwd stuff */
	struct person *link;	/* link to next person */
};

struct passwd *NILPWD = 0;
struct person *NILPERS = 0;

int persize = sizeof(struct person);
int pwdsize = sizeof(struct passwd);

char LASTLOG[] = "/usr/adm/lastlog";	/* last login info */
char USERLOG[] = "/etc/utmp";	/* who is logged in */
char outbuf[BUFSIZ];		/* output buffer */
char *ctime();

int unbrief = 1;		/* -b option default */
int header = 1;			/* -f option default */
int hack = 1;			/* -h option default */
int idle = 0;			/* -i option default */
int large = 0;			/* -l option default */
int match = 1;			/* -m option default */
int plan = 1;			/* -p option default */
int unquick = 1;		/* -q option default */
int small = 0;			/* -s option default */
int wide = 1;			/* -w option default */

#ifdef FTU
char *index();
#endif	/* FTU */

int lf;
int llopenerr;
int fmanual = 0;

long tloc;			/* current time */



main(argc, argv)
	int argc;
	char *argv[];
{

	FILE *fp, *fopen();	/* for plans */
	struct passwd *getpwent();	/* read /etc/passwd */
	struct person *person1, *p, *pend;	/* people */
	struct passwd *pw;	/* temporary */
	struct utmp user;	/*   ditto   */
	char *malloc();
	char *s, *pn, *ln;
	char c;
	char *PLAN = "/.plan";	/* what plan file is */
	char *PROJ = "/.project";	/* what project file */
	char *MM = "%MENTION-MAIL%\n";
	int PLANLEN = strlen(PLAN);
	int PROJLEN = strlen(PROJ);
	int MMLEN = strlen(MM);
	int done;
	int uf;
	int usize = sizeof user;
	int unshort;
	int i, j;
	int fngrlogin;

#ifdef FTU
	struct chlogin chf;
#endif	/* FTU */
#ifdef TTYPLACE
	char *ttywhere();
#endif
	setbuf(stdout, outbuf);	/* buffer output */
	/*
	 * parse command line for (optional) arguments
	 */
	i = 1;
	if (strcmp(*argv, "sh")) {
		fngrlogin = 0;
		while (i++ < argc && (*++argv)[0] == COMMAND) {
			for (s = argv[0] + 1; *s != NULL; s++) {
				switch (*s) {

				case 'b':
					unbrief = 0;
					break;

				case 'f':
					header = 0;
					break;

				case 'h':
					hack = 0;
					break;

				case 'i':
					idle = 1;
					unquick = 0;
					break;

				case 'l':
					large = 1;
					break;

				case 'm':
					match = 0;
					break;

				case 'p':
					plan = 0;
					break;

				case 'q':
					unquick = 0;
					break;

				case 's':
					small = 1;
					break;

				case 'w':
					wide = 0;
					break;

				default:
					fprintf(stderr, "finger: Usage -- 'finger [-bfhilmpqsw] [login1 [login2 ...] ]'\n");
					exit(1);
				}
			}
		}
	} else
		fngrlogin = 1;	/* argument flags were not processed */

	if (unquick || idle)
		time(&tloc);
	/*
	 * If no login names given. Get them by reading USERLOG
	 */
	if ((i > argc) || fngrlogin) {	/* Do all loggedin USERS */
		unshort = large;
		if ((uf = open(USERLOG, 0)) < 0) {
			fprintf(stderr, "finger: error opening %s\r\n", USERLOG);
			exit(2);
		}
		done = 0;
		user.ut_name[0] = NULL;
		p = person1 = (struct person *)malloc(persize);	/* LL Head */

		while (read(uf, (char *)&user, usize) == usize) {
			if (user.ut_name[0] == NULL)
				continue;
			for (j = 0; j < NMAX; j++) {
				p->tty[j] = user.ut_line[j];
				p->name[j] = user.ut_name[j];
			}
			for (j = 0; j < HMAX; j++) {
				p->office[j] = user.ut_host[j];
			}
			p->office[HMAX] = NULL;
			p->name[NMAX] = NULL;
			p->tty[NMAX] = NULL;
			p->loginat = user.ut_time;
			p->pwd = NILPWD;
			if (p->office[0])
				p->loggedin = REMOTE_LOGIN;
			else
				p->loggedin = DIRECT_LOGIN;

			p->link = (struct person *)malloc(persize);
			p = p->link;
			done++;
		}
		close(uf);
		if (!done) {
			printf("No one logged on\r\n");
			exit(0);
		}
#ifdef FTU
		/*
		 * add to list all the FTU users
		 */
		if ((uf = open(FILEUTMP, 0)) >= 0) {
			chf.cl_user[0] = NULL;
			while (read(uf, (char *)&chf, sizeof(chf)) == sizeof(chf)) {
				if (chf.cl_user[0] == NULL)
					continue;
				for (j = 0; j < NMAX; j++)
					p->name[j] = chf.cl_user[j];
				p->name[NMAX] = NULL;
				strcpy(p->tty, "FILE");

				if (!(s = chaos_name(chf.cl_haddr))) {
					s = StrCpy("CHAOS|XXXXXX");
					sprintf(s, "CHAOS|%o", chf.cl_haddr);
				}
				p->host = s;
				p->loginat = chf.cl_ltime;
				p->pwd = NILPWD;
				p->loggedin = FILE_LOGIN;
				p->writeable = 1;

				p->link = (struct person *)malloc(persize);
				p = p->link;
			}
			close(uf);
		}
#endif	/* FTU */
		/*
		 * we had created one node too many, remove it
		 */
		pend = p;
		for (p = person1; p->link != pend; p = p->link);
		p->link = NILPERS;
		pend = p;
	}
	/*
	 * Name(s) was passed as argument, check to see if they're logged in
	 */
	else {
		fmanual++;
		unshort = (small == 1 ? 0 : 1);
		while (i <= argc && nfinger(*argv)) {
			i++;
			argv++;
		}
		if (i++ > argc)
			exit(0);

		p = person1 = (struct person *)malloc(persize);
		strcpy(person1->name, (argv++)[0]);
		person1->loggedin = NO_LOGIN;
		person1->pwd = NILPWD;

		while (i++ <= argc) {
			if (nfinger(*argv)) {
				argv++;
				continue;
			}
			p->link = (struct person *)malloc(persize);
			p = p->link;
			strcpy(p->name, (argv++)[0]);
			p->loggedin = NO_LOGIN;
			p->pwd = NILPWD;
		}
		p->link = NILPERS;
		pend = p;
		/*
		 * Get login information from USERLOG
		 */
		if ((uf = open(USERLOG, 0)) >= 0) {
			while (read(uf, (char *)&user, usize) == usize) {
				if (user.ut_name[0] == NULL)
					continue;
				for (p = person1; p != NILPERS; p = p->link) {
					pw = p->pwd;
					if (pw == NILPWD) {
						i = (strcmp(p->name, user.ut_name) ? 0 : NMAX);
					} else {
						i = 0;
						while ((i < NMAX) && (pw->pw_name[i] == user.ut_name[i])) {
							if (pw->pw_name[i] == NULL) {
								i = NMAX;
								break;
							}
							i++;
						}
					}
					if (i == NMAX) {
						if (p->loggedin == DIRECT_LOGIN) {
							pend->link = (struct person *)malloc(persize);
							pend = pend->link;
							pend->link = NILPERS;
							strcpy(pend->name, p->name);
							for (j = 0; j < NMAX; j++) {
								pend->tty[j] = user.ut_line[j];
							}
							pend->tty[NMAX] = NULL;
							pend->loginat = user.ut_time;
							pend->loggedin = 2;
							if (pw == NILPWD) {
								pend->pwd = NILPWD;
							} else {
								pend->pwd = (struct passwd *)malloc(pwdsize);
								pwdcopy(pend->pwd, pw);
							}
						} else {
							if (p->loggedin != 2) {
								for (j = 0; j < NMAX; j++) {
									p->tty[j] = user.ut_line[j];
								}
								p->tty[NMAX] = NULL;
								p->loginat = user.ut_time;
								p->loggedin = DIRECT_LOGIN;
							}
						}
					}
				}
			}
			close(uf);
		} else {
			fprintf(stderr, "finger: error opening %s\n", USERLOG);
			exit(2);
		}
	}
	/*
	 * read /etc/passwd for the useful info.
	 * If names were manually specified. Then go through entire passwd
	 */
	if (unquick && fmanual) {
		setpwent();	/* rewind passwd file */
		while ((pw = getpwent()))
			for (p = person1; p != NILPERS; p = p->link) {
				if (strcmp(p->name, pw->pw_name) && !matchcmp(pw->pw_gecos, pw->pw_name, p->name))
					continue;
				/*
				 * there was a match, see if there was already an entry
				 *  if so handle multiple login entries
				 *  -- append new "duplicate" entry to end of list
				 */
				if (p->pwd == NILPWD) {	/* new entry */
					p->pwd = (struct passwd *)malloc(pwdsize);
					pwdcopy(p->pwd, pw);
					break;
				} else {	/* entry existed */
					pend->link = (struct person *)malloc(persize);
					pend = pend->link;
					pend->link = NILPERS;
					strcpy(pend->name, p->name);
					pend->pwd = (struct passwd *)malloc(pwdsize);
					pwdcopy(pend->pwd, pw);
					break;
				}
			}
		endpwent();
	} else if (unquick) {	/* names were not passed manually */
		setpwent();	/* rewind passwd file */
		for (done = 0; !done && (pw = getpwent());) {
			done++;	/* presume done */
			for (p = person1; p; p = p->link) {
				if (p->pwd)
					continue;	/* passwd existed */

				if (!strcmp(p->name, pw->pw_name)) {
					p->pwd = (struct passwd *)malloc(pwdsize);
					pwdcopy(p->pwd, pw);
				} else
					done = 0;	/* try next */
			}
		}
		endpwent();
	}
	/*
	 * find the LAST Login of a user by checking the LASTLOG file.
	 * *  the entry is indexed by the uid, so this can only be done if
	 * *  the uid is known (which it isn't in quick mode)
	 */
	if (fmanual || unquick) {
		if ((lf = open(LASTLOG, 0)) >= 0) {
			llopenerr = 0;
		} else {
			fprintf(stderr, "finger: lastlog open error\r\n");
			llopenerr = 1;
		}
		for (p = person1; p != NILPERS; p = p->link) {
			if (p->loggedin == 2)
				p->loggedin = DIRECT_LOGIN;
			decode(p);
		}
		if (!llopenerr) {
			close(lf);
		}
	}
#ifdef TTYPLACE
	/*
	 * for each tty, find the equivalent port
	 */
	for (p = person1; p != NILPERS; p = p->link)
		strcpy(p->ttywhere, ttywhere(p->tty));
#endif	/* TTYPLACE */
	/*
	 * print Header
	 */
	if (header) {
		if (unquick) {
			if (!unshort) {
				if (wide) {
					printf("Login       Name              TTY Idle    When         Where\r\n");
				} else {
					printf("Login    TTY Idle    When            Office\r\n");
				}
			}
		} else {
			printf("Login      TTY            When");
			if (idle) {
				printf("             Idle");
			}
			printf("\r\n");
		}
	}
	/*
	 * print out what we got
	 */
	for (p = person1; p != NILPERS; p = p->link) {
		if (unquick) {
			if (unshort) {
				personprint(p);
				if (p->pwd != NILPWD) {
					if (hack) {
						s = malloc(strlen((p->pwd)->pw_dir) + PROJLEN + 1);
						strcpy(s, (p->pwd)->pw_dir);
						strcat(s, PROJ);
						if ((fp = fopen(s, "r")) != NULL) {
							printf("Project: ");
							while ((c = getc(fp)) != EOF) {
								if (c == LINEBREAK) {
									break;
								}
								putc(c, stdout);
							}
							fclose(fp);
							printf("\r\n");
						}
					}
#ifdef FTU
					s = malloc(strlen((p->pwd)->pw_dir) + strlen("/.forward") + 1);
					strcpy(s, (p->pwd)->pw_dir);
					strcat(s, "/.forward");
					if (fp = fopen(s, "r")) {
						printf("Mail forwards to: <");
						while ((c = getc(fp)) != EOF && c != '\n')
							putc(c, stdout);
						fclose(fp);
						printf(">\n");
					}
					free(s);
#endif
					if (plan) {
						s = malloc(strlen((p->pwd)->pw_dir) + PLANLEN + 1);
						strcpy(s, (p->pwd)->pw_dir);
						strcat(s, PLAN);
						if ((fp = fopen(s, "r")) == NULL) {
							printf("No Plan.\r\n");
						} else {
							register int i = 0, j;
							register int okay = 1;
							char mailbox[100];	/* mailbox name */

							printf("Plan:\r\n");
							while ((c = getc(fp)) != EOF) {
								if (i < MMLEN) {
									if (c != MM[i]) {
										if (okay) {
											for (j = 0; j < i; j++) {
												putc(MM[j], stdout);
											}
										}
										putc(c, stdout);
										okay = 0;
									}
								} else
									putc(c, stdout);	/* i >= MMLEN */
								i++;
							}
							fclose(fp);
							if (okay) {
								strcpy(mailbox, MAILDIR);	/* start with the directory */
								strcat(mailbox, (p->pwd)->pw_name);
								if (access(mailbox, F_OK) == 0) {
									struct stat statb;

									stat(mailbox, &statb);
									if (statb.st_size) {
										printf("User %s has new mail.\r\n", (p->pwd)->pw_name);
									};
								}
							}

						}
					}
				}
				if (p->link != NILPERS) {
					printf("\r\n");
				}
			} else {
				shortprint(p);
			}
		} else {
			quickprint(p);
		}
	}
	exit(0);
}


/*  given a pointer to a pwd (pfrom) copy it to another one, allocating
 *  space for all the stuff in it.  Note: Only the useful (what the
 *  program currently uses) things are copied.
 */
pwdcopy(pto, pfrom)		/* copy relevant fields only */
	struct passwd *pto, *pfrom;
{
	pto->pw_name = malloc(strlen(pfrom->pw_name) + 1);
	strcpy(pto->pw_name, pfrom->pw_name);
	pto->pw_uid = pfrom->pw_uid;
	pto->pw_gecos = malloc(strlen(pfrom->pw_gecos) + 1);
	strcpy(pto->pw_gecos, pfrom->pw_gecos);
	pto->pw_dir = malloc(strlen(pfrom->pw_dir) + 1);
	strcpy(pto->pw_dir, pfrom->pw_dir);
	pto->pw_shell = malloc(strlen(pfrom->pw_shell) + 1);
	strcpy(pto->pw_shell, pfrom->pw_shell);
}


/*  print out information on quick format giving just name, tty, login time
 *  and idle time if idle is set.
 */
quickprint(pers)
	struct person *pers;
{
	int idleprinted;

	printf("%-*.*s", NMAX, NMAX, pers->name);
	printf("  ");
	if (pers->loggedin) {
#ifdef FTU
		if (idle && pers->loggedin != FILE_LOGIN) {
#else
		if (idle) {
#endif	/* FTU */
			findidle(pers);
			if (pers->writeable) {
				printf(" %-*.*s %-16.16s", LMAX, LMAX, pers->tty, ctime(&pers->loginat));
			} else {
				printf("*%-*.*s %-16.16s", LMAX, LMAX, pers->tty, ctime(&pers->loginat));
			}
			printf("   ");
			idleprinted = ltimeprint(&pers->idletime);
		} else {
			printf(" %-*.*s %-16.16s", LMAX, LMAX, pers->tty, ctime(&pers->loginat));
		}
	} else {
		printf("          Not Logged In");
	}
	printf("\r\n");
}


/*  print out information in short format, giving login name, full name,
 *  tty, idle time, login time, office location and phone.
 */
shortprint(pers)
	struct person *pers;
{
	struct passwd *pwdt = pers->pwd;
	char buf[26];
	int i, len, offset, dialup;

	if (pwdt == NILPWD) {
		printf("%-*.*s", NMAX, NMAX, pers->name);
#ifdef FTU
		if (pers->loggedin != FILE_LOGIN)
			return;
		strcpy(buf, ctime(&pers->loginat));
		for (i = 4; i < 19; i++)
			buf[i] = buf[i + 7];
		printf("    ???                %s    %-9.9s  Host: %s\r\n", pers->tty, buf, pers->host);
#else
		printf("       ???\r\n");
#endif	/* FTU */
		return;
	}
	printf("%-*.*s", NMAX, NMAX, pwdt->pw_name);
	dialup = 0;
	if (wide) {
		if (strlen(pers->realname) > 0) {
			printf(" %-20.20s", pers->realname);
		} else {
			printf("        ???          ");
		}
	}
	if (pers->loggedin) {
		if (pers->writeable) {
			printf("  ");
		} else {
			printf(" *");
		}
	} else {
		printf("  ");
	}
#ifdef FTU
	if (pers->loggedin == FILE_LOGIN)
		printf("FILE   ");
	else
#endif	/* FTU */
	if (strlen(pers->tty) > 0) {
		strcpy(buf, pers->tty);
		if ((buf[0] == 't') && (buf[1] == 't') && (buf[2] == 'y')) {
			offset = 3;
			for (i = 0; i < 2; i++) {
				buf[i] = buf[i + offset];
			}
		}
		if ((buf[0] == 'd') && pers->loggedin) {
			dialup = 1;
		}
		printf("%-2.2s ", buf);
	} else {
		printf("   ");
	}
	strcpy(buf, ctime(&pers->loginat));
	if (pers->loggedin) {
#ifdef FTU
		if (pers->loggedin != FILE_LOGIN)
#endif	/* FTU */
			stimeprint(&pers->idletime);
		offset = 7;
		for (i = 4; i < 19; i++) {
			buf[i] = buf[i + offset];
		}
		printf(" %-9.9s ", buf);
	} else if (pers->loginat == 0)
		printf(" < .  .  .  . >");
	else if (tloc - pers->loginat >= 180 * 24 * 60 * 60)
		printf(" <%-6.6s, %-4.4s>", buf + 4, buf + 20);
	else
		printf(" <%-12.12s>", buf + 4);

#ifdef FTU
	if (pers->loggedin == FILE_LOGIN)
		printf(" Host: %s", pers->host);
	else
#endif
	if (pers->loggedin == REMOTE_LOGIN && strlen(pers->office) > 0)
		printf(" %-16.16s", pers->office);
#ifdef TTYPLACE
	else if (pers->ttywhere[0])
		printf(" %s", pers->ttywhere);
#endif
	printf("\r\n");
}

/*  print out a person in long format giving all possible information.
 *  directory and shell are inhibited if unbrief is clear.
 */
personprint(pers)
	struct person *pers;
{
	struct passwd *pwdt = pers->pwd;
	int idleprinted;

	if (pwdt == NILPWD) {
		printf("Login name: %-10s", pers->name);
		printf("			");
		printf("In real life: ???\r\n");
		return;
	}
	printf("Login name: %-10s", pwdt->pw_name);
	if (pers->loggedin) {
		if (pers->writeable) {
			printf("			");
		} else {
			printf("	(messages off)	");
		}
	} else
		printf("			");

	if (strlen(pers->realname) > 0) {
		printf("In real life: %-s", pers->realname);
	}

	if (strlen(pers->office) > 0) {
		printf("\r\nOffice: %-.16s", pers->office);
		if (strlen(pers->officephone) > 0) {
			printf(", %s", pers->officephone);
			if (strlen(pers->homephone) > 0) {
				printf("		Home phone: %s", pers->homephone);
			} else {
				if (strlen(pers->random) > 0) {
					printf("	%s", pers->random);
				}
			}
		} else {
			if (strlen(pers->homephone) > 0) {
				printf("\t\t\tHome phone: %s", pers->homephone);
			}
			if (strlen(pers->random) > 0) {
				printf("			%s", pers->random);
			}
		}
	} else {
		if (strlen(pers->officephone) > 0) {
			printf("\r\nPhone: %s", pers->officephone);
			if (strlen(pers->homephone) > 0) {
				printf("\r\n, %s", pers->homephone);
				if (strlen(pers->random) > 0) {
					printf(", %s", pers->random);
				}
			} else {
				if (strlen(pers->random) > 0) {
					printf("\r\n, %s", pers->random);
				}
			}
		} else {
			if (strlen(pers->homephone) > 0) {
				printf("\r\nPhone: %s", pers->homephone);
				if (strlen(pers->random) > 0) {
					printf(", %s", pers->random);
				}
			} else {
				if (strlen(pers->random) > 0) {
					printf("\r\n%s", pers->random);
				}
			}
		}
	}
	if (unbrief) {
		printf("\r\n");
		printf("Directory: %-25s", pwdt->pw_dir);
		if (strlen(pwdt->pw_shell) > 0) {
			printf("	Shell: %-s", pwdt->pw_shell);
		}
	}
	if (pers->loggedin) {
		register char *ep = ctime(&pers->loginat);

		printf("\r\nOn since %15.15s on %-*.*s	", &ep[4], LMAX, LMAX, pers->tty);
		idleprinted = ltimeprint(&pers->idletime);
		if (idleprinted) {
			printf(" Idle Time");
		}
		printf("\r\nLocation: %s", pers->ttywhere);
	} else if (pers->loginat == 0)
		printf("\r\nNever logged in.");
	else if (tloc - pers->loginat > 180 * 24 * 60 * 60) {
		register char *ep = ctime(&pers->loginat);

		printf("\r\nLast login %10.10s, %4.4s on %.*s", ep, ep + 20, LMAX, pers->tty);
	} else {
		register char *ep = ctime(&pers->loginat);

		printf("\r\nLast login %16.16s on %.*s", ep, LMAX, pers->tty);
	}
	printf("\r\n");

}

/*  decode the information in the gecos field of /etc/passwd
 *  another hacky section of code, but given the format the stuff is in...
 */
decode(pers)
	struct person *pers;
{
	struct passwd *pwdt = pers->pwd;
	char buffer[256], *bp, *gp, *lp;
	char *phone();
	int alldigits;
	int len;
	int i;

	pers->realname = NULLSTR;
	pers->officephone = NULLSTR;
	pers->homephone = NULLSTR;
	pers->random = NULLSTR;
	if (pwdt != NILPWD) {
		gp = pwdt->pw_gecos;
		bp = &buffer[0];
		if (*gp == ASTERISK) {
			gp++;
		}
		while ((*gp != NULL) && (*gp != COMMA)) {	/* name */
			if (*gp == SAMENAME) {
				lp = pwdt->pw_name;
				*bp++ = CAPITALIZE(*lp++);
				while (*lp != NULL) {
					*bp++ = *lp++;
				}
			} else {
				*bp++ = *gp;
			}
			gp++;
		}
		*bp = NULL;
		pers->realname = malloc(strlen(&buffer[0]) + 1);
		strcpy(pers->realname, &buffer[0]);
		if (*gp++ == COMMA) {	/* office, supposedly */
			alldigits = 1;
			bp = &buffer[0];
			while ((*gp != NULL) && (*gp != COMMA)) {
				*bp = *gp++;
				alldigits = alldigits && ('0' <= *bp) && (*bp <= '9');
				bp++;
			}
			*bp = NULL;
			len = strlen(&buffer[0]);
			if (buffer[len - 1] == TECHSQ) {
				strcpy(&buffer[len - 1], " Tech Sq.");
				if (strlen(pers->office) <= 0)
					strcpy(pers->office, &buffer[0]);
			} else if (buffer[len - 1] == MIT) {
				strcpy(&buffer[len - 1], " MIT");
				if (strlen(pers->office) <= 0)
					strcpy(pers->office, &buffer[0]);
			} else {
				/*
				 * hope that the first field is indeed the office 
				 */
				if (strlen(pers->office) <= 0)
					strcpy(pers->office, &buffer[0]);
			}
			if (*gp++ == COMMA) {	/* office phone, theoretically */
				bp = &buffer[0];
				alldigits = 1;
				while ((*gp != NULL) && (*gp != COMMA)) {
					*bp = *gp++;
					alldigits = alldigits && ('0' <= *bp) && (*bp <= '9');
					bp++;
				}
				*bp = NULL;
				len = strlen(&buffer[0]);
				if (alldigits) {
					if (len != 4) {
						if ((len == 7) || (len == 10)) {
							pers->homephone = phone(&buffer[0], len);
						} else {
							pers->random = malloc(len + 1);
							strcpy(pers->random, &buffer[0]);
						}
					} else {
						pers->officephone = phone(&buffer[0], len);
					}
				} else {
					pers->random = malloc(len + 1);
					strcpy(pers->random, &buffer[0]);
				}
				if (*gp++ == COMMA) {	/* home phone?? */
					bp = &buffer[0];
					alldigits = 1;
					while ((*gp != NULL) && (*gp != COMMA)) {
						*bp = *gp++;
						alldigits = alldigits && ('0' <= *bp) && (*bp <= '9');
						bp++;
					}
					*bp = NULL;
					len = strlen(&buffer[0]);
					if (alldigits && ((len == 7) || (len == 10))) {
						if (*pers->homephone != NULL) {
							pers->officephone = pers->homephone;
						}
						pers->homephone = phone(&buffer[0], len);
					} else {
						pers->random = malloc(strlen(&buffer[0]) + 1);
						strcpy(pers->random, &buffer[0]);
					}
				}
			}
		}
		if (pers->loggedin == NO_LOGIN) {
			findwhen(pers);
		} else
#ifdef FTU
		if (pers->loggedin != FILE_LOGIN)
#endif	/* FTU */
		{
			findidle(pers);
		}
	}
}

/*
 *  very hacky section of code to format phone numbers.  filled with
 *  magic constants like 4, 7 and 10.
 */
char *
phone(s, len)
	char *s;
	int len;
{
	char fonebuf[15];
	int i;

	switch (len) {

	case 4:
		fonebuf[0] = ' ';
		fonebuf[1] = 'x';
		fonebuf[2] = '3';
		fonebuf[3] = '-';
		for (i = 0; i <= 3; i++) {
			fonebuf[4 + i] = *s++;
		}
		fonebuf[8] = NULL;
		return (StrCpy(&fonebuf[0]));
		break;

	case 7:
		for (i = 0; i <= 2; i++) {
			fonebuf[i] = *s++;
		}
		fonebuf[3] = '-';
		for (i = 0; i <= 3; i++) {
			fonebuf[4 + i] = *s++;
		}
		fonebuf[8] = NULL;
		return (StrCpy(&fonebuf[0]));
		break;

	case 10:
		for (i = 0; i <= 2; i++) {
			fonebuf[i] = *s++;
		}
		fonebuf[3] = '-';
		for (i = 0; i <= 2; i++) {
			fonebuf[4 + i] = *s++;
		}
		fonebuf[7] = '-';
		for (i = 0; i <= 3; i++) {
			fonebuf[8 + i] = *s++;
		}
		fonebuf[12] = NULL;
		return (StrCpy(&fonebuf[0]));
		break;

	default:
		fprintf(stderr, "finger: error in phone numbering\r\n");
		return (StrCpy(s));
		break;
	}
}

findwhen(pers)			/* find last login from the LASTLOG file */
	struct person *pers;
{
	struct passwd *pwdt = pers->pwd;
	struct lastlog ll;
	int llsize = sizeof ll;
	int i;

	if (!llopenerr) {
		lseek(lf, pwdt->pw_uid * llsize, 0);
		if ((i = read(lf, (char *)&ll, llsize)) == llsize) {
			for (i = 0; i < LMAX; i++) {
				pers->tty[i] = ll.ll_line[i];
			}
			pers->tty[LMAX] = NULL;
			pers->loginat = ll.ll_time;
		} else {
			if (i != 0)
				fprintf(stderr, "finger: lastlog read error\r\n");
			pers->tty[0] = NULL;
			pers->loginat = 0L;
		}
	} else {
		pers->tty[0] = NULL;
		pers->loginat = 0L;
	}
}

/*  find the idle time of a user by doing a stat on /dev/histty,
 *  where histty has been gotten from USERLOG, supposedly.
 */

findidle(pers)
	struct person *pers;
{
	struct stat ttystatus;
	struct passwd *pwdt = pers->pwd;
	char buffer[20];
	char *TTY = "/dev/";
	int TTYLEN = strlen(TTY);
	int i;

	strcpy(&buffer[0], TTY);
	i = 0;
	do {
		buffer[TTYLEN + i] = pers->tty[i];
	} while (++i <= LMAX);
	if (stat(&buffer[0], &ttystatus) >= 0) {
		time(&tloc);
		if (tloc < ttystatus.st_atime) {
			pers->idletime = 0L;
		} else {
			pers->idletime = tloc - ttystatus.st_atime;
		}
		if ((ttystatus.st_mode & TALKABLE) == TALKABLE) {
			pers->writeable = 1;
		} else {
			pers->writeable = 0;
		}
	} else {
		fprintf(stderr, "finger: error STATing %s\r\n", &buffer[0]);
		exit(4);
	}
}


/*  print idle time in short format; this program always prints 4 characters;
 *  if the idle time is zero, it prints 4 blanks.
 */

stimeprint(dt)
	long *dt;
{
	struct tm *gmtime();
	struct tm *delta;

	delta = gmtime(dt);
	if (delta->tm_yday == 0) {
		if (delta->tm_hour == 0) {
			if (delta->tm_min >= 10) {
				printf(" %2.2d ", delta->tm_min);
			} else {
				if (delta->tm_min == 0) {
					printf("    ");
				} else {
					printf("  %1.1d ", delta->tm_min);
				}
			}
		} else {
			if (delta->tm_hour >= 10) {
				printf("%3.3d:", delta->tm_hour);
			} else {
				printf("%1.1d:%02.2d", delta->tm_hour, delta->tm_min);
			}
		}
	} else {
		printf("%3dd", delta->tm_yday);
	}
}


/*  print idle time in long format with care being taken not to pluralize
 *  1 minutes or 1 hours or 1 days.
 */

ltimeprint(dt)
	long *dt;
{
	struct tm *gmtime();
	struct tm *delta;
	int printed = 1;

	delta = gmtime(dt);
	if (delta->tm_yday == 0) {
		if (delta->tm_hour == 0) {
			if (delta->tm_min >= 10) {
				printf("%2d minutes", delta->tm_min);
			} else {
				if (delta->tm_min == 0) {
					if (delta->tm_sec > 10) {
						printf("%2d seconds", delta->tm_sec);
					} else {
						printed = 0;
					}
				} else {
					if (delta->tm_min == 1) {
						if (delta->tm_sec == 1) {
							printf("%1d minute %1d second", delta->tm_min, delta->tm_sec);
						} else {
							printf("%1d minute %d seconds", delta->tm_min, delta->tm_sec);
						}
					} else {
						if (delta->tm_sec == 1) {
							printf("%1d minutes %1d second", delta->tm_min, delta->tm_sec);
						} else {
							printf("%1d minutes %d seconds", delta->tm_min, delta->tm_sec);
						}
					}
				}
			}
		} else {
			if (delta->tm_hour >= 10) {
				printf("%2d hours", delta->tm_hour);
			} else {
				if (delta->tm_hour == 1) {
					if (delta->tm_min == 1) {
						printf("%1d hour %1d minute", delta->tm_hour, delta->tm_min);
					} else {
						printf("%1d hour %2d minutes", delta->tm_hour, delta->tm_min);
					}
				} else {
					if (delta->tm_min == 1) {
						printf("%1d hours %1d minute", delta->tm_hour, delta->tm_min);
					} else {
						printf("%1d hours %2d minutes", delta->tm_hour, delta->tm_min);
					}
				}
			}
		}
	} else {
		if (delta->tm_yday >= 10) {
			printf("%2d days", delta->tm_yday);
		} else {
			if (delta->tm_yday == 1) {
				if (delta->tm_hour == 1) {
					printf("%1d day %1d hour", delta->tm_yday, delta->tm_hour);
				} else {
					printf("%1d day %2d hours", delta->tm_yday, delta->tm_hour);
				}
			} else {
				if (delta->tm_hour == 1) {
					printf("%1d days %1d hour", delta->tm_yday, delta->tm_hour);
				} else {
					printf("%1d days %2d hours", delta->tm_yday, delta->tm_hour);
				}
			}
		}
	}
	return (printed);
}

matchcmp(gname, login, given)
	char *gname;
	char *login;
	char *given;
{
	char buffer[20];
	char c;
	int flag, i, unfound;

	if (!match)
		return (0);
	if (namecmp(login, given))
		return (1);
	if (*gname == '\0')
		return (0);

	if (*gname == ASTERISK) {
		gname++;
	}
	flag = 1;
	i = 0;
	unfound = 1;
	while (unfound) {
		if (flag) {
			c = *gname++;
			if (c == SAMENAME) {
				flag = 0;
				c = *login++;
			} else {
				unfound = (*gname != COMMA) && (*gname != NULL);
			}
		} else {
			c = *login++;
			if (c == NULL) {
				if ((*gname == COMMA) || (*gname == NULL)) {
					break;
				} else {
					flag = 1;
					continue;
				}
			}
		}
		if (c == BLANK) {
			buffer[i++] = NULL;
			if (namecmp(buffer, given)) {
				return (1);
			}
			i = 0;
			flag = 1;
		} else {
			buffer[i++] = c;
		}
	}
	buffer[i++] = NULL;

	if (namecmp(buffer, given))
		return (1);
	return (0);
}

namecmp(name1, name2)
	char *name1;
	char *name2;
{
	char c1, c2;

	c1 = *name1;
	if ((('A' <= c1) && (c1 <= 'Z')) || (('a' <= c1) && (c1 <= 'z'))) {
		c1 = CAPITALIZE(c1);
	}
	c2 = *name2;
	if ((('A' <= c2) && (c2 <= 'Z')) || (('a' <= c2) && (c2 <= 'z'))) {
		c2 = CAPITALIZE(c2);
	}
	while (c1 == c2) {
		if (c1 == NULL) {
			return (1);
		}
		c1 = *++name1;
		if ((('A' <= c1) && (c1 <= 'Z')) || (('a' <= c1) && (c1 <= 'z'))) {
			c1 = CAPITALIZE(c1);
		}
		c2 = *++name2;
		if ((('A' <= c2) && (c2 <= 'Z')) || (('a' <= c2) && (c2 <= 'z'))) {
			c2 = CAPITALIZE(c2);
		}
	}
	if (*name1 == NULL) {
		while (('0' <= *name2) && (*name2 <= '9')) {
			name2++;
		}
		if (*name2 == NULL) {
			return (1);
		}
	} else {
		if (*name2 == NULL) {
			while (('0' <= *name1) && (*name1 <= '9')) {
				name1++;
			}
			if (*name1 == NULL) {
				return (1);
			}
		}
	}
	return (0);
}

/*
nfinger first calls netfinger, and if that fails it tries
chfinger.  It saves error messages until it has tried them
both, and if neither connection gets through it prints
whichever one it feels is more appropriate.
- BCN 13-Jan-85
*/
nfinger(name)
	char *name;
{
	char errms1[100];	/* Max length of an error message      */
	char errms2[100];	/* Similarly, for the internet routine */
	char cpnm[100];		/* The host name gets bashed           */
	int rcd1, rcd2;		/* The return codes 1=Chaos, 2=Int     */

	strcpy(cpnm, name);	/* Save the host name                  */
#ifdef CHAOS
	rcd1 = chfinger(name, errms1, 0);
	if (rcd1 >= 2) {
		if ((rcd2 = netfinger(cpnm, errms2)) >= 2)
			if (rcd1 == 2) {
				printf("%s", errms2);
				return (rcd2);
			} else if ((rcd1 == 4) && ((rcd1 = chfinger(name, errms1, 1)) < 2));
			else if (rcd2 == 2) {
				printf("%s", errms1);
				return (rcd1);
			} else {
				printf("Internet: %s\r\n   Chaos: %s", errms1, errms2);
				return (rcd1);
			}
	} else
		return (rcd1);
#else
	rcd2 = netfinger(name, errms2);
	if (rcd2 >= 2)
		printf("%s", errms1);
	return (rcd2);
#endif	/* CHAOS */
}

netfinger(name, errmes)
	char *errmes;
	char *name;
{
	char *host;
	char fname[100];
	struct hostent *hp;
	struct servent *sp;
	struct sockaddr_in sin;
	int s;
	char *rindex();
	register FILE *f;
	register int c;
	register int lastc;

	if (name == NULL)
		return (0);
	host = rindex(name, '@');
	if (host == NULL) {
		host = rindex(name, '%');
		if (host != NULL)
			*host = '@';
		else
			return (0);
	}
	*host++ = 0;
	hp = gethostbyname(host);
	if (hp == NULL) {
		static struct hostent def;
		static struct in_addr defaddr;
		static char namebuf[128];
		int inet_addr();

		defaddr.s_addr = inet_addr(host);
		if (defaddr.s_addr == -1) {
			sprintf(errmes, "unknown host: %s\r\n", host);
			return (2);
		}
		strcpy(namebuf, host);
		def.h_name = namebuf;
		def.h_addr = (char *)&defaddr;
		def.h_length = sizeof(struct in_addr);
		def.h_addrtype = AF_INET;
		def.h_aliases = 0;
		hp = &def;
	}
	printf("[%s]", hp->h_name);
	sp = getservbyname("finger", "tcp");
	if (sp == 0) {
		sprintf(errmes, "tcp/finger: unknown service\r\n");
		return (3);
	}
	sin.sin_family = hp->h_addrtype;
	bcopy(hp->h_addr, (char *)&sin.sin_addr, hp->h_length);
	sin.sin_port = sp->s_port;
	s = socket(hp->h_addrtype, SOCK_STREAM, 0);
	if (s < 0) {
		fflush(stdout);
		perror("socket");
		return (4);
	}
	if (connect(s, (char *)&sin, sizeof(sin)) < 0) {
		fflush(stdout);
		perror("connect");
		close(s);
		return (5);
	}
	printf("\r\n");
	if (large)
		write(s, "/W ", 3);
	write(s, name, strlen(name));
	write(s, "\r\n", 2);
	f = fdopen(s, "r");
	while ((c = getc(f)) != EOF) {
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
		putchar(lastc = c);
	}
	if (lastc != '\n')
		putchar('\n');
	return (1);
}

#ifdef CHAOS

chfinger(name, errmes, gw_int)
	char *name;
	char *errmes;
	int gw_int;		/* Gateway through MC if necessary */
{
	register FILE *f;
	register char *host, *fname, *cname;
	register int c, mode;
	char contact[50];
	int address;
	char *rindex();
	int lastc;

	if (name == 0)
		return (0);
	host = rindex(name, '@');
	if (host == 0)
		return (0);
	*host++ = 0;		/* split into host, name */
	fname = host_name(host);
	if (fname == 0) {
		sprintf(errmes, "unknown host %s\r\n", host);
		return (2);
	}
	address = chaos_addr(host, 0);
	mode = 0;		/* reading */
	if (address != 0) {
		cname = "NAME";
		if (large)
			sprintf(contact, "%s/W", name);
		else
			strcpy(contact, name);
	} else {
		if (gw_int) {
			sprintf(contact, "%s 117", fname);
			address = chaos_addr("mit-mc", 0);
			fixhostname(contact);
			mode = 2;	/* have to write to the ARPA side */
			cname = "TCP";
		} else
			return (4);	/* We know about it, but its internet */
	}
	printf("[%s]\r\n", fname);
	fflush(stdout);
	if ((c = chopen(address, cname, mode, 0, contact, 0, 0)) < 0) {
		sprintf(errmes, "host %s not responding\r\n", fname);
		return (3);
	}
	if (mode == 2) {
		if (large)
			write(c, "/W ", 3);
		write(c, name, strlen(name));
		write(c, "\r\n", 2);
	}
	f = fdopen(c, "r");
	while ((c = getc(f)) != EOF) {
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
		putchar(lastc = c);
	}
	if (lastc != '\n')
		putchar('\n');
	return (1);
}

#include <ctype.h>

/*
 * convert the Arpanet host name to upper case
 */
fixhostname(s)
	register char *s;
{
	while (*s != ' ') {
		if (islower(*s))
			*s = toupper(*s);
		s++;
	}
}
#endif	/* CHAOS */

#ifdef TTYPLACE
#define LINE		120
struct ttywhere_s {
	char tty[20];
	char where[LINE];
} ttyw_s[256];

char *
ttywhere(tty)			/* find location of a tty */
	char *tty;
{
	static struct ttywhere_s *tw;
	char ln[LINE], *s, *t;
	FILE *f1, *fopen();

	/*
	 * on first entry ... Cache the information
	 */
	if (!tw) {
		if (!(f1 = fopen(TTYPLACE, "r"))) {
			fprintf(stderr, "finger: can't open %s\n", TTYPLACE);
			return ("");
		}
		for (tw = ttyw_s; fgets(ln, LINE, f1);) {
			if (ln[0] == '#')
				continue;	/* comment lines */
			if (ln[0] == '\n')
				continue;	/* Blank lines */
			/*
			 * copy the TTY portion (to the first white space)
			 */
			for (s = ln, t = tw->tty; *s; *t++ = *s++)
				if (*s == ' ' || *s == '\t')
					break;
			*t = NULL;
			/*
			 * SKIP to first non white space
			 */
			for (; *s; s++)
				if (*s != ' ' && *s != '\t')
					break;
			/*
			 * copy the WHERE portion
			 */
			for (t = tw->where; *s; *t++ = *s++)
				if (*s == '\n')
					break;
			*t = NULL;
			tw++;
		}
		*tw->tty = NULL;
		fclose(f1);
	} else
		tw = ttyw_s;	/* ~rewind */

	for (tw = ttyw_s; *tw->tty; tw++)
		if (!strcmp(tty, tw->tty))
			return (tw->where);	/* found */

	return ("");		/* not found */
}
#endif	/* TTYPLACE */
