#include <linux/signal.h>
#include <linux/errno.h>
#include <linux/sched.h>
#include <linux/proc_fs.h>
#include <linux/if_arp.h>

#include "chaos.h"
#include "chlinux/chsys.h"
#include "chunix/chconf.h"
#include "chncp/chncp.h"
#include "chunix/challoc.h"

#define ELENGTH 6
#define NPAIRS 20		/* how many addresses should we remember */
struct ar_pair {
	chaddr arp_chaos;
	unsigned char arp_ether[6];
	long arp_time;
};

#define arp_sha(arp)	(           (((char *)(arp+1))      ) )
#define arp_scha(arp)	((chaddr *) (((char *)(arp+1))+6    ) )->ch_addr
#define arp_tea(arp)	(           (((char *)(arp+1))+6+2  ) )
#define arp_tcha(arp)	((chaddr *) (((char *)(arp+1))+6+2+6) )->ch_addr

struct chxcvr chetherxcvr[NCHETHER];
struct ar_pair charpairs[NPAIRS];
long charptime = 1;		/* LRU clock for ar_pair slots */

int
charpin(struct sk_buff *skb, struct device *dev, struct packet_type *pt)
{
#if 0
	struct arphdr *arp = (struct arphdr *)skb->h.raw;
	unsigned char *arp_ptr = (unsigned char *)(arp + 1);
	short mychaddr;

	printk("charpin()\n");

	if (ntohs(arp->ar_hrd) != ARPHRD_ETHER || ntohs(arp->ar_pro) != ETHERTYPE_CHAOS || dev->type != ntohs(arp->ar_hrd) || arp->ar_pln != sizeof(chaddr) || arp->ar_hln != ELENGTH || arp->ar_hln != dev->addr_len || dev->flags & IFF_NOARP || arp_scha(arp) == 0 || arp_tcha(arp) == 0) {
		kfree_skb(skb, FREE_READ);
		return 0;
	}

	/*
	 * find inbound interface so we know our address 
	 */
	{
		register struct chxcvr *xp;

		for (xp = chetherxcvr; xp->xc_etherinfo.bound_dev != dev; xp++)
			if (xp == &chetherxcvr[NCHETHER - 1])
				goto freem;
		mychaddr = xp->xc_addr;

		printk("xp %p, mychaddr %o\n", xp, mychaddr);
	}


	/*
	 * lookup in our arp cache 
	 */
	{
		register struct ar_pair *app;
		register struct ar_pair *nap = charpairs;
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
			memcpy(app->arp_ether, arp_sha(arp), ELENGTH);
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
			memcpy(nap->arp_ether, arp_sha(arp), ELENGTH);
			nap->arp_chaos.ch_addr = arp_scha(arp);
			nap->arp_time = 1;
		}
	}

	if (ntohs(arp->ar_op) == ARPOP_REQUEST) {
		char sha[MAX_ADDR_LEN], tha[MAX_ADDR_LEN];

		memcpy(sha, arp_tea(arp), dev->addr_len);
		memcpy(tha, arp_sha(arp), dev->addr_len);

		/*
		 * flip the protocol addresses 
		 */
		arp_tcha(arp) = arp_scha(arp);
		arp_scha(arp) = mychaddr;

		/*
		 * flip the h/w addresses 
		 */
		memcpy(arp_tea(arp), arp_sha(arp), ELENGTH);
		memcpy(arp_sha(arp), sha, ELENGTH);

		arp->ar_op = htons(ARPOP_REPLY);

		dev->hard_header(skb, dev, ETHERTYPE_ARP, tha, sha, skb->len);

		printk("charpin() responding to arp request\n");
		dev_queue_xmit(skb, dev, 0);

		return;
	}
      freem:
	printk("charpin() bailing\n");
	kfree_skb(skb, FREE_READ);
#endif
	return 0;
}

