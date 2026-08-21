#include "../h/chaos.h"
#include "../chunix/chsys.h"
#include "../chunix/chconf.h"
#include "../chncp/chncp.h"
#include "../chncp/address-res.h"

/*
 * Chaos device driver for the interlan ethernet interface.
 *
 * This driver depends heavily on the fact that the device will
 * interrupt exactly once per receive buffer and once per command
 * regardless of how many times the CSR is read (and thus the
 * DONE bits cleared).
 * Until a lot of this chaos code is rewritten, it is too painful to DMA
 * directly into a packet buffer since the buffers must be > 512 bytes, even
 * for the smallest packet.  Thus we just have fixed buffers, and copy the data out.
 * For transmission, we do indeed DMA directly from packets.
 * Note that for the VAX we permanently allocate a buffered data path for both
 * transmit and receive (except for the 780 where receive BDP won't work).
 * NOTE!!! The size of this structure must not be an even multiple of 8 !!!
 * If it is then "buffer chaining" will occur!
 */

/*
 * Structure of an Ethernet header -- transmit format
 */
struct il_xheader {
	unsigned char ilx_dhost[6];	/* Destination Host */
	unsigned short ilx_type;	/* Type of packet */
};

union chilpkt {
	struct il_stats ilp_stat;
	struct {
		struct il_rheader ilp_Rhdr;
		union {
			struct ar_packet ilp_Arpkt;
			struct {
				struct pkt_header ilp_Chhead;
				char ilp_Chdata[CHMAXDATA];
			} ilp_Chpkt;
		} ilp_data;
		char ilp_crc[4];
	} ilp_edata;
};

#define ilp_arpkt	ilp_edata.ilp_data.ilp_Arpkt
#define ilp_chhead	ilp_edata.ilp_data.ilp_Chpkt.ilp_Chhead
#define ilp_chdata	ilp_edata.ilp_data.ilp_Chpkt.ilp_Chdata
#define ilp_chpkt	ilp_edata.ilp_data.ilp_Chpkt
#define ilp_rhdr	ilp_edata.ilp_Rhdr

void chilstart(struct chxcvr *xp);
void chilreset(struct chxcvr *xp);

#ifdef DEBUG
#define IF_DEBUG(x)
#else
#define IF_DEBUG(x)
#endif

#define splIL()		spl5()

/*
 * Address resolution data:
 * This should move to an "address resolution" file when more than one
 * Ethernet device is supported.
 */
#define ETHER_BROADCAST {-1, -1, -1, -1, -1, -1}
unsigned char ether_broadcast[6] = ETHER_BROADCAST;

#define NPAIRS	20		/* how many addresses should we remember */
struct ar_pair {
	chaddr arp_chaos;
	unsigned char arp_ether[6];
	long arp_time;
};
long arptime;			/* LRU clock for ar_pair slots */

/*
 * Canned request packet we send out when we don't have the ethernet address
 * of an outgoing packet.
 */
struct chilapkt {
	struct il_xheader ar_xhdr;
	struct ar_packet ar_pkt;
};

/*
 * Software state per interface.
 */
struct chilsoft {
	char il_unit;
#ifdef vax
	char il_ubanum;
	char il_rbdp;
	char il_xbdp;
	unsigned il_ruba;
	unsigned il_xuba;
#endif
	short il_oactive;	/* is output active? */
	short il_startrcv;	/* hang receive next chance */
	short il_scsr;
	unsigned char il_enaddr[6];	/* board's ethernet address */
	short il_doreply;	/* Reply buffer full */
	short il_replying;	/* Reply buffer being transmitted */
	struct ildevice *il_devaddr;
	struct chxcvr il_xcvr;
	struct ar_pair il_pairs[NPAIRS];
	struct ar_pair *il_epairs;
	struct il_xheader il_savex;	/* Yick. */
	union chilpkt il_rpkt;	/* DMA input buffer */
	struct chilapkt il_arrequest;	/* Address request buffer */
	struct chilapkt il_arreply;	/* Address reply buffer */
} chilsoft[NCHIL];

/*
 * Error codes
 */
