#include <stdio.h>
#include <ctype.h>
#include <nlist.h>
#include <hosttab.h>
#include "../h/chaos.h"
#include "../chunix/chsys.h"
#include "../chunix/chconf.h"
#include "../chncp/chncp.h"
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

#if NCHDR > 0
char *drstate[] = {
	"Idle",
	"Syn1",
	"Syn2",
	"Cnt1",
	"Data",
	"Esc1",
	"Chck",
	"Done",
};
#endif
char *myprefix;
int mypreflen;
int vflag, data;
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
/*
	for (i = 0; i < MAXSYMBOLS; i++)
		if (namelist[i].n_value == 0)
			fprintf(stderr, "%a: symbol %s not found\n",
				namelist[i].n_name);
 */
	if (namelist[0].n_value == -1)
		prexit("%a: cannot read symbol file: %s\n", symfile);
	for (i = 0; i < MAXSYMBOLS; i++)
		info.kaddr[i] = (caddr_t)namelist[i].n_value;
	lseek(mem, (long)info.kaddr[aconntab], 0);
	read(mem, Chconntab, sizeof(Chconntab));
	lseek(mem, (long)info.kaddr[amyaddr], 0);
	read(mem, &Chmyaddr, sizeof(Chmyaddr));
	lseek(mem, (long)info.kaddr[aroutetab], 0);
	read(mem, Chroutetab, sizeof(Chroutetab));
	lseek(mem, (long)info.kaddr[achhz], 0);
	read(mem, Chhz, sizeof(Chhz));
	if (Chhz == 0)
		Chhz = 60;
#if NCHDR > 0
	lseek(mem, (long)info.kaddr[adr11cxcvr], 0);
	read(mem, dr11cxcvr, sizeof(dr11cxcvr));
	lseek(mem, (long)info.kaddr[adr11chosts], 0);
	read(mem, dr11chosts, sizeof(dr11chosts));
#endif
#if NCHCH > 0
	lseek(mem, (long)info.kaddr[ach11xcvr], 0);
	read(mem, ch11xcvr, sizeof(ch11xcvr));
#endif
	lseek(mem, (long)info.kaddr[alsnlist], 0);
	read(mem, &Chlsnlist, sizeof(Chlsnlist));
	lseek(mem, (long)info.kaddr[arfclist], 0);
	read(mem, &Chrfclist, sizeof(Chrfclist));

	/*
	 * Chclock should be read last! 
	 */
	lseek(mem, (long)info.kaddr[aclock], 0);
	read(mem, &Chclock, sizeof(Chclock));
	printf("Myaddr is 0%o", Chmyaddr);
	printf("\nConnections:\n # T St  Remote Host  Idx Idle Tlast Trecd Tackd Tw Rlast Rackd Rread Rw Flags\n");
	for (ap = argv, i = 0; *ap; ap++)
		if (isdigit(ap[0][0])) {
			int n = atoi(*ap);

			i++;
			if (Chconntab[n])
				prconn(Chconntab[n], n);
		}
	if (i == 0) {
		for (i = 0; i < CHNCONNS; i++)
			if (Chconntab[i] != NOCONN)
				prconn(Chconntab[i], i);
		if (Chrfclist)
			prrfcs();
#if NCHDR > 0
		prxcvr("DR11-C", dr11cxcvr, NCHDR);
#endif
#if NCHCH > 0
		prxcvr("CH11", ch11xcvr, NCHCH);
#endif
	}
}

static char *xstathead = " Received Transmitted CRC(xcvr) CRC(buff) Overrun Aborted Length Rejected";

