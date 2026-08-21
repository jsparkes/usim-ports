/*
 * Accomodate Ritchie C compiler...
 */
#ifdef pdp11
#ifndef lint
#ifndef void
#define void int
#endif
#endif
#endif

#ifdef SELECT
#include <pthread.h>
#endif

/*
 * System and device independent include file for the Chaosnet NCP
 */

/*
 * A chaos index - a hosts connection identifier
 * JAO: by convention, the LSB are used as an index into a table
 * (ci_Tidx), and the MSB is incremented to keep connection indices
 * unique to avoid collisions.
 */
typedef struct {
	unsigned char tidx;	/* Connection table index */
	unsigned char uniq;	/* Uniquizer for table slot */
} chindex;

#define ci_uniq	ci_bytes.ci_Uniq
#define ci_tidx	ci_bytes.ci_Tidx

#define CH_INDEX_SHORT(ci) ((ci).tidx | ((ci).uniq << 8))
#define SET_CH_INDEX(ci,short) do { ci.tidx = (short & 0xff); ci.uniq = (short & 0xff00) >> 8; } while (0)

/*
 * A chaos network address.
 * JAO: By convention, the subnet is the MSB and the host the LSB
 * of the 16-bit (short) address. On the network, and in memory, 
 * LSB comes first.
 */
typedef struct {
	unsigned char host;	/* Host number on subnet */
	unsigned char subnet;	/* Subnet number */
} chaddr;

#define ch_subnet	ch_bytes.ch_Subnet
#define ch_host		ch_bytes.ch_Host

#define CH_ADDR_SHORT(ch) ((ch).host | ((ch).subnet << 8))
#define SET_CH_ADDR(ch,short) do { \
ch.host = (short & 0xff); \
ch.subnet = (short & 0xff00) >> 8; \
} while (0)

/*
 * A chaosnet clock time - wrapsaround
 */
typedef unsigned short chtime;

/*
 * This is the part of the packet which is only for use by the ncp.
 * It is not transmitted over the network
 * NOTE that this structure is assumed to be at least 8 bytes by
 * the Interlan Ethernet driver, and assumed to be at most 8 bytes
 * by the storage allocator!!!!!  GGAAAAAAAAAAAGGGHHH
 */
struct ncp_header {
	struct packet *nh_next;	/* Link to next packet on this list */
	chtime nh_time;		/* Last time packet was processed */
	chaddr nh_xdest;	/* Routed destination address */
};

/* The packet length is 16 bits, but only the lowest 12 bits denote
   an actual length; the MSB 4 bits are a forwarding count. We store them
   in CHAOS network order, LSB first. */

typedef struct {
	unsigned char lsb;
	unsigned char msb;
} chpklenfc;

#define LENFC_LEN(lenfc) ((lenfc).lsb | ((int)((lenfc).msb & 0x0f) << 8))
#define LENFC_FC(lenfc) (((lenfc).msb & 0xf0) >> 4)
#define SET_LENFC_LEN(lenfc,len) do {  (lenfc).lsb = (len) & 0xff; \
(lenfc).msb = ((lenfc).msb & 0xf0) | (((len) & 0x0f00) >> 8); } while(0)
#define SET_LENFC_FC(lenfc,fc) (lenfc).msb = (((lenfc).msb & 0x0f) | ((fc & 0xf) << 4))

#define PH_LEN(ph) (LENFC_LEN(ph.ph_lenfc))
#define PH_FC(ph)  (LENFC_FC(ph.ph_lenfc))
#define SET_PH_LEN(ph,len) SET_LENFC_LEN(ph.ph_lenfc,len)
#define SET_PH_FC(ph,fc) SET_LENFC_FC(ph.ph_lenfc,fc)

/*
 * This is the part of the packet header that is transmitted over the
 * network, thus must have fixed, portable format from ncp to ncp
 */
struct pkt_header {
	unsigned char ph_type;	/* Protocol type */
	unsigned char ph_op;	/* Opcode of the packet */
	chpklenfc ph_lenfc;
	chaddr ph_daddr;	/* Destination address */
	chindex ph_didx;	/* Destination index */
	chaddr ph_saddr;	/* Source address */
	chindex ph_sidx;	/* Source index */
	unsigned short ph_pkn;	/* Packet number */
	unsigned short ph_ackn;	/* Acknowledged packet number */
};