char *ilerrs[] = {
	"success",		/* 0 */
	"success with retries",	/* 01 */
	"illegal command",	/* 02 */
	"inappropriate command",	/* 03 */
	"failure",		/* 04 */
	"buffer size exceeded",	/* 05 */
	"frame too small",	/* 06 */
	0,			/* 07 */
	"excessive collisions",	/* 010 */
	0,			/* 011 */
	"buffer alignment error",	/* 012 */
	0,			/* 013 */
	0,			/* 014 */
	0,			/* 015 */
	0,			/* 016 */
	"non-existent memory"	/* 017 */
};

char *ildiag[] = {
	"success",		/* 0 */
	"checksum error",	/* 1 */
	"NM10 dma error",	/* 2 */
	"transmitter error",	/* 3 */
	"receiver error",	/* 4 */
	"loopback failure",	/* 5 */
};

long chillost;
long chilarrp;
long chilarls;
long chilxstat;
long chilrst;
long chilrin;
long chilcin;
long chilar;
long chilxrq;
long chilxrp;
int chilnoerr;
int chilprpacket;

#ifdef vax
#include "../h/buf.h"
#include "../vaxuba/ubavar.h"
#include "../vax/pte.h"
#include "../vaxuba/ubareg.h"

/*
 * This stuff should be elsewhere, but isn't right now.
 */
#ifdef VAX780
#define UBAPURGEBITS UBADPR_BNE
#else
#define UBAPURGEBITS (UBADPR_PURGE|UBADPR_NXM|UBADPR_UCE)
#endif				/* VAX780 */

#define PURGEBDP(ubanum, bdpnum) \
	uba_hd[ubanum].uh_uba->uba_dpr[bdpnum] |= UBAPURGEBITS

/*
 * Autoconfiguration support for vax
 */
int chilprobe(caddr_t reg);
void chilattach(struct uba_device *ui);
void chilrint(int dev);
void chilcint(int dev);
struct uba_device *chilinfo[NCHIL];
unsigned short chilstd[] = { 0 };
struct uba_driver childriver = { chilprobe, 0, chilattach, 0, chilstd, "chil", chilinfo };

/*
 * Do an OFFLINE command to cause an interrupt for the
 * autoconfigure stuff.
 */
int
chilprobe(caddr_t reg)
{
	int br, cvec;		/* r11, r10 */
	struct ildevice *addr = (struct ildevice *)reg;
	int i;

#ifdef lint
	{
		int j = 0;

		br = 0;
		cvec = br;
		br = cvec;
		i = j;
		j = i;
		chilrint(0);
		chilcint(0);
	}
#endif
	addr->il_csr = ILC_OFFLINE | IL_CIE;
	DELAY(100000);
	i = addr->il_csr;	/* clear CDONE */
	if (cvec > 0 && cvec != 0x200)
		cvec -= 4;

	return (sizeof(struct ildevice));
}

/*
 * Attach interface
 */
void
chilattach(struct uba_device *ui)
{
	chilsoft[ui->ui_unit].il_ubanum = ui->ui_ubanum;
	chilsetup(ui->ui_unit, (struct ildevice *)ui->ui_addr);
}
#else				/* vax */
int chilndev = NCHIL;
int chiladdr[NCHIL] = { CHIL_ADDR };
#endif				/* vax */

/*
 * Do the run-time initialization of the given transceiver.
 * Reception is not enabled.
 */
void
chilsetup(int unit, struct ildevice *addr)
{
	struct chilsoft *sp = &chilsoft[unit];
	int s;

	sp->il_xcvr.xc_devaddr = (unsigned *)addr;	/* This indicates attachment! */
	sp->il_xcvr.xc_start = chilstart;
	sp->il_xcvr.xc_reset = chilreset;
	sp->il_xcvr.xc_xmit = chxmitpkt;
	sp->il_xcvr.xc_cost = CHCCOST;
	sp->il_xcvr.xc_ilinfo.il_soft = sp;
	sp->il_epairs = &sp->il_pairs[NPAIRS];
	sp->il_devaddr = addr;
	sp->il_unit = unit;
#ifdef vax
	sp->il_xuba = uballoc(sp->il_ubanum, (caddr_t)&sp->il_rpkt, sizeof(union chilpkt), UBA_CANTWAIT | UBA_NEEDBDP);
	sp->il_xbdp = (sp->il_xuba >> 28) & 0x0f;
	sp->il_ruba = uballoc(sp->il_ubanum, (caddr_t)&sp->il_rpkt, sizeof(union chilpkt),
#ifdef VAX780
	    UBA_CANTWAIT);
#else
	    UBA_CANTWAIT);