prxcvr(name, xcvr, nx)
	char *name;
	struct chxcvr *xcvr;
	int nx;
{
	register struct chxcvr *xp = xcvr;
	register int i;

	for (i = 0; i < nx; i++, xp++) {
		printf("%-8s %2d Netaddr: ", name, i);
		praddr(xcvr->xc_addr);
		printf(" Devaddr: %x\n", xp->xc_devaddr);
		printf("%s\n%9D%12D%10D%8D%8D%8D%8D%8D\n", xstathead, xp->xc_rcvd, xp->xc_xmtd, xp->xc_crcr, xp->xc_crci, xp->xc_lost, xp->xc_abrt, xp->xc_leng, xp->xc_rej);
		printf(" Tpacket Ttime Rpacket Rtime");
#if NCHDR > 0
		printf(" Tcnt Tstate  Rcnt Rstate");
#endif
		printf("\n%6x ", xp->xc_tpkt);
		pridle(xp->xc_ttime);
		printf(" %6x ", xp->xc_rpkt);
		pridle(xp->xc_rtime);
#if NCHDR > 0
		printf("%5d %-6s", xp->xc_info.xc_dr11c.dr_tcnt, drstate[xp->xc_info.xc_dr11c.dr_tstate]);
		printf("%5d %-6s", xp->xc_info.xc_dr11c.dr_rcnt, drstate[xp->xc_info.xc_dr11c.dr_rstate]);
		printf("  %6x", xp->xc_info.xc_dr11c.dr_addr);
		if (xp->xc_info.xc_dr11c.dr_intrup)
			printf(" I");
#endif
		printf("\n");
	}
}

pridle(n)
	chtime n;
{
	register int idle;

	idle = Chclock - n;
	if (idle < 0)
		idle = 0;
	if (idle < Chhz)
		printf("%3dt", idle);
	else {
		idle += Chhz / 2;
		idle /= Chhz;
		if (idle < 100)
			printf("%3ds", idle);
		else if (idle < 60 * 99)
			printf("%3dm", (idle + 30) / 60);
		else
			printf("%3dh", (idle + 60 * 30) / (60 * 60));
	}
}

prhost(h, len)
	short h;
{
	register char *cp;
	extern char *chaos_name();

	if (cp = chaos_name(h)) {
		if (mypreflen && strncmp(cp, myprefix, mypreflen) == 0)
			cp += mypreflen;
		printf("%-*.*s", len, len, cp);
	} else
		praddr(h, len);
}

praddr(h, len)
	short h;
{
	printf("%02d.%03d%*s", ((chaddr *) & h)->ch_subnet & 0377, ((chaddr *) & h)->ch_host & 0377, len - 6, "");
}

prconn(conn, i)
	struct connection *conn;
{
	register struct packet *pkt;
	struct connection myconn;

