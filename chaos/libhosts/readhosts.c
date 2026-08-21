#include <ctype.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <err.h>

#include "hosttab.h"

#define MAXNAMELENGTH 50
#define NNETS 50
#define NHOSTS 1000
#define NSYSTEMS 100
#define NMACHINES 100

struct net_entry nets[NNETS];
struct host_entry hosts[NHOSTS];
char *systems[NSYSTEMS];
char *machines[NMACHINES];

struct host_data everything;
struct host_data *host_data;

char *parsehosts(struct host_entry *h, char *p);
char *parseaddr(struct net_address *a, char *p);
void nicnames(struct host_entry *h, char *p);
char *lower(char *s);
char *canon(char **t, char *s);

void hosts2(FILE *fp, char *whoami);

void
readhosts(char *me, char *hosts)
{
	FILE *fp;

	fp = fopen(hosts == NULL ? "hosts" : hosts, "r");
	if (fp == NULL)
		errx(1, "can't open hosts for input: %s", hosts);

	hosts2(fp, me);
	host_data = &everything;

	fclose(fp);
}

/*
 * Read an SRI-style official host table and populate a host_data structure..
 */
void
hosts2(FILE *fp, char *whoami)
{
	char line[512];
	struct host_entry *myhost = NULL;
	struct net_entry *nextnet = nets;
	struct host_entry *nexthost = hosts;

	while (fgets(line, sizeof(line), fp) != NULL) {
		char name[MAXNAMELENGTH];
		int number;

		if (line[0] == ';' || line[0] == 014 || line[0] == 012 || line[0] == 0)
			continue;
		if (sscanf(line, "NET %[^,], %d", name, &number) == 2) {
			char *cp;

			nextnet->net_name = malloc(strlen(name) + 1);
			strcpy(nextnet->net_name, name);
			for (cp = nextnet->net_name; *cp; cp++)
				if (*cp == 'C' && strncmp("CHAOS", cp, 5) == 0) {
					nextnet->net_type = NT_CHAOS;
					break;
				}

			nextnet->net_number = number;
			nextnet += 1;
		} else if (sscanf(line, "HOST %[^,],", name) == 1) {
			char *p, *q;
			char status[8], system[20], machine[20];

			nexthost->host_name = malloc(strlen(name) + 1);
			strcpy(nexthost->host_name, name);
			lower(nexthost->host_name);
			if (myhost == NULL && strcmp(whoami, name) == 0) {
				myhost = nexthost;
			}
			for (p = line; *p++ != ',';);
			while (*p == ' ' || *p == '\t')
				p++;
			p = parsehosts(nexthost, p);
			while (*p == ' ' || *p == '\t')
				p++;
			for (q = status; *p && *p != ','; p++)
				if (q < &status[7])
					*q++ = *p;
			*q = 0;
			if (*p == ',')
				p += 1;
			while (*p == ' ' || *p == '\t')
				p++;
			for (q = system; *p && *p != ','; p++)
				if (q < &system[19])
					*q++ = *p;
			*q = 0;
			if (*p == ',')
				p += 1;
			while (*p == ' ' || *p == '\t')
				p++;
			for (q = machine; *p && *p != ','; p++)
				if (q < &machine[19])
					*q++ = *p;
			*q = 0;
			if (strcmp("SERVER", status) == 0)
				nexthost->host_server = 1;
			else if (strcmp("USER", status) == 0)
				nexthost->host_server = 0;
			else
				errx(5, "bad status %s for %s", status, name);
			nexthost->host_system = canon(systems, system);
			nexthost->host_machine = canon(machines, machine);
			if (*p == ',')
				nicnames(nexthost, ++p);
			nexthost += 1;
		} else
			errx(6, "syntax error in hosts: %s", line);
	}
	if (myhost == NULL)
		errx(7, "No host in table matches: %s", whoami);

	everything.ht_nets = nets;
	everything.ht_hosts = hosts;
	everything.ht_me = myhost;
	everything.ht_nsize = nextnet - nets;
	everything.ht_hsize = nexthost - hosts;
}