#endif
	sp->il_rbdp = (sp->il_ruba >> 28) & 0x0f;
#endif
	s = splIL();
	ilbusywait(sp, ILC_RESET, 0);	/* Initialize interface */
	addr->il_bar = sp->il_xuba & 0xffff;
	addr->il_bcr = sizeof(struct il_stats);
	ilbusywait(sp, ILC_STAT, (sp->il_ruba >> 2) & IL_EUA);
	if (sp->il_rbdp)
		PURGEBDP(sp->il_ubanum, sp->il_rbdp);
	splx(s);
	bcopy((caddr_t)sp->il_rpkt.ilp_stat.ils_addr, sp->il_enaddr, 6);
	bcopy(ether_broadcast, sp->il_arrequest.ar_xhdr.ilx_dhost, 6);
	sp->il_arrequest.ar_xhdr.ilx_type = ILADDR_TYPE;
	sp->il_arrequest.ar_pkt.ar_hardware = AR_ETHERNET;
	sp->il_arrequest.ar_pkt.ar_protocol = ILCHAOS_TYPE;
	sp->il_arrequest.ar_pkt.ar_hlength = 6;
	sp->il_arrequest.ar_pkt.ar_plength = 2;
	sp->il_arrequest.ar_pkt.ar_opcode = AR_REQUEST;
	bcopy(sp->il_enaddr, sp->il_arrequest.ar_pkt.ar_esender, 6);
	bcopy((unsigned char *)&sp->il_arrequest, (unsigned char *)&sp->il_arreply, sizeof(struct chilapkt));
	sp->il_arreply.ar_pkt.ar_opcode = AR_REPLY;

	printf("chil%d: ether addr %x %x %x %x %x %x module %s firmware %s\n", unit, sp->il_enaddr[0] & 0xff, sp->il_enaddr[1] & 0xff, sp->il_enaddr[2] & 0xff, sp->il_enaddr[3] & 0xff, sp->il_enaddr[4] & 0xff, sp->il_enaddr[5] & 0xff, sp->il_rpkt.ilp_stat.ils_module, sp->il_rpkt.ilp_stat.ils_firmware);
}

/*
 * chaos interlan initialization. Also does initialization to the static xcvr
 * structure since initialized unions don't work anyway.
 */
void
chilinit(void)
{
	int unit;

	for (unit = 0; unit < NCHIL; unit++) {
#ifndef vax
		chilsetup(unit, (struct ildevice *)chiladdr[unit]);
#endif				/* not vax */
		chilreset(&chilsoft[unit].il_xcvr);
	}
}

/*
 * Receive our chaosnet address
 */
int
chilseta(unsigned dev, unsigned short addr)
{
	struct chilsoft *sp;

	if (dev >= NCHIL)
		return 1;
	else {
		sp = &chilsoft[dev];
		if (sp->il_xcvr.xc_devaddr == 0)
			return 1;
		sp->il_xcvr.xc_addr = addr;
		if (sp->il_xcvr.xc_subnet >= CHNSUBNET)
			return 1;
		sp->il_arrequest.ar_pkt.ar_csender.ch_addr = addr;
		sp->il_arreply.ar_pkt.ar_csender.ch_addr = addr;
		printf("chil%d: enabled as Chaosnet address 0%o\n", dev, addr);
		chattach(&sp->il_xcvr);
		return 0;
	}
}

/*
 * Reset interface after UNIBUS reset.
 */