	lseek(mem, (long)conn, 0);
	read(mem, &myconn, sizeof(myconn));
	printf("%2d %c %-3s ", i, myconn.cn_mode == CHTTY ?
#if NCHT > 0
	    chtty(myconn.cn_ttyp)
#else
	    ' '
#endif
	    : ' ', myconn.cn_state >= 0 && myconn.cn_state <= MAXSTATE ? statename[myconn.cn_state] : "???");
	prhost(myconn.cn_faddr, 12);
	printf(" ");
	pridx(myconn.cn_fidx);
	pridle(myconn.cn_active);
	printf("%6d%6d%6d%3d", myconn.cn_tlast, myconn.cn_trecvd, myconn.cn_tacked, myconn.cn_twsize);
	printf("%6d%6d%6d%3d", myconn.cn_rlast, myconn.cn_racked, myconn.cn_rread, myconn.cn_rwsize);
	if (myconn.cn_flags & CHEOFSEEN)
		printf(" ES");
	if (myconn.cn_flags & CHANSWER)
		printf(" ANS ");
	if (myconn.cn_sflags & CHIWAIT)
		printf(" IW");
	if (myconn.cn_sflags & CHOWAIT)
		printf(" OW");
	printf("\n");
	switch (myconn.cn_state) {
	case CSRFCSENT:
		if (myconn.cn_thead) {
			lseek(mem, (long)myconn.cn_thead, 0);
			read(mem, &pktbuf, sizeof(pktbuf));
			printf(" RFC:'%.*s'\n", pktbuf.p_pkt.pk_len, pktbuf.p_pkt.pk_cdata);
		} else
			printf("NO RFC!\n");
		break;
	case CSRFCRCVD:
		if (myconn.cn_rhead) {
			lseek(mem, (long)myconn.cn_rhead, 0);
			read(mem, &pktbuf, sizeof(pktbuf));
			printf(" RFC:'%.*s'\n", pktbuf.p_pkt.pk_len, pktbuf.p_pkt.pk_cdata);
		} else
			printf("NO RFC!\n");
		break;
	case CSLISTEN:
		for (pkt = Chlsnlist; pkt; pkt = pktbuf.p_pkt.pk_next) {
			lseek(mem, (long)pkt, 0);
			read(mem, &pktbuf, sizeof(pktbuf));
			if (pktbuf.p_pkt.pk_stindex == myconn.cn_ltidx) {
				printf("Contact: '%.*s'\n", pktbuf.p_pkt.pk_len, pktbuf.p_pkt.pk_cdata);
				break;
			}
		}
		if (pkt == NOPKT)
			printf("No packet on listen list!\n");
		break;
	}
	if (vflag) {
		if (myconn.cn_toutput) {
			printf("Output packet:\n");
			(void)prpkt(myconn.cn_toutput);
		}
		if (myconn.cn_thead) {
			printf("Transmit queue:\n");
			prq(myconn.cn_thead);
		}
		if (myconn.cn_rhead) {
			printf("Receive queue:\n");
			prq(myconn.cn_rhead);
		}
		if (myconn.cn_routorder) {
			printf("Out of order queue:\n");
			prq(myconn.cn_routorder);
		}
	}
}

chtty(addr)
	caddr_t addr;
{
	return addr == 0 ? '?' : "0123456789abcdef"[(addr - info.kaddr[achtty]) / sizeof(struct tty)];
}

prq(pkt)
	register struct packet *pkt;
{
	register struct packet *npkt;

	for (; pkt; pkt = npkt)
		npkt = prpkt(pkt);
}

struct packet *
prpkt(pkt)
	struct packet *pkt;
{
	register int i;
	register int length;

	lseek(mem, (long)pkt, 0);
	read(mem, &pktbuf, sizeof(pktbuf));
	length = pktbuf.p_pkt.pk_len;
	printf("\top: %3o\tpkn: %3u len:%d", pktbuf.p_pkt.pk_op, (unsigned)pktbuf.p_pkt.pk_pkn, length);
	pridle(pktbuf.p_pkt.pk_time);
	if (data) {
		printf(" \"");
		for (i = 0; i < length; i++) {
			int c = pktbuf.p_pkt.pk_cdata[i] & 0377;

			if (c < ' ')
				printf("[^%c]", c + '@');
			else if (c < 0177)
				putchar(c);
			else
				printf("[0%o]", c);
		}
		putchar('"');
	}
	printf("\n");
	return pktbuf.p_pkt.pk_next;
}

prrfcs()
{
	register struct packet *pkt;

	printf("Queued RFC's:\n Sourcehost  Sourceidx RFC string\n");
	for (pkt = Chrfclist; pkt; pkt = pktbuf.p_pkt.pk_next) {
		lseek(mem, (long)pkt, 0);
		read(mem, (char *)&pktbuf, sizeof(pktbuf));
		printf(" ");
		prhost(pktbuf.p_pkt.pk_saddr, 14);
		pridx(pktbuf.p_pkt.pk_sidx);
		printf(" %.*s", pktbuf.p_pkt.pk_len, pktbuf.p_pkt.pk_cdata);
		if (pktbuf.p_pkt.pk_ackn != 0)
			printf(" (skipped)");
		printf("\n");
	}
}

pridx(i)
{
	printf("%4x", ((chindex *) & i)->ci_idx);
}

prexit(a, b, c, d, e)
	char *a;
{
	fprintf(stderr, a, b, c, d, e);
	exit(1);
}
