/*
 *	Xperimental program for printing routing table
 *	(sned)
 */
#include <stdio.h>
#include <ctype.h>
#include <nlist.h>
#include <hosttab.h>
#ifdef vax
#define VMUNIX
#include <sys/chaos.h>
#include "/sys/chunix/chsys.h"
#include "/sys/chunix/chconf.h"
#include "/sys/chncp/chncp.h"
#endif
#include <sys/tty.h>
/*
 * Chaos status display
 */

/*
 * First, the appropriate global symbols.
 */
struct nlist namelist[] = {
#define aconntab	0
	{"_Chconntab"},
#define aclock		1
	{"_Chclock"},
#define amyaddr		2
	{"_Chmyaddr"},
#define aroutetab	3
	{"_Chroutetab"},
#define adr11cxcvr	4
	{"_dr11cxcvr"},
#define adr11chosts	5
	{"_dr11chosts"},
#define alsnlist	6
	{"_Chlsnlist"},
#define ach11xcvr	7
	{"_ch11xcvr"},
#define arfclist	8
	{"_Chrfclist"},
#define achhz		9
	{"_Chhz"},
#define achtty		10
	{"_cht_tty"},
#define MAXSYMBOLS	11
	{""},
};

#ifdef vax
char *symfile = "/vmunix";
#else
char *symfile = "/unix";
#endif
char *memfile = "/dev/kmem";
int mem;
struct {
	caddr_t kaddr[MAXSYMBOLS];
} info;
struct connection *Chconntab[CHNCONNS];
union {
	char p_c[CHHEADSIZE + CHMAXDATA];
	struct packet p_pkt;
} pktbuf;
struct packet *Chlsnlist;
struct packet *Chrfclist;
struct chroute Chroutetab[CHNSUBNET];
short Chmyaddr;
chtime Chclock;
int Chhz;

#if NCHDR > 0
struct chxcvr dr11cxcvr[NCHDR];
short dr11chosts[NCHDR];
#endif
#if NCHCH > 0
struct chxcvr ch11xcvr[NCHCH];
#endif
char *statename[] = {
	"Cls",
	"Lst",
	"RFR",
	"RFS",
	"Opn",
	"Los",
	"Inc",
};

#define MAXSTATE 6

char *myprefix;
int mypreflen;
int vflag, data;
int ndirect = 0;
struct packet *prpkt();

main(argc, argv)
	int argc;
	char **argv;
{
	register int i;
	register struct chxcvr *xp;
	register struct host *hp;
	char **ap, *cp;

	nice(-20);
	myprefix = host_me();
	for (cp = myprefix; *cp && *cp != '-'; cp++);
	if (*cp == '-') {
		*++cp = '\0';
		mypreflen = strlen(myprefix);
	}
	for (ap = &argv[1]; *ap; ap++)
		if (**ap == '-')
			switch (ap[0][1]) {
			case 'v':
				vflag++;
				break;
			case 'd':
				data++;
				break;
			case 's':
				symfile = &ap[0][2];
				break;
			case 'm':
				memfile = &ap[0][2];
				break;
			default:
				prexit("Unknown flag: %s\n", *ap);
			}
	if ((mem = open(memfile, 0)) < 0)
		prexit("Can't read memory file\n");
	if ((i = open(symfile, 0)) < 0)
		prexit("Can't read symbol file\n");
	close(i);
	nlist(symfile, namelist);
	if (namelist[0].n_value == -1)
		prexit("%a: cannot read symbol file: %s\n", symfile);
	for (i = 0; i < MAXSYMBOLS; i++)
		info.kaddr[i] = (caddr_t)namelist[i].n_value;
	lseek(mem, (long)info.kaddr[amyaddr], 0);
	read(mem, &Chmyaddr, sizeof(Chmyaddr));
	lseek(mem, (long)info.kaddr[aroutetab], 0);
	read(mem, Chroutetab, sizeof(Chroutetab));

	printf("Myaddr is 0%o\n", Chmyaddr);
	printf("%6s %7s %5s %4s\n", "Subnet", "addr", "via", "cost");
	for (i = 0; i < CHNSUBNET; i++) {
		if (Chroutetab[i].rt_type == CHNOPATH)
			continue;
		else if (Chroutetab[i].rt_type == CHDIRECT) {
			ndirect++;
			continue;
		}
		printf("%5o: ", i);
		printf("%7o ", Chroutetab[i].rt_addr);
		printf("%5o ", Chroutetab[i].rt_subnet);
		printf("%4d  ", Chroutetab[i].rt_cost);
		if (cp = chaos_name(Chroutetab[i].rt_addr))
			printf("%s\n", cp);
		else
			printf("\n");
	}
	if (ndirect == 0)
		exit();
	printf("Direct Connections to:\n");
	for (i = 0; i < CHNSUBNET; i++)
		if (Chroutetab[i].rt_type == CHDIRECT) {
			printf("%5o: ", i);
			printf("Direct at locn ");
			printf("%12O\n", Chroutetab[i].rt_xcvr);
			continue;
		}
}

prexit(s, n)
	char *s;
	int n;
{
	printf(s, n);
	exit(1);
}