void
chilreset(struct chxcvr *xp)
{
	struct chilsoft *sp = xp->xc_ilinfo.il_soft;
	int s;

	if (sp == 0)
		return;
	s = splIL();
	ilbusywait(sp, ILC_RESET, 0);
	ilbusywait(sp, ILC_CLPBAK, 0);
	ilbusywait(sp, ILC_CLPRMSC, 0);
	ilbusywait(sp, ILC_CRCVERR, 0);
	ilbusywait(sp, ILC_FLUSH, 0);
	ilbusywait(sp, ILC_ONLINE, 0);
	chilrstart(sp);
	splx(s);
}

/*
 * Start output on an idle line
 */
void
chilstart(struct chxcvr *xp)
{
	struct chilsoft *sp = xp->xc_ilinfo.il_soft;

	if (sp->il_xcvr.xc_tpkt != NOPKT)
		panic("chilstart: already busy");
	if (sp->il_oactive == 0)
		chilcint(sp->il_unit);
}

/*
 * Command (usually transmit) done interrupt.
 */
void
chilcint(int dev)
{
	struct ildevice *addr;
	struct il_xheader *epkt;
	struct chilsoft *sp = &chilsoft[dev];
	struct packet *pkt;
	int elength;

	chilcin++;
	LOCK;
	addr = (struct ildevice *)sp->il_xcvr.xc_devaddr;
	sp->il_oactive = 0;
	sp->il_scsr = addr->il_csr;
	if (sp->il_xbdp)
		PURGEBDP(sp->il_ubanum, sp->il_xbdp);
	if (sp->il_replying) {
		sp->il_replying = 0;
		sp->il_doreply = 0;
	} else if ((pkt = sp->il_xcvr.xc_tpkt) != NOPKT) {
		if ((sp->il_scsr & IL_STATUS) > 1) {
			chilxstat = sp->il_scsr;
			sp->il_xcvr.xc_abrt++;
		} else {
			if (sp->il_scsr & IL_STATUS)
				sp->il_xcvr.xc_abrt++;
			sp->il_xcvr.xc_xmtd++;
		}
		bcopy(&sp->il_savex, (char *)&pkt->pk_phead - sizeof(struct il_xheader), sizeof(struct il_xheader));
		xmitdone(pkt);
	}
	/*
	 * If the last receive interrupt happened while a command was
	 * in progress, it couldn't prime the receive buffer then, so it
	 * set the flag telling us to do it here.
	 */
	if (sp->il_startrcv)
		chilrstart(sp);
	/*
	 * If last receive interrupt created an address resolution reply
	 * to send, send it rather than another data packet.
	 */
	if (sp->il_doreply) {
		sp->il_replying = 1;
		epkt = (struct il_xheader *)&sp->il_arreply;
		elength = sizeof(struct chilapkt);
		chilxrp++;
	} else if ((pkt = xmitnext(&sp->il_xcvr)) == NOPKT)
		return;
	else {
		epkt = (struct il_xheader *)
		    ((char *)&pkt->pk_phead - sizeof(struct il_xheader));
		elength = sizeof(struct il_xheader) + sizeof(struct pkt_header) + pkt->pk_len;
		bcopy((char *)epkt, (char *)&sp->il_savex, sizeof(struct il_xheader));
		if (pkt->pk_xdest == 0) {
			epkt->ilx_type = ILCHAOS_TYPE;
			bcopy(ether_broadcast, epkt->ilx_dhost, 6);
		} else {
			struct ar_pair *app;

			for (app = sp->il_pairs; app < sp->il_epairs && app->arp_chaos.ch_addr; app++)
				if (pkt->pk_xdest == app->arp_chaos.ch_addr) {
					app->arp_time = ++arptime;
					epkt->ilx_type = ILCHAOS_TYPE;
					bcopy(app->arp_ether, epkt->ilx_dhost, 6);
					app = 0;
					break;
				}
			if (app) {
				chilxrq++;
				/*
				 * We have no cached address translation -
				 * we must ask for one.
				 */
				epkt = (struct il_xheader *)&sp->il_arrequest;
				sp->il_arrequest.ar_pkt.ar_ctarget.ch_addr = pkt->pk_xdest;
				elength = sizeof(struct chilapkt);
			}
		}
	}
	sp->il_xuba = ubaremap(sp->il_ubanum, sp->il_xuba, (caddr_t)epkt);
	addr->il_bar = sp->il_xuba & 0xffff;
	addr->il_bcr = (elength + 1) & ~1;
	addr->il_csr = (sp->il_xuba >> 2) & IL_EUA | ILC_XMIT | IL_CIE | IL_RIE;
	sp->il_oactive++;
}

