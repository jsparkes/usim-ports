#include "../h/chaos.h"
#include "../netchaos/chunix/chsys.h"
#include "../netchaos/chunix/chconf.h"
#include "../netchaos/chncp/chncp.h"
#include "../netchaos/chunix/challoc.h"
#include "../h/mbuf.h"
#include "../h/errno.h"
#include "../h/socket.h"
#include "../net/if.h"
#include "../netinet/in.h"
#include "../netinet/if_ether.h"

/*
 * This file provides the glue between the 4.2 BSD ethernet device drivers and
 * the Chaos NCP.
 */

struct chxcvr chetherxcvr[NCHETHER];

#define ELENGTH	sizeof(etherbroadcastaddr)
#define NPAIRS	20		/* how many addresses should we remember */
struct ar_pair {
	chaddr arp_chaos;
	unsigned char arp_ether[6];
	long arp_time;
} charpairs[NPAIRS];
long charptime = 1;		/* LRU clock for ar_pair slots */

/*
 * Process an incoming address resolution packet for the Chaosnet.
 * Stay clear of chaos packet buffers altogether.
 *
 * The defines below kludge around the krufty IP specific definitions
 * of the address resolution formats.
 */
#define arp_scha(arp)	((chaddr *)(arp)->arp_spa)->ch_addr
#define arp_tcha(arp)	((chaddr *)&(arp)->arp_tpa[-2])->ch_addr
#define arp_tea(arp)	((arp)->arp_tha - 2)

void
charpin(struct mbuf *m, struct arpcom *ac)
{
	struct ether_arp *arp = mtod(m, struct ether_arp *);
	short mychaddr;

	if (m->m_len < sizeof(struct ether_arp) || ntohs(arp->arp_hrd) != ARPHRD_ETHER || ntohs(arp->arp_pro) != ETHERTYPE_CHAOS || arp->arp_pln != sizeof(chaddr) || arp->arp_hln != ELENGTH || arp_scha(arp) == 0 || arp_tcha(arp) == 0)
		goto freem;
	{
		struct chxcvr *xp;

		for (xp = chetherxcvr; xp->xc_etherinfo.che_arpcom != ac; xp++)
			if (xp == &chetherxcvr[NCHETHER - 1])
				goto freem;
		mychaddr = xp->xc_addr;
	}
	{
		struct ar_pair *app;
		struct ar_pair *nap = charpairs;
		unsigned char *eaddr = 0;

		for (app = nap; app < &charpairs[NPAIRS]; app++)
			if (arp_scha(arp) == app->arp_chaos.ch_addr) {
				eaddr = app->arp_ether;
				break;
			} else if (app->arp_time < nap->arp_time) {
				nap = app;
				if (app->arp_chaos.ch_addr == 0)
					break;
			}
		/*
		 * If we are already cacheing the senders addresses,
		 * update our cache with possibly new information.
		 */
		if (eaddr) {
			nap = app;
			bcopy(arp->arp_sha, app->arp_ether, ELENGTH);
		}
		if (arp_tcha(arp) != mychaddr)
			goto freem;
		/*
		 * If we have never heard of this host before, find
		 * a slot and remember him.
		 * We set the time to 1 to be better than empty entries
		 * but worse than any that have actually been used.
		 * This is to prevent other hosts from flushing our cache
		 */
		if (eaddr == 0) {
			bcopy(arp->arp_sha, nap->arp_ether, ELENGTH);
			nap->arp_chaos.ch_addr = arp_scha(arp);
			nap->arp_time = 1;
		}
	}
	if (ntohs(arp->arp_op) == ARPOP_REQUEST) {
		struct sockaddr sa;
		struct ether_header *eh;

		arp_tcha(arp) = arp_scha(arp);
		arp_scha(arp) = mychaddr;
		eh = (struct ether_header *)sa.sa_data;
		bcopy(arp->arp_sha, eh->ether_dhost, ELENGTH);
		bcopy(arp->arp_sha, arp_tea(arp), ELENGTH);
		bcopy(ac->ac_enaddr, arp->arp_sha, ELENGTH);
		arp->arp_op = htons(ARPOP_REPLY);
		eh->ether_type = ETHERTYPE_ARP;	/* if_output will swap it! */
		sa.sa_family = AF_UNSPEC;
		(*ac->ac_if.if_output) (&ac->ac_if, m, &sa);
		return;
	}
      freem:
	m_freem(m);
}

