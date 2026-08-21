/* readmcr --- dump a microcode (MCR) file
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

static bool showimem;
static bool showamem;
static bool showdmem;
static bool printloc = true;
static bool printuinsn = true;
static char *symfn;
static int promfd = -1;
static char *promfn;
static symtab_t symmcr;

static char *
getlbl(symtype_t type, int loc, int *offset)
{
	char *lbl;

	if (symfn == NULL)
		return "";
	lbl = sym_find_by_type_val(&symmcr, type, loc, offset);
	if (lbl == NULL)
		return "";
	return lbl;
}

static void
write_to_promfd_word(uint16_t w) {
    char buffer[7];
    sprintf(buffer, "%02x\x0A%02x\x0A", w & 0xff, (w>>8) & 0xff);
    write(promfd, buffer, 6 * sizeof(char));
}

static int
num_set_bits(uint16_t n) {
    int count = 0;
    for (int i = 0; i < 16; i++) {
        if ((n & 0x0001) == 1) count++;
        n = n >> 1;
    }
    return count;
}

static void
write_to_promfd(uint16_t w1, uint16_t w2, uint16_t w3) {
    // discard the 46th bit
    w3 = w3 & 0xBFFF;
    // shift 47th bit to 46th position
    w3 = ((w3 & 0x8000) >> 1) | (w3 & 0x3FFF);
    int count1 = num_set_bits(w1);
    int count2 = num_set_bits(w2);
    int count3 = num_set_bits(w3);
    int count = count1 + count2 + count3;
    // if even number of set bits, then set parity bit
    if ((count & 0x01) == 0) w3 = 0x8000 | w3;
    write_to_promfd_word(w1);
    write_to_promfd_word(w2);
    write_to_promfd_word(w3);
}

static void
dump_i_mem(int fd, int start, int size)
{
	int loc;

	printf("i-memory; start %o, size %o\n", start, size);
	loc = start;
    if (promfd != -1) {
        // write zeroes if it doesnt start from 0
        for (int i = 0; i < start; i++) {
            write_to_promfd(0x00, 0x00, 0x00);
        }
    }
	for (int i = 0; i < size; i++) {
		uint16_t w1;
		uint16_t w2;
		uint16_t w3;
		uint16_t w4;
		uint64_t ll;

		w1 = read16le(fd);
		w2 = read16le(fd);
		w3 = read16le(fd);
		w4 = read16le(fd);
		ll = ((uint64_t) w1 << 48) | ((uint64_t) w2 << 32) | ((uint64_t) w3 << 16) | ((uint64_t) w4 << 0);
        if (promfd != -1) {
            write_to_promfd(w4, w3, w2);
        }
		if (showimem) {
			char *l;
			int offset;

			l = getlbl(IMEM, loc, &offset);
			if (offset == 0 && strlen(l) > 0)
				printf("%s:\n", l);
			if (printloc == true) {
				if (printuinsn == true)
					printf("%05o %016" PRIo64 ":\t %s\n", loc, ll, uinst_desc(ll, &symmcr));
				else
					printf("%05o\t %s\n", loc, uinst_desc(ll, &symmcr));
			} else {
				if (printuinsn == true)
					printf("%016" PRIo64 ":\t %s\n", ll, uinst_desc(ll, &symmcr));
				else
					printf("\t %s\n", uinst_desc(ll, &symmcr));
			}
		}
		loc++;
	}
}

static void
dump_d_mem(int fd, int start, int size)
{
	printf("d-memory; start %o, size %o\n", start, size);
	if (size != 04000)
		errx(1, "d-mem is not exactly 4000 words: %o", size);
	for (int i = 0; i < size; i++) {
		unsigned int v;

		v = read32pdp(fd);
		if (showdmem == true)
			printf("%o <- %o\t%s\n", i, v, getlbl(DMEM, i, NULL));
	}
}

static void
dump_main_mem(int fd, int start, int size)
{
	printf("main-memory; start %o, size %o\n", start, size);
	read32pdp(fd);
	lseek(fd, 0, SEEK_CUR);
}

static void
dump_a_mem(int fd, int start, int size)
{
	printf("a-memory; start %o, size %o\n", start, size);
	for (int i = 0; i < size; i++) {
		unsigned int v;

		v = read32pdp(fd);
		if (showamem == true)
			printf("%o <- %o\t%s %s\n", i, v, getlbl(AMEM, i, NULL), getlbl(MMEM, i, NULL));
	}
}

static void
usage(void)
{
	fprintf(stderr, "usage: readmcr FILE\n");
	fprintf(stderr, "dump a microcode file\n");
	fprintf(stderr, "\n");
	fprintf(stderr, "  -i             show I memory (microcode) section\n");
	fprintf(stderr, "  -a             show A memory section\n");
	fprintf(stderr, "  -d             show D memory section\n");
	fprintf(stderr, "  -s FILE        decode labels from a symbol file\n");
	fprintf(stderr, "  -p FILE        write I memory section to (prom hex) FILE\n");
	fprintf(stderr, "  -n             do not print location\n");
	fprintf(stderr, "  -N             do not print raw microcode instruction\n");
	fprintf(stderr, "  -h             show help message\n");
}

int
main(int argc, char *argv[])
{
	int c;
	int fd;
	bool done;

	showimem = false;
	showamem = false;
	showdmem = false;
	symfn = NULL;
    promfn = NULL;
	while ((c = getopt(argc, argv, "p:iads:nNh")) != -1) {
		switch (c) {
        case 'p':
            promfn = strdup(optarg);
            break;
		case 'i':
			showimem = true;
			break;
		case 'a':
			showamem = true;
			break;
		case 'd':
			showdmem = true;
			break;
		case 's':
			symfn = optarg;
			sym_read_file(&symmcr, symfn);
			break;
		case 'n':
			printloc = false;
			break;
		case 'N':
			printuinsn = false;
			break;
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
	if (argc != 1) {
		usage();
		exit(1);
	}
	fd = open(argv[0], O_RDONLY);
	if (fd == -1) {
		fprintf(stderr, "%s: no such file or directory\n", argv[0]);
		exit(1);
	}
    if (promfn != NULL) 
    {
        promfd = open(promfn, O_WRONLY | O_CREAT | O_TRUNC, S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP);
        if (promfd == -1) {
            perror(promfn);
            exit(1);
        }
    }
	done = false;
	while (!done) {
		int code;
		int start;
		int size;

		code = read32pdp(fd);
		start = read32pdp(fd);
		size = read32pdp(fd);
        switch (code) {
        case 1:
            dump_i_mem(fd, start, size);
            break;
        case 2:
            dump_d_mem(fd, start, size);
            break;
        case 3:
            dump_main_mem(fd, start, size);
            break;
        case 4:
            dump_a_mem(fd, start, size);
            done = true;
            break;
        default:
            errx(1, "unknown section code: %o", code);
            break;
        }
	}
    if (promfd != -1) close(promfd);
    if (promfn != NULL) free(promfn);
	exit(0);
}