#define ph_len ph_lenfc.ph_lfcparts.ph_Len

/* This is the actual structure of a packet in core */
struct packet {
#ifndef SELECT
	struct ncp_header pk_nhead;	/* NCP specific information */
#endif
	struct pkt_header pk_phead;	/* Network header */
	union {
		char pk_Cdata[sizeof(short)];	/* Character data */
		short pk_Idata[1];	/* word data */
		long pk_Ldata[1];	/* long data */
		struct sts_data {	/* data of STS packets */
			unsigned short pk_Receipt;
			unsigned short pk_Rwsize;
		} pk_stsdata;
		struct rut_data {	/* data of RUT packets */
			unsigned short pk_subnet;
			unsigned short pk_cost;
		} pk_Rutdata[1];
		struct status {
			char sb_name[CHSTATNAME];
			struct statdata {
				struct stathead {
					unsigned short sb_Ident;
					unsigned short sb_Nshorts;
				} sb_head;
				union {
					struct statxcvr {
						long sx_Rcvd;
						long sx_Xmtd;
						long sx_Abrt;
						long sx_Lost;
						long sx_Crcr;
						long sx_Crci;
						long sx_Leng;
						long sx_Rej;
					} sb_Xstat;
				} sb_union;
			} sb_data[1];
		} pk_Status;
	} pk_data;
};

#define NOPKT		((struct packet *)0)

#ifdef SELECT
#define CHHEADSIZE	(sizeof(struct pkt_header))
#else
#define CHHEADSIZE	(sizeof(struct ncp_header)+sizeof(struct pkt_header))
#endif
#define pkalloc(x,y)	((struct packet *)ch_alloc((x)+CHHEADSIZE,(y)))

/*
 * Allocate an NCP packet given the number of shorts in the received packet.
 */
#define hpkalloc(nshorts)	((struct packet *)ch_alloc((int) \
				((nshorts) /* * sizeof(short) */ << 1) + \
				sizeof(struct ncp_header), 1))

/* macros for accessing packets fields */
#define pk_next		pk_nhead.nh_next
#define pk_time		pk_nhead.nh_time
#define pk_xdest	pk_nhead.nh_xdest
#define pk_type		pk_phead.ph_type
#define pk_op		pk_phead.ph_op
#define pk_len		pk_phead.ph_len
#define pk_fc		pk_phead.ph_lenfc.ph_lfcparts.ph_fcount
#define pk_lenword	pk_phead.ph_lenfc.ph_lfcwhole
#define pk_daddr	pk_phead.ph_daddr
#define pk_dhost	pk_phead.ph_daddr.host
#define pk_dsubnet	pk_phead.ph_daddr.subnet
#define pk_didx		pk_phead.ph_didx
#define pk_dtindex	pk_phead.ph_didx.tidx
#define pk_saddr	pk_phead.ph_saddr
#define pk_shost	pk_phead.ph_saddr.host
#define pk_ssubnet	pk_phead.ph_saddr.subnet
#define pk_sidx		pk_phead.ph_sidx
#define pk_stindex	pk_phead.ph_sidx.tidx
#define pk_suniq	pk_phead.ph_sidx.uniq
#define pk_pkn		pk_phead.ph_pkn
#define pk_ackn		pk_phead.ph_ackn
#define pk_cdata	pk_data.pk_Cdata
#define pk_idata	pk_data.pk_Idata
#define pk_ldata	pk_data.pk_Ldata
#define pk_receipt	pk_data.pk_stsdata.pk_Receipt
#define pk_rwsize	pk_data.pk_stsdata.pk_Rwsize
#define pk_rutdata	pk_data.pk_Rutdata
#define pk_status	pk_data.pk_Status
#define sb_ident	sb_head.sb_Ident
#define sb_nshorts	sb_head.sb_Nshorts
#define sb_xstat	sb_union.sb_Xstat

