/*
 * ChaosNet <=> Internet conversion driver.
 *
 * 		  (c) 1984 by the
 *	   Student Information Processing Board
 *		      of the 		
 *        Massachusetts Institute of Technology
 *
 * 	   All writes reserved (tee hee)
 *
 *	Written over the weekend of 03/17/84 by Jeffrey I. Schiller and
 *		Ramin Zabih of the SIPB.
 *
 *	Modified 05/25/84 Jis to not die when fed a dataless UNC packet.
 *
 */

#include "../h/chaos.h"
#include "../vax/mtpr.h"
#include "../chunix/chsys.h"
#include "../chunix/chconf.h"
#include "../chncp/chncp.h"
#include "../chunix/challoc.h"
#include "../h/mbuf.h"
#include "../h/errno.h"
#include "../h/socket.h"
#include "../net/if.h"
#include "../netinet/in.h"
#include "../netinet/if_ether.h"
#include "../h/time.h"
#include "../net/route.h"
#include "../net/netisr.h"
#include "../netinet/in_systm.h"
#include "../netinet/ip.h"
#include "../netinet/ip_var.h"
#include "../netinet/in_var.h"

/* Magic Numbers */

#define	IPACKN	2048

/* Static Data used by this "interface" */

struct arpcom ac_chip;

#define if_chip ac_chip.ac_if	/* ifnet structure with arp common */

void chipinit(int unit);
int chipioctl(struct ifnet *ifp, int cmd, caddr_t data);
int chipout(struct ifnet *ifp, struct mbuf *m0, struct sockaddr *dst);

/*
 * Attach routine called by the Chaos NCP during intialization, we
 * then arrange to announce to the IP layer that we exist.
 */
void
chipattach(void)
{
	struct ifnet *ifp = &if_chip;

	/*
	 * UNIT COULD BE AMBIGUOUS 
	 */
	ifp->if_unit = 0;
	ifp->if_name = "chi";
	ifp->if_mtu = 488;	/* RDZ: as per Hornig */
	ifp->if_reset = 0;	/* No reset routine, because we are not hardware based */
	ifp->if_init = chipinit;
	ifp->if_ioctl = chipioctl;
	ifp->if_output = chipout;
	ifp->if_flags = 0;	/* We are turned on after we get an address */
	if_attach(ifp);

	return;
}

void
chipinit(int unit)
{
	struct ifnet *ifp = &if_chip;

	/*
	 * not yet, if address still unknown 
	 */
	if (ifp->if_addrlist == (struct ifaddr *)0)
		return;

	if (ifp->if_flags & IFF_RUNNING)
		return;

	ifp->if_flags |= IFF_RUNNING;
	return;
}

/*
 * chipinput: We are called by the Chaos Code after he recognizes
 *  That a packet he has received is an UNC packet. We verify that
 *  the packet ack field is IPACKN. (Protocol = IP) and if not we discard
 *  the packet, otherwise we arrange to hand it off to the IP layer.
 */
void
chipinput(struct packet *pkt)
{
	struct ifqueue *inq;
	int chlength;
	int offset;
	int lentocopy;
	int s;
	struct mbuf *m;
	struct mbuf *topm;
	struct mbuf *upm;
	struct ifnet *ifp = &if_chip;

	s = splimp();
	ifp->if_ipackets++;
	topm = NULL;
	upm = NULL;
/*	chipdpkt (pkt, 0);		/* JIS DEBUG */

	/*
	 * Verify that the interface is logically up... 
	 */

	if ((ifp->if_flags & IFF_UP) == 0)
		goto flush_packet;

	/*
	 * Verify that we have an IP protocol encapulated packet 
	 */

	if (pkt->pk_ackn != IPACKN)
		goto flush_packet;

	chlength = pkt->pk_len;
	offset = 0;
	while (chlength > 0) {
		MGET(m, M_DONTWAIT, MT_DATA);
		if (m == NULL) {	/* If there isn't space, we lose. */
			ifp->if_ierrors++;	/* RDZ: Got an error */
		      flush_packet:
			ch_free((char *)pkt);	/* Flush... */
			if (topm != NULL)
				m_freem(topm);
			splx(s);
			return;
		}
		if (topm == NULL)
			topm = m;
		/*
		 * RDZ: No need for subroutine... 
		 */
		lentocopy = (chlength > MLEN ? MLEN : chlength);
		m->m_len = lentocopy;

		/*
		 * put interface pointer in the first mbuf of the chain 
		 */
		if (m == topm) {
			*(mtod(m, struct ifnet **)) = ifp;

			lentocopy -= sizeof ifp;
		}
		bcopy(&(pkt->pk_cdata[offset]), &m->m_dat[(m == topm) ? sizeof ifp : 0], lentocopy);

		if (upm != NULL) {
			upm->m_next = m;
			upm = m;
		} else {
			upm = m;
		}
		chlength = chlength - lentocopy;
		offset = offset + lentocopy;
	}
	ch_free((char *)pkt);	/* All done with the Chaos version. */
	if (topm == NULL) {	/* No IP datagram in this packet! */
		splx(s);	/* Restore priority */
		return;		/* All done... */
	}
	schednetisr(NETISR_IP);
	inq = &ipintrq;
	if (IF_QFULL(inq)) {	/* So now you tell me... */
		IF_DROP(inq);	/* ... */
		ifp->if_ierrors++;	/* RDZ: input error */
		m_freem(topm);	/* Give the memory back. */
		splx(s);	/* Restore priority */
	}
/*	ipdmp (topm);			/* JIS DEBUG */
	IF_ENQUEUE(inq, topm);
	splx(s);
}