/*
 * Output a chaos packet on a 4.2 BSD ethernet driver.
 * Note that 4.2BSD cannot support the notion of putting something
 * at the head of the transmit queue, so we ignore this.
 */
void
cheoutput(struct chxcvr *xcvr, struct packet *pkt, int head)
{
	struct arpcom *ac = xcvr->xc_etherinfo.che_arpcom;
	struct mbuf *m = 0;
	int resolving = 1;
	struct sockaddr sa;
	struct ether_header *eh;

	xcvr->xc_xmtd++;
	eh = (struct ether_header *)sa.sa_data;
	sa.sa_family = AF_UNSPEC;
	MGET(m, M_DONTWAIT, MT_DATA);
	if (m == NULL) {
		printf("cheoutput: no mbufs, cant xmt pkt %X\n", pkt);
		xmitdone(pkt);
		return;
	}
	if (pkt->pk_xdest == 0) {
		eh->ether_type = ETHERTYPE_CHAOS;
		bcopy(etherbroadcastaddr, eh->ether_dhost, ELENGTH);
		resolving = 0;
	} else {
		struct ar_pair *app;

		for (app = charpairs; app < &charpairs[NPAIRS] && app->arp_chaos.ch_addr; app++)
			if (pkt->pk_xdest == app->arp_chaos.ch_addr) {
				app->arp_time = ++charptime;
				eh->ether_type = ETHERTYPE_CHAOS;
				bcopy(app->arp_ether, eh->ether_dhost, ELENGTH);
				resolving = 0;
				break;
			}
	}
	if (resolving) {
		struct ether_arp *arp;

		eh->ether_type = ETHERTYPE_ARP;
		bcopy(etherbroadcastaddr, eh->ether_dhost, ELENGTH);
		m->m_len = sizeof(struct ether_arp);
		m->m_off = MMAXOFF - sizeof(struct ether_arp);
		arp = mtod(m, struct ether_arp *);

		arp->arp_hrd = htons(ARPHRD_ETHER);
		arp->arp_pro = htons(ETHERTYPE_CHAOS);
		arp->arp_hln = ELENGTH;
		arp->arp_pln = sizeof(chaddr);
		arp->arp_op = htons(ARPOP_REQUEST);
		bcopy((caddr_t)ac->ac_enaddr, (caddr_t)arp->arp_sha, ELENGTH);
		arp_scha(arp) = xcvr->xc_addr;
		arp_tcha(arp) = pkt->pk_xdest;
		bcopy(etherbroadcastaddr, arp_tea(arp), ELENGTH);
	} else {
		struct mbuf *p = chdtom(pkt);

		m->m_len = pkt->pk_len + sizeof(struct pkt_header);
		if (p->m_off >= MSIZE) {
			m->m_off = (int)(&pkt->pk_phead) - (int)m;
			++mclrefcnt[mtocl(p)];
		} else {
			m->m_off = MMAXOFF - m->m_len;
			bcopy((char *)&pkt->pk_phead, mtod(m, char *), m->m_len);
		}
	}
	xmitdone(pkt);
	(*ac->ac_if.if_output) (&ac->ac_if, m, &sa);
}

/*
 * Receive a packet from a 4.2BSD network driver.
 * If we're lucky, this is fast, but many cases must be handled.
 * It is assumed that
 *  CHMAXDATA + sizeof(ncp_header) + sizeof(pkt_header) + MSIZE
 * is less than CLBYTES.
 */