#define ISDATOP(pkt)	(((pkt)->pk_op & DATOP) != 0)
#define CONTPKT(pkt)	(ISDATOP(pkt) || (pkt)->pk_op == RFCOP || \
			(pkt)->pk_op == OPNOP || (pkt)->pk_op == EOFOP)

/* Here are the packet types */
#define PKNML	00		/* Normal */
#define PKLSN	01		/* Listen list packet */

/*
 * This is the connection structure. These are allocated in a packet of the
 * appropriate size
 */
struct connection {
#ifndef USE_CBRIDGE
	struct csys_header cn_syshead;	/* System dependent info  */
#endif
	unsigned char cn_flags;	/* Random flags */
	unsigned char cn_state;	/* State of the connection */
	chtime cn_active;	/* Last time connection was active */
	chaddr cn_Faddr;	/* Foreign address */
	chindex cn_Fidx;	/* Foreign index */
	chindex cn_Lidx;	/* Local index */
	chaddr cn_Laddr;	/* Local address */

	/*
	 * transmit side state 
	 */
	unsigned short cn_twsize;	/* Transmit window size */
	unsigned short cn_tlast;	/* Last packet we sent */
	unsigned short cn_trecvd;	/* Last pkt receipted by him */
	unsigned short cn_tacked;	/* Last pkt acked by him */
	struct packet *cn_thead;	/* Head of list of pkts transmitted */
	struct packet *cn_ttail;	/* Tail of list of pkts transmitted */
#ifdef CHSTRCODE
	struct packet *cn_toutput;	/* Pkt being filled for output */
	/*
	 * NOT a list, just one packet 
	 */
	unsigned short cn_troom;	/* Room left in cn_toutput packet */
	/*
	 * pk_len shows current fill level 
	 */
#endif

	/*
	 * receive side state 
	 */
	unsigned short cn_rwsize;	/* Receive window size */
	unsigned short cn_rlast;	/* Last pkt rcvd (in order) */
	unsigned short cn_racked;	/* Last pkt acked by us */
	unsigned short cn_rread;	/* Last pkt read by our user */
	unsigned short cn_rsts;	/* Max rread-racked before auto STS */
#ifdef CHSTRCODE
	unsigned short cn_roffset;	/* read offset in current packet */
	/*
	 * which is conn->cn_rhead 
	 */
#endif
	struct packet *cn_rhead;	/* Head of ordered rcvd pkts */
	struct packet *cn_rtail;	/* Tail of ordered received packets */
	struct packet *cn_routorder;	/* list of out of order packets */

	/*
	 * select state 
	 */
	struct proc *cn_tsel;	/* process selecting on xmit */
	struct proc *cn_rsel;	/* process selecting on recv */

#ifdef SELECT
	unsigned short packetnumber;	/* ---!!! */
#ifndef USE_CBRIDGE
	struct queue_head queuehead;	/* ---!!! */
	pthread_mutex_t queuelock;	/* ---!!! */
	pthread_mutex_t queuesem;	/* ---!!! */
	pthread_cond_t queuecond;	/* ---!!! */
	pthread_mutex_t twsem;	/* ---!!! */
	pthread_cond_t twcond;	/* ---!!! */
	struct queue_head orderhead;	/* ---!!! */
	struct packet *lastpacket;	/* ---!!! */
#else
  pthread_mutex_t interruption;
#endif
#endif
};

#define setack(connection, packet) \
	packet->pk_ackn = connection->cn_racked = connection->cn_rread
#define cn_fidx			cn_Fidx
#define cn_faddr		cn_Faddr
#define cn_fhost		cn_Faddr.host
#define cn_fsubnet		cn_Faddr.subnet
#define cn_lidx			cn_Lidx
#define cn_ltidx		cn_Lidx.tidx
#define cn_luniq		cn_Lidx.uniq
#define cn_laddr		cn_Laddr

/* bit values for cn_flags */
#ifdef	CHSTRCODE
#define CHEOFSEEN	1	/* EOF packet received and acknowledged */
#endif
#define CHANSWER	2	/* This connection should send an ANS pkt */
#define CHSELTCOLL	4	/* more than one proc selecting on xmit */
#define CHSELRCOLL	8	/* more than one proc selecting on recv */