union S_un {
	struct {
		unsigned char s_b1, s_b2, s_b3, s_b4;
	} S_un_b;
	struct {
		unsigned short s_w1, s_w2;
	} S_un_w;
	struct in_addr S_addr;
};

#define	s_host	S_un_b.s_b2	/* host on imp */
#define	s_net   S_un_b.s_b1	/* network */
#define	s_imp	S_un_w.s_w2	/* imp */
#define	s_impno	S_un_b.s_b4	/* imp # */
#define	s_lh	S_un_b.s_b3	/* logical host */

/*
 * chipout: We are called through ifp->if_output by the IP code
 *  When a packet wants to go to the chaos net, we allocate a chaos
 *  packet. Copy the IP packet into the chaos packet's data area
 *  set the packet opcode to UNCOP and the protocol ID to CHAIP
 *  and call the ChaosNet code to do the dogwork.
 */
int
chipout(struct ifnet *ifp, struct mbuf *m0, struct sockaddr *dst)
{
	struct mbuf *m = m0;
	struct ip *ip = mtod(m0, struct ip *);
	struct packet *pkt;
	struct sockaddr_in *sin = (struct sockaddr_in *)dst;
	int chlength;
	int offset;
	int lentocopy;
	int s;
	union S_un S_un;

	s = splimp();
	chlength = htons((unsigned short)ip->ip_len);
	if (chlength > CHMAXDATA) {
		ifp->if_oerrors++;
		m_freem(m0);
		splx(s);
		return (1);
	}
	pkt = pkalloc(chlength, 1);	/* Cannot wait (I think...) */
	if (pkt == NULL) {
		ifp->if_oerrors++;
		m_freem(m0);
		splx(s);
		return (1);
	}
	ifp->if_opackets++;
	offset = 0;
	while (chlength > 0) {
		/*
		 * RDZ: Minimum 
		 */
		lentocopy = (chlength > m->m_len ? m->m_len : chlength);
/*		lentocopy = imin (chlength, m->m_len);	/* JG signed min */
		bcopy((char *)((int)m + m->m_off), &(pkt->pk_cdata[offset]), lentocopy);
		chlength -= lentocopy;
		offset += lentocopy;
		m = m->m_next;
		if ((m == NULL) & (chlength != 0)) {
			printf("CHIP: m = NULL and chlength = %d\n", chlength);
			chlength = 0;
		}
	}
	pkt->pk_next = NOPKT;
	pkt->pk_type = 0;
	pkt->pk_op = UNCOP;
	pkt->pk_len = htons((unsigned short)ip->ip_len);

/* ArpaNet := 10.<sin_addr.s_host>.<sin_addr.s_lh>.<sin_addr.s_impno> but
   ChaosNet := 128.31.<pk_dsubnet>.<pk_dhost> get it... */

	/*
	 * ??? in_addrs are extracted differently in 4.3 
	 */
	/*
	 * see ../netinet/in.h and find an example somewhere of how to 
	 */
	/*
	 * dig this stuff out properly in 4.3 
	 */

	S_un.S_addr = sin->sin_addr;
	pkt->pk_dhost = S_un.s_impno;
	pkt->pk_dsubnet = S_un.s_lh;
	pkt->pk_didx = 0;
	pkt->pk_dtindex = 0;
	pkt->pk_shost = 0;	/* ??? */
	pkt->pk_ssubnet = 0;	/* ??? */
	pkt->pk_sidx = 0;
	pkt->pk_pkn = 0;
	pkt->pk_ackn = IPACKN;
	m_freem(m0);
/*	chipdpkt (pkt, 1);		/* JIS DEBUG */
	sendctl(pkt);
	splx(s);
	return (0);
}

/*
 * Process an ioctl request.
 */
int
chipioctl(struct ifnet *ifp, int cmd, caddr_t data)
{
	struct ifaddr *ifa = (struct ifaddr *)data;
	int s = splimp(), error = 0;

	switch (cmd) {

	case SIOCSIFADDR:
		ifp->if_flags |= IFF_UP;
		chipinit(ifp->if_unit);

		switch (ifa->ifa_addr.sa_family) {

		case AF_INET:
			((struct arpcom *)ifp)->ac_ipaddr = IA_SIN(ifa)->sin_addr;

			break;

		}
		break;

	default:
		error = EINVAL;
	}
	splx(s);
	return (error);
}