void
chpktin(struct mbuf *m, struct arpcom *ac)
{
	struct pkt_header *php;
	int chlength;
	struct chxcvr *xp;

	for (xp = chetherxcvr; xp->xc_etherinfo.che_arpcom != ac; xp++)
		if (xp >= &chetherxcvr[NCHETHER - 1]) {
			m_freem(m);
			return;
		}
	IF_ADJ(m);
	/*
	 * First handle the easy cases.
	 */
	php = mtod(m, struct pkt_header *);

	chlength = php->ph_len + sizeof(struct pkt_header);
	if (m->m_len < sizeof(struct pkt_header) || chlength > CHMAXDATA + sizeof(struct pkt_header)) {
		m_freem(m);
		return;
	}
	if (m->m_next == 0) {
		/*
		 * Incoming packet is in a single mbuf.
		 */
		if (m->m_off < MSIZE)
			if (m->m_off >= MMINOFF + sizeof(struct ncp_header))
				m->m_off -= sizeof(struct ncp_header);
			else if (chlength <= MLEN - sizeof(struct ncp_header)) {
				bcopy((char *)php, &m->m_dat[sizeof(struct ncp_header)], chlength);
				m->m_off = MMINOFF;
			} else
				goto copyout;
		else {
			struct mbuf *mm;

			m->m_off = 0;
			MFREE(m, mm);
			m = (struct mbuf *)((int)php & ~CLOFSET);
			bcopy(php, (char *)m + MSIZE + sizeof(struct ncp_header), chlength);
			m->m_off = MSIZE;
		}
		m->m_len = chlength + sizeof(struct ncp_header);
	} else {
		char *cp;
		struct packet *pkt;
		int nbytes;

	      copyout:
		if ((pkt = (struct packet *)
			ch_alloc(chlength + sizeof(struct ncp_header), 1)
		    ) == NULL) {
			m_freem(m);
			return;
		}
		cp = (char *)&pkt->pk_phead;
		nbytes = chlength;
		do {
			struct mbuf *mp;

			if (nbytes > 0) {
				bcopy(mtod(m, char *), cp, MIN(m->m_len, nbytes));

				cp += m->m_len;
				nbytes -= m->m_len;
			}
			MFREE(m, mp);
			m = mp;
		} while (m);
		if (nbytes > 0) {
			ch_free((char *)pkt);
			return;
		}
		m = chdtom(pkt);
	}
	xp->xc_rpkt = mtod(m, struct packet *);

	rcvpkt(xp);
}

/*
 * Nothing happens at initialization time.
 */
void
cheinit(void)
{
}

void
chereset(void)
{
}

void
chestart(void)
{
}

/*
 * Handle the CHIOCETHER ioctl to assign a chaos address to an ethernet interface.
 * Note that all initialization is done here.
 */
int
cheaddr(char *addr)
{
	extern struct ifnet *ifunit();
	struct chether *che = (struct chether *)addr;
	struct ifnet *ifp = ifunit(che->ce_name);
	struct chxcvr *xp;

	if (ifp == 0)
		return ENXIO;
	for (xp = chetherxcvr; xp->xc_etherinfo.che_arpcom; xp++)
		if (xp->xc_etherinfo.che_arpcom == 0 || &xp->xc_etherinfo.che_arpcom->ac_if == ifp)
			break;
		else if (xp == &chetherxcvr[NCHETHER - 1])
			return EIO;
	xp->xc_etherinfo.che_arpcom = (struct arpcom *)ifp;
	xp->xc_addr = che->ce_addr;
	xp->xc_cost = CHCCOST;
	xp->xc_xmit = cheoutput;
	xp->xc_reset = chereset;
	xp->xc_start = chestart;
	printf("chether: %s enabled as Chaosnet address 0%o\n", ifp->if_name, che->ce_addr);
	chattach(xp);
	return 0;
}