/* macros for certain connection states */

#define chtfull(conn)	((conn)->cn_state == CSOPEN && \
			 (short)((conn)->cn_tlast - (conn)->cn_tacked) >= \
				(conn)->cn_twsize)
#define chtempty(conn)	((conn)->cn_state != CSOPEN || \
			 (conn)->cn_tlast == (conn)->cn_tacked)
#define chrempty(conn)	((conn)->cn_rhead == NOPKT && \
			 (conn)->cn_state == CSOPEN)

#define NOCONN		((struct connection *)0)
#define connalloc()	((struct connection *)ch_alloc(sizeof(struct connection),0))

/*
 * These are unsigned comparisons
 * all the casting is necessary due to compiler flakiness with shorts etc.
 * cmp_lt(a,b) is true if a < b, cmp_le(a,b) if a <= b, cmp_gt(a,b) if a > b
 * and cmp_ge(a,b) if a >= b
 */
#define cmp_gt(a,b) (0100000 & (b - a))
#define cmp_ge(a,b) !(cmp_lt(a,b)
#define cmp_lt(a,b) cmp_gt(b, a)
#define cmp_le(a,b) !cmp_gt(a,b)
/* codes for error returns in various places - needs cleaning up somewhat */

#define CHERROR		-1
#define CHEOF		-2
#define CHNOPKT		-3	/* No packets */
#define CHNOCONN	-4	/* No connections */
#define CHCTIMEOUT	-5	/* Time out */
#define CHTEMP		-6

/*
 * Network interface structure.
 *
 * There is one chxcvr structure for each interface connected to this host,
 * all of which are defined in the file "chconf.c". The xcinfo is a union
 * of structures needed for each device type's device dependent state.
 * This union should be defined in "chconf.h"
 * The bottom level device driver routines define arrays of these structures
 * one per interface of a given type and find the structure appropriate
 * to a given interface by indexing on the device number given in the
 * interrupt vector.  The top level gets at an chxcvr structure through
 * the routing table.
 * The ttime and rtime values are for timing-out hung transmitters or
 * receivers.  The drivers must ensure that tpkt and rpkt are only nonzero
 * when there is really a packet being received or transmitted.  This may
 * be partly useless for dma (or other packet-atomic) interfaces.
 */
struct chxcvr {
	struct packet *xc_list;	/* Packets to be transmitted */
	struct packet *xc_tail;	/* Tail of xc_list */
	struct packet *xc_tpkt;	/* Packet being transmitted */
	struct packet *xc_rpkt;	/* Packet being received */
	chtime xc_ttime;	/* Time tpkt begun transmission */
	chtime xc_rtime;	/* Time rpkt began reception */
	chaddr xc_Addr;		/* Address of this interface */
	unsigned *xc_devaddr;	/* Device address (UNIBUS) */
	int (*xc_start)(struct chxcvr *);	/* Start rtn for idle xmtr */
	int (*xc_reset)(void);	/* Reset rtn for interface */
	int (*xc_xmit)(struct chxcvr *, struct packet *, int);	/* xmit pkt (xcvr, pkt, at-head-p) */
	unsigned short xc_cost;	/* cost of access path */
	struct statxcvr xc_xstat;	/* Xcvr metering */
#ifndef USE_CBRIDGE
	union xcinfo xc_info;	/* Device dependent info */
#endif
};

#define xc_addr		xc_Addr
#define xc_subnet	xc_Addr.subnet
#define xc_host		xc_Addr.host
#define xc_rcvd		xc_xstat.sx_Rcvd
#define xc_xmtd		xc_xstat.sx_Xmtd
#define xc_crcr		xc_xstat.sx_Crcr
#define xc_crci		xc_xstat.sx_Crci
#define xc_lost		xc_xstat.sx_Lost
#define xc_leng		xc_xstat.sx_Leng
#define xc_rej		xc_xstat.sx_Rej
#define xc_abrt		xc_xstat.sx_Abrt

