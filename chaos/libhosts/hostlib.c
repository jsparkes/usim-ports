/*
 * NOTES
 * 2/1/85 dove
 *    accept chaos address numbers in host_info()
 */

#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include <err.h>

#include "hosttab.h"

#define H host_data
#define CHECK	if (!host_data) errx(1, "host_data table not initialized")
#define CHECKCH	if (ch_net == 0) getchaos()

static int ch_net;		/* The net number of the local chaosnet */
static int arpa_net;

int arpa_host(struct host_entry *h);
struct host_entry *chaos_entry(short addr);

/*
 * Copy s2 to s1, converting to lower case
 * and truncating or null-padding to always copy n bytes
 * return s1
 */
static void
lowercase(char *s1, char *s2, int n)
{
	int i;

	for (i = 0; i < n; i++)
		if ((*s1++ = isupper(*s2) ? tolower(*s2++) : *s2++) == '\0')
			return;
}

struct host_entry *
host_info(char *name)
{
	struct host_entry *h;
	char **p;
	int i;
	char lcname[MAXHOST];

	if (isdigit(*name)) {
		return chaos_entry(chaos_addr(name, 0));
	}

	CHECK;
	lowercase(lcname, name, MAXHOST);
	for (i = H->ht_hsize, h = H->ht_hosts; --i >= 0; h++) {
		if (strncmp(lcname, h->host_name, MAXHOST) == 0)
			return h;
		p = h->host_nicnames;
		if (p)
			while (*p)
				if (strncmp(lcname, *p++, MAXHOST) == 0)
					return h;
	}
	return 0;
}

char *
host_name(char *name)
{
	struct host_entry *h = host_info(name);

	return h ? h->host_name : 0;
}

char *
host_system(char *name)
{
	struct host_entry *h = host_info(name);

	return h ? h->host_system : 0;
}

char *
host_machine(char *name)
{
	struct host_entry *h = host_info(name);

	return h ? h->host_machine : 0;
}

int
net_number(char *name)
{
	struct net_entry *n;
	int i;
	char lcname[MAXHOST];

	CHECK;
	lowercase(lcname, name, MAXHOST);
	for (i = H->ht_nsize, n = H->ht_nets; --i >= 0; n++)
		if (strncmp(lcname, n->net_name, MAXHOST) == 0)
			return n->net_number;
	return 0;
}

void
getchaos(void)
{
	struct net_address *a;
	struct net_entry *n;
	int i;

	for (a = H->ht_me->host_address; a->addr_net; a++) {
		for (n = H->ht_nets, i = 0; i < H->ht_nsize; i++, n++)
			if (n->net_number == a->addr_net)
				break;
		if (n->net_type == NT_CHAOS) {
			ch_net = a->addr_net;
			break;
		}
	}
}

/*
 * Return the local chaos network address for the given host.
 * First we must know which network we are on, then find the given
 * host's address on that same network.
 *
 * subnet -- preferred subnet
 */
unsigned short
chaos_addr(char *name, int subnet)
{
	struct host_entry *h;
	int found = 0;

	CHECK;
	CHECKCH;
	if (isdigit(*name)) {
		if (sscanf(name, "%o", &found) != 1)
			return 0;
		else
			return found;
	}
	if ((h = host_info(name)) == 0)
		return 0;
	return chaos_host(h, 0);
}

unsigned short
chaos_host(struct host_entry *h, int subnet)
{
	struct net_address *a;
	int found;

	CHECK;
	CHECKCH;
	for (a = h->host_address, found = 0; a->addr_net; a++)
		if (a->addr_net == ch_net) {
			if ((a->addr_host >> 8) == subnet)
				return a->addr_host;
			else if (found == 0)
				found = a->addr_host;
		}
	return found;
}