/*
 * Receiver interrupt
 */
void
chilrint(int dev)
{
	struct chilsoft *sp = &chilsoft[dev];
	struct packet *pkt;	/* shouldn't be reg on pdp11 */
	int elength;

	chilrin++;
	if (sp->il_rbdp)
		PURGEBDP(sp->il_ubanum, sp->il_rbdp);
	if (sp->il_rpkt.ilp_rhdr.ilr_status == 0377) {
		printf("chil%d: Receive interrupt with no packet!\n", dev);
		goto restart;
	}
	if (sp->il_rpkt.ilp_rhdr.ilr_status & 4) {
		sp->il_xcvr.xc_rej++;
		chillost++;
	}
	/*
	 * Data length is chaos packet size plus header (minus
	 * frame status and length == 4 plus 4 for the crc == 0!)
	 */
	elength = sp->il_rpkt.ilp_rhdr.ilr_length - sizeof(struct il_rheader);
	if (sp->il_rpkt.ilp_rhdr.ilr_status & 3) {
		struct il_rheader *r = &sp->il_rpkt.ilp_rhdr;

		if (r->ilr_status & 1)
			sp->il_xcvr.xc_crci++;
		if (r->ilr_status & 2)
			sp->il_xcvr.xc_leng++;
		if (!chilnoerr) {
			printf("chil%d: Bad frame. Status: %d\n", dev, r->ilr_status);
			printf("Source ");
			printf("%x.%x.%x.%x.%x.%x ", r->ilr_shost[0], r->ilr_shost[1], r->ilr_shost[2], r->ilr_shost[3], r->ilr_shost[4], r->ilr_shost[5]);
			printf("Destination ");
			printf("%x.%x.%x.%x.%x.%x ", r->ilr_dhost[0], r->ilr_dhost[1], r->ilr_dhost[2], r->ilr_dhost[3], r->ilr_dhost[4], r->ilr_dhost[5]);
			printf("Type %x\n", r->ilr_type);
			if ((chilprpacket++ & 0177) == 0)
				chilprbadpacket(sp);
		}
	} else if (sp->il_rpkt.ilp_rhdr.ilr_type == ILADDR_TYPE)
		chilrar(sp, &sp->il_rpkt.ilp_arpkt, elength);
	else if (sp->il_rpkt.ilp_rhdr.ilr_type == ILCHAOS_TYPE)
		if (elength < sizeof(struct pkt_header) || elength < sizeof(struct pkt_header) + sp->il_rpkt.ilp_chhead.ph_len)
			sp->il_xcvr.xc_leng++;
		else {
			if ((pkt = pkalloc((int)sp->il_rpkt.ilp_chhead.ph_len, 1)) == NOPKT)
				sp->il_xcvr.xc_rej++;
			else {
				bcopy((unsigned char *)&sp->il_rpkt.ilp_chpkt, (unsigned char *)&pkt->pk_phead, sizeof(struct pkt_header) + sp->il_rpkt.ilp_chhead.ph_len);
				sp->il_xcvr.xc_rcvd++;
				LOCK;
				sp->il_xcvr.xc_rpkt = pkt;
				rcvpkt(&sp->il_xcvr);
			}
		}
      restart:
	if (sp->il_oactive)
		sp->il_startrcv = 1;
	else
		chilrstart(sp);
}

void
chilrstart(struct chilsoft *sp)
{
	struct ildevice *addr = sp->il_devaddr;

	chilrst++;
	sp->il_rpkt.ilp_rhdr.ilr_status = 0177777;
	addr->il_bar = sp->il_ruba & 0xffff;
	addr->il_bcr = sizeof(union chilpkt);
	addr->il_csr = (sp->il_ruba >> 2) & IL_EUA | ILC_RCV | IL_RIE;
	while (!((sp->il_scsr = addr->il_csr) & IL_CDONE))
		/*
		 * Busy wait 
		 */ ;
	if (sp->il_scsr & IL_STATUS)
		printf("il%d: receive buffer error: %d\n", sp->il_unit, sp->il_scsr & IL_STATUS);
	sp->il_startrcv = 0;
}