int
chein(struct sk_buff *skb, struct device *dev, struct packet_type *pt)
{
#if 0
	struct pkt_header *php = (struct pkt_header *)skb->h.raw;
	struct chxcvr *xp;
	int chlength;

	printk("chein()\n");
	for (xp = chetherxcvr; xp->xc_etherinfo.bound_dev != dev; xp++)
		if (xp >= &chetherxcvr[NCHETHER - 1]) {
			kfree_skb(skb, FREE_READ);
			return 0;
		}

	/*
	 * verify the lengths before we start 
	 */
	chlength = php->ph_len + sizeof(struct pkt_header);

	if (skb->len < sizeof(struct pkt_header) || chlength > CHMAXDATA + sizeof(struct pkt_header)) {
		kfree_skb(skb, FREE_READ);
		return 0;
	}

	/*
	 * copying is painful, but it works 
	 */
	{
		struct packet *pkt;

		pkt = (struct packet *)ch_alloc(chlength + sizeof(struct ncp_header), 1);
		if (pkt != NULL) {
			memcpy((char *)&pkt->pk_phead, skb->h.raw, chlength);
			xp->xc_rpkt = pkt;
			rcvpkt(xp);
		}
	}

	kfree_skb(skb, FREE_READ);
#endif
	return 0;
}

cheoutput(xcvr, pkt, head)
	struct chxcvr *xcvr;
	register struct packet *pkt;
	int head;
{
#if 0
	int chlength, arplength, resolving = 1;
	struct sk_buff *skb;
	struct device *dev;

	printk("cheoutput() pk_xdest 0x%x\n", pkt->pk_xdest);
	xcvr->xc_xmtd++;

	dev = xcvr->xc_etherinfo.bound_dev;

	/*
	 * sizeof(pkt_header) better be >= sizeof(arp) 
	 */
	chlength = pkt->pk_len + sizeof(struct pkt_header);
	arplength = sizeof(struct arphdr) + 2 * (dev->addr_len + 4);

	skb = alloc_skb(ETH_HLEN + (chlength < arplength ? arplength : chlength), GFP_ATOMIC);
	if (skb == 0) {
		xmitdone(pkt);
		return;
	}
	printk("allocated %d byte packet\n", ETH_HLEN + (chlength < arplength ? arplength : chlength));

	/*
	 * save room for the ethernet header 
	 */
	skb_reserve(skb, ETH_HLEN);

	if (pkt->pk_xdest == 0) {
		/*
		 * broadcast 
		 */
		dev->hard_header(skb, dev, ETHERTYPE_CHAOS, dev->broadcast, 0, skb->len);
		resolving = 0;
	} else {
		register struct ar_pair *app;

		/*
		 * get h/w address from arp cache 
		 */
		for (app = charpairs; app < &charpairs[NPAIRS] && app->arp_chaos.ch_addr; app++)
			if (pkt->pk_xdest == app->arp_chaos.ch_addr) {
				app->arp_time = ++charptime;
				printk("found in cache\n");
				dev->hard_header(skb, dev, ETHERTYPE_CHAOS, app->arp_ether, 0, skb->len);
				resolving = 0;
				break;
			}
	}

	printk("resolving %d\n", resolving);
	if (resolving) {
		struct arphdr *arp;

		dev->hard_header(skb, dev, ETHERTYPE_ARP, dev->broadcast, 0, skb->len);

		/*
		 * advance tail & len 
		 */
		printk("need %d bytes for arp\n", sizeof(struct arphdr) + 2 * (dev->addr_len + 4));
		arp = (struct arphdr *)skb_put(skb, sizeof(struct arphdr) + 2 * (dev->addr_len + 4));

		arp->ar_hrd = htons(ARPHRD_ETHER);
		arp->ar_pro = htons(ETHERTYPE_CHAOS);
		arp->ar_hln = ELENGTH;
		arp->ar_pln = sizeof(chaddr);
		arp->ar_op = htons(ARPOP_REQUEST);
		memcpy((caddr_t)arp_sha(arp), dev->dev_addr, ELENGTH);
		arp_scha(arp) = xcvr->xc_addr;
		memcpy(arp_tea(arp), dev->broadcast, ELENGTH);
		arp_tcha(arp) = pkt->pk_xdest;
		printk("sending chaos arp for %o from %o\n", pkt->pk_xdest, xcvr->xc_addr);
	} else {
		char *pp = skb_put(skb, chlength);

		if (pp)
			memcpy(pp, (char *)&pkt->pk_phead, chlength);
		printk("sending chaos datagram\n");
	}

	skb->arp = 1;

	xmitdone(pkt);
	dev_queue_xmit(skb, xcvr->xc_etherinfo.bound_dev, 0);
#endif
}