#define NOXCVR		((struct chxcvr *)0)

/*
 * Routing table entry structure.
 * One per subnet possibly accessible from this host.
 * Entries for directly connected subnets point to hardware
 * transceiver structure (rt_type == CHDIRECT).
 * Bridges (rt_type == CHFIXED or CHBRIDGE) have address of
 * directly connected bridges.
 */
struct chroute {
	union {
		struct chxcvr *rt_Xcvr;	/* interface to use */
		chaddr rt_Addr;	/* bridge address */
	} rt_u;
	unsigned char rt_type;	/* type of access */
	unsigned short rt_cost;	/* cost of access path */
};

#define rt_xcvr		rt_u.rt_Xcvr
#define rt_addr		rt_u.rt_Addr
#define rt_host		rt_u.rt_Addr.host
#define rt_subnet	rt_u.rt_Addr.subnet

/* values for rt_type */
#define CHNOPATH	0	/* No path to this subnet yet (now) */
#define	CHDIRECT	1	/* Either chaos cable or other hardware */
#define CHFIXED		2	/* Unvarying bridge */
#define CHBRIDGE	3	/* Bridge - known via RUT packet */
/* initial values for rt_cost depending on rt_type */
#define CHDCOST		10	/* Directly connected hardware (ala dr11) */
#define CHCCOST		16	/* Chaos cable connection (ether) */
#define CHACOST		20	/* Async or other slow link */
#define CHHCOST		1024	/* "high" cost */

/* definition of globals */

#ifdef CHDEFINE
#define EXTERN
#else
#define EXTERN extern
#endif				/* CHDEFINE */

EXTERN int Chaos_error;
EXTERN struct connection *Chconntab[CHNCONNS];	/* connection table */
EXTERN chtime Chclock;		/* clock (mod ??) */
EXTERN struct packet *Chlsnlist,	/* listening connections */
      *Chrfclist,		/* list of unmatched rfc's */
      *Chrfctail;		/* tail of same list */
EXTERN struct chroute Chroutetab[CHNSUBNET];	/* subnet routing table */
EXTERN int Chhz;		/* Hertz of clock */
EXTERN int Chrfcrcv;		/* Flag for rfc reader */
extern short Chmyaddr;		/* This ncp'c host number */
extern char Chmyname[];		/* This ncp's host name */
EXTERN int Chdebug;

/* chclock */

extern void ch_clock(void);
extern void chretran(struct connection *conn, int age);
extern void chroutage(void);
extern void chbridge(void);

/* chrcv */

extern void rcvpkt(struct chxcvr *xp);
extern void reflect(struct packet *pkt);
extern void rcvdata(struct connection *conn, struct packet *pkt);
extern void makests(struct connection *conn, struct packet *pkt);
extern void receipt(struct connection *conn, unsigned short acknum, unsigned short recnum);
extern void sendlos(struct packet *pkt, char *str, int len);
extern void rcvbrd(struct packet *pkt);
extern void rcvrfc(struct packet *pkt);
extern void lsnmatch(struct packet *rfcpkt, struct connection *conn);
extern void rmlisten(struct connection *conn);
extern int concmp(struct packet *rfcpkt, char *lsnstr, int lsnlen);
extern void rcvrut(struct packet *pkt);
extern void statusrfc(struct packet *pkt);
extern void dumprtrfc(struct packet *pkt);
extern void timerfc(struct packet *pkt);
extern void uptimerfc(struct packet *pkt);
extern void prpkt(struct packet *pkt, char *str);
extern void showpkt(char *str, struct packet *pkt);

/* chsend */

extern void sendsts(struct connection *conn);
extern void sendsns(struct connection *conn);
extern void chxmitpkt(struct chxcvr *xcvr, struct packet *pkt, int head);
extern void sendctl(struct packet *pkt);
extern void senddata(struct packet *pkt);
extern void sendrut(struct packet *pkt, struct chxcvr *xcvr, unsigned short cost, int copy);
extern void sendtome(struct packet *pkt);
extern void xmitdone(struct packet *pkt);
extern struct packet *xmitnext(struct chxcvr *xcvr);

