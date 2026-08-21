/* showmcr --- decode a octal value as a CADR microcode instruction
 */

#include <err.h>
#include <fcntl.h>
#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "misc.h"
#include "udiss.h"
#include "usym.h"

static void
usage(void)
{
	fprintf(stderr, "usage: showmcr N ...\n");
	fprintf(stderr, "decode a seriesof octal values as CADR instructions\n");
	fprintf(stderr, "\n");
	fprintf(stderr, "  -h             show help message\n");
}

int
main(int argc, char *argv[])
{
	int c;

	while ((c = getopt(argc, argv, "h")) != -1) {
		switch (c) {
		case 'h':
			usage();
			exit(0);
		default:
			usage();
			exit(1);
		}
	}
	argc -= optind;
	argv += optind;
	if (argc < 1) {
		usage();
		exit(1);
	}

	for (int loc = 0; loc < argc; loc++) {
		uint64_t ll = strtol(argv[loc], NULL, 8);
		printf("%05o %016" PRIo64 ":\t %s\n", loc, ll, uinst_desc(ll, NULL));
	}

	exit(0);
}