cheinit()
{
}

chereset()
{
}

chestart()
{
}

/*
 * Handle the CHIOCETHER ioctl to assign a chaos address to an
 * ethernet interface.  Note that all initialization is done here.
 */
cheaddr(addr)
	char *addr;
{
#if 0
	struct chether che;
	struct chxcvr *xp;
	struct device *dev;
	struct packet_type *p1, *p2;
	int errno;

	printk("cheaddr()\n");

	errno = verify_area(VERIFY_READ, (int *)addr, sizeof(struct chether));
	if (errno)
		return errno;

	memcpy_fromfs(&che, (int *)addr, sizeof(struct chether));

	/*
	 * find named ethernet device 
	 */
	dev = dev_get(che.ce_name);
	if (dev == NULL)
		return -ENODEV;

	xp = (struct chxcvr *)kmalloc(sizeof(struct chxcvr), GFP_KERNEL);
	if (xp == NULL) {
		return -ENOMEM;
	}

	/*
	 * attach it 
	 */
	xp->xc_addr = che.ce_addr;
	xp->xc_cost = CHCCOST;
	xp->xc_xmit = cheoutput;
	xp->xc_reset = chereset;
	xp->xc_start = chestart;

	printk("chether: %s enabled as Chaosnet address 0%o\n", che.ce_name, che.ce_addr);

	chattach(xp);

	/*
	 * set up for reception of chaos & arp packets 
	 */
	p1 = (struct packet_type *)kmalloc(sizeof(*p1), GFP_KERNEL);
	if (p1 == NULL) {
		chedeinit();
		return -ENOMEM;
	}

	p1->func = chein;
	p1->type = ETHERTYPE_CHAOS;
	p1->data = (void *)0;
	p1->dev = dev;
	dev_add_pack(p1);

	p2 = (struct packet_type *)kmalloc(sizeof(*p2), GFP_KERNEL);
	if (p2 == NULL) {
		chedeinit();
		return -ENOMEM;
	}

	p2->func = charpin;
	p2->type = ETHERTYPE_ARP;
	p2->data = (void *)0;
	p2->dev = dev;
	dev_add_pack(p2);

	xp->xc_etherinfo.prot_hook1 = p1;
	xp->xc_etherinfo.prot_hook2 = p2;
	xp->xc_etherinfo.bound_dev = dev;

	printk("cheaddr() done\n");
#endif
	return 0;
}

void
chedeinit()
{
	struct chxcvr *xp;

	printk("chedeinit()\n");

	/*
	 * memory leak unloading till we turn this on 
	 */
#if 0
	if (xp->xc_etherinfo.prot_hook1) {
		dev_remove_pack((struct packet_type *)
		    xp->xc_etherinfo.prot_hook1);
		kfree_s(xp->xc_etherinfo.prot_hook1, sizeof(struct packet_type));
		xp->xc_etherinfo.prot_hook1 = NULL;
	}

	if (xp->xc_etherinfo.prot_hook2) {
		dev_remove_pack((struct packet_type *)
		    xp->xc_etherinfo.prot_hook2);
		kfree_s(xp->xc_etherinfo.prot_hook2, sizeof(struct packet_type));
		xp->xc_etherinfo.prot_hook2 = NULL;
	}

	kfree_s((char *)xp, sizeof(struct chxcvr));
#endif
}
