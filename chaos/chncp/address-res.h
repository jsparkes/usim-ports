/*
 * Address resolution definitions - ethernet and chaosnet specific
 */

struct ar_packet {
	short ar_hardware;	/* Hardware type */
	short ar_protocol;	/* Protocol-id - same as Ethernet packet type */
	unsigned char ar_hlength;	/* Hardware address length = 6 for ethernet */
	unsigned char ar_plength;	/* Protocol address length = 2 for chaosnet */
	short ar_opcode;	/* Address resolution op-code */
	unsigned char ar_esender[6];	/* Ethernet sender address */
	chaddr ar_csender;	/* Chaos sender address */
	unsigned char ar_etarget[6];	/* Target ethernet address */
	chaddr ar_ctarget;	/* target chaos address */
};

/*
 * Values for ar_hardware:
 */
#define AR_ETHERNET	(1<<8)	/* Ethernet hardware */

/*
 * Values for ar_opcode:
 */
#define AR_REQUEST	(1<<8)	/* Request for resolution */
#define AR_REPLY	(2<<8)	/* Reply to request */
