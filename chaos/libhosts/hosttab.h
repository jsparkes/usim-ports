/*
 * Declarations for host table.
 */

#include <stdio.h>

#define MAXHOST	20		/* Maximum size of host name */

struct net_entry {
	char *net_name;
	int net_number;
	int net_type;
};

#define NT_CHAOS	1	/* Net is a chaos net */

struct host_entry {
	char *host_name;
	struct net_address *host_address;
	int host_server;	/* if SERVER, else USER */
	char *host_system;
	char *host_machine;
	char **host_nicnames;
};

struct net_address {
	short addr_net;
	short addr_host;
};

struct ip_address {		/* Internet Protocol address */
	char ip_net;		/* Network */
	char ip_hhost;		/* Most significant byte of address (often subnet) */
	char ip_mhost;		/* Middle byte of address */
	char ip_lhost;		/* Least significant byte of address */
};

struct host_data {
	struct net_entry *ht_nets;	/* Pointer to net table */
	struct host_entry *ht_hosts;	/* Pointer to host table */
	struct host_entry *ht_me;	/* This host */
	int ht_nsize;		/* Size of net table */
	int ht_hsize;		/* Size of host table */
};
extern struct host_data *host_data;	/* Filled inside the library */

extern void readhosts(char *whoami, char *hoststable);

extern struct host_entry *host_info(char *name);
extern char *host_name(char *name);
extern char *host_system(char *name);
extern char *host_machine(char *name);
extern char *chaos_name(short addr);
extern void chaosnames(FILE *fp);
extern int net_number(char *name);
extern unsigned short chaos_addr(char *name, int subnet);
extern unsigned short chaos_host(struct host_entry *h, int subnet);
extern int arpa_addr(char *name);
extern int ip_addr(char *name, int net, int subnet, struct ip_address *ip);
extern struct host_entry *host_here(void);	/* This host's entry */
extern char *host_me(void);	/* Name of this host */
extern void host_start(void);
extern struct host_entry *host_next(void);