/* chutil */

extern struct connection *allconn(void);
extern void clsconn(struct connection *conn, int state, struct packet *pkt);
extern void rlsconn(struct connection *conn);
extern void freelist(struct packet *pkt);
extern struct packet *pktstr(struct packet *pkt, char *str, int);
extern void clear(char *ptr, int n);
extern void movepkt(struct packet *opkt, struct packet *npkt);
extern void chmove(char *from, char *to, int n);
extern void setpkt(struct connection *conn, struct packet *pkt);
extern void chattach(struct chxcvr *xp);
extern void swaplong(short *lp, int n);

#ifndef LINUX_KERNEL
#include <sys/types.h>

#ifndef USE_CBRIDGE
extern void chaos_interrupt_connection(struct connection *conn);
#endif
extern struct packet *chaos_allocate_packet(struct connection *conn, int opcode, ssize_t len);
extern int chaos_connection_queue(struct connection *conn, struct packet *packet);
extern struct packet *chaos_connection_dequeue(struct connection *conn);
extern struct connection *chaos_find_connection(unsigned short index);
extern void chaos_dump_connection(struct connection *conn);
#endif

/* chuser */

extern struct connection *ch_open(int, int, struct packet *);
extern struct connection *ch_listen(struct packet *, int);
extern int ch_write(struct connection *conn, struct packet *pkt);
extern void ch_read(struct connection *conn);
extern int ch_eof(struct connection *conn);
extern void ch_close(struct connection *conn, struct packet *pkt, int release);
extern void ch_sts(struct connection *conn);
extern void ch_accept(struct connection *conn);
extern struct packet *ch_rnext(void);
extern void ch_rskip(void);

/* chaos */

#ifdef BSD42
extern int chf_rw(struct file *fp, enum uio_rw rw, struct uio *uio);
extern int chf_ioctl(struct file *fp, int cmd, caddr_t value);
extern int chf_select(struct file *fp, int which);
extern void chf_close(struct file *fp);
extern int chwcopy(char *from, char *to, unsigned count, int uio, int *errorp);
extern int chrcopy(char *from, char *to, unsigned count, int uio, int *errorp);
extern int chropen(dev_t dev, int flag);
#endif				/* BSD42 */

/* This is not the same as chopen.c:chopen(), and thus conflicts! 
 *
 * extern struct connection *chopen(struct chopen *c, int wflag, int *errnop); 
 */

#if BSD42
extern void chrclose(dev_t dev, int flag, struct connection *conn);
extern void chclose(struct connection *conn);
extern int chrread(dev_t dev, int uio);
extern int chread(struct connection *conn, int uio);
extern int chrwrite(dev_t dev);
extern int chwrite(struct connection *conn, int uio);
extern int chrioctl(int dev, caddr_t addr, int cmd, int flag);
extern int chioctl(struct connection *conn, int cmd, caddr_t addr);
extern void chtimeout(void);
extern void chrwake(struct connection *conn);
extern void chtwake(struct connection *conn);
#endif				/* BSD42 */

/* challoc */

extern void ch_free(void *pkt);
extern void *ch_alloc(int, int);
extern void ch_free(void *p);
extern int ch_size(char *p);
extern int ch_badaddr(char *p);
extern void ch_bufalloc(void);
extern void ch_buffree(void);

/* chconf */

extern void chreset(void);
extern void chdeinit(void);
extern void chxtime(void);

/* chtime */

extern void ch_time(long *tp);
extern void ch_uptime(long *tp);

/* debugging instrumentation */
#define DEBUG_CHAOS
#ifdef DEBUG_CHAOS

#define debug(a,b)	if(Chdebug&(a)) b; else	/* expect a ; after! */

#define DALLOC	1		/* Allocation tracing */
#define DTRANS	2		/* Transmitter tracing */
#define DCONN	4		/* Connection activity */
#define DPKT	8		/* Print packets */
#define DNOCLK	16		/* No clock timeouts */
#define DABNOR	32		/* Abnormal events */
#define DSEND	64		/* Trace each packet sent */

#else
#define debug(a,b)
#endif
