/*
 * listen.c
 *
 * basic listening node for chaosd server
 * decodes protocol and prints out packets
 */

#include <sys/stat.h>
#include <sys/time.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "chaos.h"
#include "chconf.h"
#include "chsys.h"
#include "../chncp/chncp.h"

#include "chaosd.h"
#include "misc.h"

int show_contents;
int relative_time;
int fd;
unsigned char buffer[4096];

void
decode_chaos(char *buffer, int len)
{
	struct pkt_header *ph = (struct pkt_header *)buffer;
	time_t t;
	struct tm *tm;
	struct timeval tv;
	int ms;

	if (!relative_time) {
#if 0
		t = time(NULL);
		ms = 0;
#else
		gettimeofday(&tv, NULL);
		t = tv.tv_sec;
		ms = tv.tv_usec / 1000;
#endif
		tm = localtime(&t);
		printf("%02d:%02d:%02d.%03d ", tm->tm_hour, tm->tm_min, tm->tm_sec, ms);
	} else {
		/*
		 * relative time
		 */
		static struct timeval tv_last;
		struct timeval tv1, tv2;

		gettimeofday(&tv1, NULL);
		if (tv_last.tv_sec == 0) {
			tv2.tv_sec = tv2.tv_usec = 0;
		} else {
			if (tv_last.tv_usec <= tv1.tv_usec) {
				tv2.tv_usec = tv1.tv_usec - tv_last.tv_usec;
				tv2.tv_sec = tv1.tv_sec - tv_last.tv_sec;
			} else {
				tv2.tv_usec = (tv1.tv_usec + 1 * 1000 * 1000)
					- tv_last.tv_usec;
				tv2.tv_sec = (tv1.tv_sec - 1) - tv_last.tv_sec;
			}
		}
		printf("%4ld:%06ld ", tv2.tv_sec, tv2.tv_usec);
		tv_last = tv1;
	}
	if (ph->ph_op > 0 && ph->ph_op <= BRDOP)
		printf("%s ", chopstr(ph->ph_op));
	else
		printf("%02x ", ph->ph_op);
	printf("to (%o %o:%d,%d) ", ph->ph_daddr.subnet, ph->ph_daddr.host, ph->ph_didx.tidx, ph->ph_didx.uniq);
	printf("from (%o %o:%d,%d) ", ph->ph_saddr.subnet, ph->ph_saddr.host, ph->ph_sidx.tidx, ph->ph_sidx.uniq);
	printf("pkn %o ackn %o ", ph->ph_pkn, ph->ph_ackn);
	printf("len %d ", len);
	printf("\n");
	if (show_contents)
		dumpmem((unsigned char *)buffer, len);
	if (0) {
		printf("  opcode %04x %s\n", ph->ph_op, chopstr(ph->ph_op));
		printf("  daddr %o (%o %o), tidx %d, unique %d\n", CH_ADDR_SHORT(ph->ph_daddr), ph->ph_daddr.subnet, ph->ph_daddr.host, ph->ph_didx.tidx, ph->ph_didx.uniq);
		printf("  saddr %o (%o %o), tidx %d, uniq %d\n", CH_ADDR_SHORT(ph->ph_saddr), ph->ph_saddr.subnet, ph->ph_saddr.host, ph->ph_sidx.tidx, ph->ph_sidx.uniq);
	}
	fflush(stdout);
}

int
read_chaos(int fd)
{
	int ret, len;
	unsigned char lenbytes[4];

	ret = read(fd, lenbytes, 4);
	if (ret <= 0) {
		return -1;
	}
	len = (lenbytes[0] << 8) | lenbytes[1];
	ret = read(fd, buffer, len);
	if (ret <= 0)
		return -1;
	decode_chaos((char *)buffer, len);
	return 0;
}

void
usage(void)
{
	fprintf(stderr, "listen - display chaosnet traffic\n");
	fprintf(stderr, "usage:\n");
	fprintf(stderr, "-a         show absolute time\n");
	fprintf(stderr, "-s         show packet contents\n");
	exit(1);
}

extern char *optarg;

int
main(int argc, char *argv[])
{
	int c;

	relative_time = 1;
	while ((c = getopt(argc, argv, "as")) != -1) {
		switch (c) {
		case 'a':
			relative_time = 0;
			break;
		case 's':
			show_contents++;
			break;
		default:
			usage();
		}
	}
	fd = chdopen();
	if (fd == -1) {
		exit(1);
	}
	while (1) {
		if (read_chaos(fd))
			break;
	}
	exit(0);
}