char *
parsehosts(struct host_entry *h, char *p)
{
	struct net_address addrs[10];
	struct net_address *a = addrs, *b;

	if (*p == '[') {
		p += 1;
		while (*p != ']') {
			p = parseaddr(a++, p);
			if (*p == ',')
				p += 1;
		}
		p += 1;
	} else
		p = parseaddr(a++, p);
	h->host_address = (struct net_address *)
	    malloc((a - addrs + 1) * sizeof(*h->host_address));
	b = &h->host_address[a - addrs];
	b->addr_net = 0;	/* Indicate end of list */
	while (--a >= addrs)
		*--b = *a;
	if (*p == ',')
		p += 1;
	return p;
}

char *
parseaddr(struct net_address *a, char *p)
{
	char net[20];
	struct net_entry *n;

	while (*p == ' ' || *p == '\t')
		p++;
	if (isalpha(*p)) {
		sscanf(p, "%s", net);
		while (*p++ != ' ');
#ifdef OLDHOSTS2
		if (strcmp("DIAL", net) == 0)
			strcpy(net, "DIALNET");
		else if (strcmp("ARPA", net) == 0)
			strcpy(net, "ARPANET");
		else if (strcmp("RCC", net) == 0)
			strcpy(net, "RCC-NET");
		else if (strcmp("SU", net) == 0)
			strcpy(net, "SU-NET-TEMP");
		else if (strcmp("LCS", net) == 0)
			strcpy(net, "MIT");
		else if (strcmp("RU", net) == 0)
			strcpy(net, "RU-NET");
#endif
	} else
		strcpy(net, "ARPANET");
	for (n = nets; n->net_name; n++)
		if (strcmp(net, n->net_name) == 0)
			break;
	if (!n->net_name)
		errx(7, "bad net name %s", net);
	a->addr_net = n->net_number;
	if (strcmp(net + strlen(net) - strlen("CHAOS"), "CHAOS") == 0) {
		int number;

		if (sscanf(p, "%o", &number) != 1)
			errx(8, "bad chaos net address %s", p);
		a->addr_host = number;
	} else if (strcmp(net, "ARPANET") == 0
#ifdef OLDHOSTS2
	    || strcmp(net, "BBN-RCC") == 0
#else
	    || strcmp(net, "MILNET") == 0 || strcmp(net, "RCC") == 0
#endif
	    ) {
		int host, imp;

		if (sscanf(p, "%d/%d", &host, &imp) != 2)
			errx(9, "bad arpanet address %s", p);
		a->addr_host = host * 64 + imp;
	} else if (
#ifdef OLDHOSTS2
	    strcmp(net, "LCSNET") == 0
#else
	    strcmp(net, "LCS") == 0
#endif
	    ) {
		int subnet, host;

		if (sscanf(p, "%o/%o", &subnet, &host) != 2)
			errx(11, "bad lcsnet address %s", p);
		a->addr_host = (subnet << 8) + host;
	} else
		a->addr_host = 0;
	while (*p && *p != ']' && *p != ',')
		p += 1;
	return p;
}

char *
canon(char **t, char *s)
{
	char *p;

	for (p = s; *p; p++)
		if (isupper(*p))
			*p = tolower(*p);
	for (; *t; t++)
		if (strcmp(*t, s) == 0)
			return *t;
	*t = malloc(strlen(s) + 1);
	strcpy(*t, s);
	return *t;
}

/*
 * Parse nicnames.
 */
void
nicnames(struct host_entry *h, char *p)
{
	char *names[20], name[MAXNAMELENGTH];
	char *q;
	char **a, **b;

	for (a = names; a < &names[20]; a++)
		*a = 0;
	if (*p == '[') {
		p += 1;
		while (*p != ']') {
			for (q = name; *p && *p != ',' && *p != ']'; p++)
				if (q < &name[MAXNAMELENGTH - 1])
					*q++ = *p;
			*q = 0;
			canon(names, name);
			if (*p == ',')
				p += 1;
		}
	} else
		canon(names, p);
	for (a = names; *a; a++);
	h->host_nicnames = (char **)malloc((a - names + 1) * sizeof *h->host_nicnames);
	b = &h->host_nicnames[a - names];
	while (a >= names)
		*b-- = *a--;
}

/*
 * Convert a string to lower case.
 */
char *
lower(char *s)
{
	char *p;

	for (p = s; *p; p++)
		if (isupper(*p))
			*p = tolower(*p);
	return s;
}