void
chaosnames(FILE *fp)
{
	struct host_entry *h;
	int i;
	struct net_address *a;
	char **p;

	CHECK;
	CHECKCH;
	for (i = H->ht_hsize, h = H->ht_hosts; --i >= 0; h++)
/*
 * THis doesn't work since this field isn't really maintained.
 * 		if (h->host_server)
 */
		for (a = h->host_address; a->addr_net; a++)
			if (a->addr_net == ch_net) {
				fprintf(fp, "%s\n", h->host_name);
				p = h->host_nicnames;
				if (p)
					while (*p)
						fprintf(fp, "%s\n", *p++);
			}
}

int
arpa_addr(char *name)
{
	struct host_entry *h;

	if ((h = host_info(name)) == 0)
		return 0;
	return arpa_host(h);
}

int
arpa_host(struct host_entry *h)
{
	struct net_address *a;

	if (arpa_net == 0)
		if ((arpa_net = net_number("ARPANET")) == 0)
			return 0;
	for (a = h->host_address; a->addr_net; a++)
		if (a->addr_net == arpa_net)
			return a->addr_host;
	return 0;
}

/*
 * subnet -- preferred net and subnet 
 */
int
ip_addr(char *name, int net, int subnet, struct ip_address *ip)
{
	struct host_entry *h = host_info(name);
	struct net_address *a;
	struct ip_address i, b;

	i.ip_net = 0;
	i.ip_hhost = 0;
	i.ip_mhost = 0;
	i.ip_lhost = 0;
	b = i;
	if (h == 0)
		return 1;
	for (a = h->host_address; a; a++) {
		i.ip_net = a->addr_net;
		i.ip_hhost = a->addr_host >> 8;
		i.ip_lhost = a->addr_host & 0377;
		if (i.ip_net == net) {
			if (i.ip_hhost == subnet) {
				*ip = i;
				return 0;
			}
			if (net && b.ip_net != net)
				b = i;
		} else if (b.ip_net == 0)
			b = i;
	}
	if (b.ip_net == 0)
		return 1;
	*ip = b;
	return 0;
}

static char *tftphosts[] = {
	"mit-multics",
	"mit-ln",
	"mit-rts",
	0
};

/*
 * Return true if a host supports TFTP (in particular, mail mode).
 */
int
istftphost(char *name)
{
	char lcname[MAXHOST];
	char **p;

	lowercase(lcname, name, MAXHOST);
	for (p = tftphosts; *p; p++)
		if (strncmp(lcname, *p, MAXHOST) == 0)
			return 1;
	return 0;
}

/*
 * Return the name of the host with the given local chaos net address.
 */
char *
chaos_name(short addr)
{
	struct host_entry *h, *hend;
	struct net_address *a;
	static char name[MAXHOST];

	CHECK;
	CHECKCH;
	hend = &H->ht_hosts[H->ht_hsize];
	for (h = H->ht_hosts; h < hend; h++)
		for (a = h->host_address; a->addr_net; a++)
			if (a->addr_net == ch_net && a->addr_host == addr)
				return h->host_name;
	sprintf(name, "host%0o", addr);
	return name;
}

/*
 * Return the host_entry of the host with the given local chaos net address.
 */
struct host_entry *
chaos_entry(short addr)
{
	struct host_entry *h, *hend;
	struct net_address *a;

	CHECK;
	CHECKCH;
	hend = &H->ht_hosts[H->ht_hsize];
	for (h = H->ht_hosts; h < hend; h++)
		for (a = h->host_address; a->addr_net; a++)
			if (a->addr_net == ch_net && a->addr_host == addr)
				return h;
	return NULL;
}

static int hostn;

void
host_start(void)
{
	CHECK;
	hostn = 0;
}

struct host_entry *
host_next(void)
{
	return hostn >= H->ht_hsize ? 0 : &H->ht_hosts[hostn++];
}

struct host_entry *
host_here(void)
{
	CHECK;
	return H->ht_me;
}

char *
host_me(void)
{
	return host_here()->host_name;
}