/*
 * Receive an address resolution packet.
 */
void
chilrar(struct chilsoft *sp, struct ar_packet *arp, int length)
{
	struct ar_pair *app;
	struct ar_pair *nap;

	unsigned char *eaddr;

	chilar++;
	if (length < sizeof(struct ar_packet) || arp->ar_hardware != AR_ETHERNET || arp->ar_protocol != ILCHAOS_TYPE || arp->ar_plength != sizeof(chaddr) || arp->ar_csender.ch_addr == 0)
		return;
	eaddr = 0;
	nap = sp->il_pairs;
	for (app = nap; app < sp->il_epairs && app->arp_chaos.ch_addr; app++)
		if (arp->ar_csender.ch_addr == app->arp_chaos.ch_addr) {
			eaddr = app->arp_ether;
			break;
		} else if (app->arp_time < nap->arp_time)
			nap = app;
	if (app < sp->il_epairs)
		nap = app;
	/*
	 * If we are already cacheing the senders addresses,
	 * update our cache with possibly new information.
	 */
	if (eaddr)
		bcopy(arp->ar_esender, app->arp_ether, 6);
	if (arp->ar_ctarget.ch_addr != sp->il_xcvr.xc_addr)
		return;
	/*
	 * If we have never heard of this host before, find
	 * a slot and remember him.
	 * Note that we leave the time alone since we haven't used the entry.
	 * This is to prevent other hosts from flushing our cache
	 */
	if (eaddr == 0) {
		bcopy(arp->ar_esender, nap->arp_ether, 6);
		nap->arp_chaos.ch_addr = arp->ar_csender.ch_addr;
	}
	if (arp->ar_opcode == AR_REQUEST)
		if (sp->il_doreply)
			chilarls++;
		else {
			chilarrp++;
			sp->il_arreply.ar_pkt.ar_ctarget.ch_addr = arp->ar_csender.ch_addr;
			bcopy(arp->ar_esender, sp->il_arreply.ar_pkt.ar_etarget, 6);
			bcopy(arp->ar_esender, sp->il_arreply.ar_xhdr.ilx_dhost, 6);
			sp->il_doreply = 1;
			if (sp->il_oactive == 0)
				chilcint(sp->il_unit);
		}
}

#define MAXWAIT	1000000		/* on the order of 10 seconds */

/*
 * Perform a command and busy-wait for the interrupt.
 * Used when it's not safe to sleep.
 * This should never be called to receive a packet.
 */
void
ilbusywait(struct chilsoft *sp, int command, int bits)
{
	struct ildevice *addr = sp->il_devaddr;
	int csr, i;

	csr = addr->il_csr;
	IF_DEBUG(printf("il%d: busy wait %x csr %x bcr %x bar %x bits %x", sp->il_unit, command, csr & 0xffff, addr->il_bcr & 0xffff, addr->il_bar & 0xffff, bits));
	addr->il_csr = bits | command;
	for (i = 0; !((csr = addr->il_csr) & IL_CDONE); i++)
		if (i >= MAXWAIT) {
			printf("il%d: lost CDONE\n", sp->il_unit);
			return;
		}
	IF_DEBUG((csr & IL_STATUS) ? printf("%s\n", (command == ILC_DIAG || command == ILC_RESET ? ildiag : ilerrs)[csr & IL_STATUS])
	    : printf("done\n"));
}

void
chilprbadpacket(struct chilsoft *sp)
{
	unsigned short *ptr;
	int i, elength;

	elength = sp->il_rpkt.ilp_rhdr.ilr_length - sizeof(struct il_rheader);
	for (i = 0, ptr = (unsigned short *)&sp->il_rpkt.ilp_arpkt; i < elength; i += 2) {
		printf("0%o ", *ptr++);
		if ((i & 017) == 14)
			printf("\n");
	}
}
