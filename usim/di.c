/* di --- QFASL macrocode instruction disassembler
 */

#include <fcntl.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

#include "unfasl.h"
#include "usim.h"

static int lispm_major = (LISPM_SYSTEM / 1000U) % 10;
static int lispm_major1 = (LISPM_SYSTEM / 100U) % 10;
static int lispm_minor = (LISPM_SYSTEM / 10U) % 10;
static int lispm_minor1 = (LISPM_SYSTEM / 10U) % 10;

static void
usage(void)
{
	fprintf(stderr, "usage: di [OPTION]... QFASL-LC-INSN...\n");
	fprintf(stderr, "QFASL macrocode instruction disassembler\n");
	fprintf(stderr, "\n");
	fprintf(stderr, "  -h             help message\n");
}

int
main(int argc, char *argv[])
{
	int c;

	printf("Using System %d%d.%d%d definitions (%d-bit pointers).\n", lispm_major, lispm_major1, lispm_minor, lispm_minor1, Q_POINTER_WIDTH);
	while ((c = getopt(argc, argv, "")) != -1) {
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

	for (int i = 0; i < argc; i++) {
		int insn;
		char *end;
		char *s;

		insn = strtoul(argv[i], &end, 8);
		s = disassemble_instruction(0, i, insn, 0);
		printf("%s\n", s);
	}

	exit(0);
}
