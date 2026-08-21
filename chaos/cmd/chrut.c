/*
 * Gregory D. Troxel 05AUG88
 */

#include <stdio.h>
#include <ctype.h>
#include <hosttab.h>
#include <sys/types.h>
#include <sys/dir.h>
#include <sys/file.h>
#ifdef BSD42
#include <sys/time.h>
#else
#include <time.h>
#endif
#include <sys/chaos.h>
/*
 * chsend user host
 */

struct chroute_data {
	unsigned short addr;
	short cost;
};

void dumphost();
void dumpfromto();
unsigned short chparse();
char *chunparse();

main(argc, argv)
	int argc;
	char **argv;
{
	switch (argc) {
	case 2:
		dumphost(argv[1]);
		break;

	case 3:
		dumpfromto(argv[1], argv[2]);
		break;

	default:
		printf("chrut host OR chrut from to\n");
		exit(-1);
	}

}

void
dumphost(host)
	char *host;
{
	unsigned short addr;
	char routebuf[1024];

	if ((addr = chparse(host)) == 0)
		exit(1);
	if (get_table(addr, routebuf) != 0)
		exit(2);
	printtable(routebuf);
}

void
dumpfromto(from, to)
	char *from, *to;
{
	unsigned short faddr, taddr;
	int tsubnet;
	short cost;
	unsigned short bridge;
	char routebuf[1024];
	struct chroute_data *rdata;
	char *name;

	if ((faddr = chparse(from)) == 0)
		exit(11);
	if ((taddr = chparse(to)) == 0)
		exit(12);
	tsubnet = (taddr >> 8) & 0xff;

	while (faddr != taddr) {
		if (get_table(faddr, routebuf) != 0)
			exit(13);

		printf("%s: ", chaos_name(faddr));

		rdata = (struct chroute_data *)routebuf;
		cost = rdata[tsubnet].cost;
		bridge = rdata[tsubnet].addr;

		if (bridge == 0) {
			printf("no route to subnet %o\n", tsubnet);
			return;
		}

		if ((bridge & 0xff00) == 0) {
			printf("Interface %d on subnet %o cost %d\n", bridge >> 1, tsubnet, cost);
			return;
		}

		name = chunparse(bridge);
		if (name != NULL)
			printf("Bridge %s at %o on subnet %o cost %d\n", name, bridge, bridge >> 8, cost);
		else
			printf("Bridge %o on subnet %o cost %d\n", bridge, bridge >> 8, cost);

		faddr = bridge;
	}
}

/*
 * returns:
 *  0 success
 *  1 can't open connection
 */
int
get_table(host, where)
	unsigned short host;
	char *where;
{
	int fd;
	struct chstatus chst;
	struct chroute_data *rdata;
	char c = 'c';

	fd = chopen(host, "DUMP-ROUTING-TABLE", 1, 1, 0, 0, 5);

	if (fd < 0) {
		fprintf(stderr, "chroute: can't open connection\n");
		exit(1);
	}

	ioctl(fd, CHIOCSWAIT, CSRFCSENT);

	ioctl(fd, CHIOCGSTAT, &chst);

	switch (chst.st_ptype) {
	case ANSOP:
		break;		/* right answer, cadet! */

	case CLSOP:
		printf("Connection rejected by %s.\n", chaos_name(host));
		return (1);

	case 0:
		printf("%s not responding.\n", chaos_name(host));
		return (1);

	default:
		printf("Unknown packet type 0%o\n", chst.st_ptype);
		return (1);
	}

	ioctl(fd, CHIOCPREAD, where);
	return (0);
}

/*
 * chaos address of host
 * zero if failure
 */
unsigned short
chparse(host)
	char *host;
{
	unsigned short addr;

	addr = chaos_addr(host, 0);

	if (addr != 0)
		return (addr);

	if (host[0] == '0')
		sscanf(host, "%o", &addr);
	else
		sscanf(host, "%d", &addr);

	if (addr <= 0) {
		printf("chroute: host %s unknown\n", host);
		return (0);
	}
}

char *
chunparse(addr)
	unsigned short addr;
{
	struct host_entry *h, *chaos_entry();

	h = chaos_entry(addr);

	return (h != NULL ? h->host_name : NULL);
}

printtable(routes)
	struct chroute_data *routes;
{
	struct chroute_data *rdata;
	int i;
	char *host;

	/*
	 * packets consist of (addr, cost) pairs (of words), except
	 * for directly connected nets, which get something wierd
	 * should get ANS packet 
	 */

	/*
	 * output:  sn cost addr name 
	 */

	printf("Subnet  Cost     via\n");
	printf("------  ---- -----------\n");

	for (rdata = (struct chroute_data *)routes, i = 0; i < CHMAXDATA / 4; rdata++, i++) {
		if (rdata->addr == 0)
			continue;	/* no route for this subnet */

		/*
		 * subnet, cost 
		 */
		printf("%4o  %4d  ", i, rdata->cost);

		/*
		 * check if direct (subnet 0, host is iface num << 1 + 1) 
		 */
		if ((rdata->addr & 0177400) == 0)
			printf("Interface %d\n", rdata->addr >> 1);
		else {
			/*
			 * not direct; look up name 
			 */
			host = chunparse(rdata->addr);
			if (host != NULL)
				printf("Bridge %s at %o\n", host, rdata->addr);
			else
				printf("Bridge %o\n", rdata->addr);
		}
	}
}
