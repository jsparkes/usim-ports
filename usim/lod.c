/* lod --- describe a load band (LOD) file
 */

#include <assert.h>
#include <err.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "usim.h"
#include "qcom.h"
#include "misc.h"
#include "unfasl.h"

static struct {
	char *name;
	uint32_t a;
	uint32_t v;
} scratch_pad[] = {
	{"A-INITIAL-FEF", 0, 0},
	{"A-QTRSTKG", 0, 0},
	{"A-QCSTKG", 0, 0},
	{"A-QISTKG", 0, 0},
	{(char *)0, 0, 0}
};

static int lispm_major = (LISPM_SYSTEM / 1000U) % 10;
static int lispm_major1 = (LISPM_SYSTEM / 100U) % 10;
static int lispm_minor = (LISPM_SYSTEM / 10U) % 10;
static int lispm_minor1 = (LISPM_SYSTEM / 10U) % 10;

static int lodfd;

__attribute__((unused)) static uint32_t bbuf[256] = { 0 };

static uint32_t area_origin_pntr;
static uint32_t initial_fef;

extern char *strdtp(int);

#define read_virt(addr) read_virt_fd(lodfd, addr, 0)
#define read_string(a) read_virt_string_fd(lodfd, a, 0)

static char *
disassemble_object_output(uint32_t fefptr, uint32_t delta)
{
	uint32_t v;
	uint32_t tag;

	v = read_virt(fefptr + delta);
	tag = DATA_TYPE(v);
	switch (tag) {
	case DTP_SYMBOL:	/* ---!!! System 99 uses this? */
		v = read_virt(v);
		if (DATA_TYPE(v) != DTP_SYMBOL_HEADER)
			return NULL;
		return read_string(v);
	case DTP_FIX: {
		static char s[256];

		sprintf(s, "%o", ldb(Q_POINTER_, v));
		return s;
	}
	case DTP_EXTERNAL_VALUE_CELL_POINTER: {
		static char s[256];

		v = read_virt(v);
		if (DATA_TYPE(v) != DTP_FEF_POINTER)
			return NULL;
		v = read_virt(v + 2);
		if (DATA_TYPE(v) != DTP_SYMBOL)
			return NULL;
		v = read_virt(v);
		if (DATA_TYPE(v) != DTP_SYMBOL_HEADER)
			return NULL;
		sprintf(s, "#'%s", read_string(v));
		return s;
	}
	case DTP_U_ENTRY:
		return "DTP-U-ENTRY";
	case DTP_FEF_POINTER:
		return "DTP-FEF-POINTER";
	case DTP_LIST:
		return "DTP-LIST";
	case DTP_ARRAY_POINTER:
		return "DTP-ARRAY-POINTER";
	default:
		errx(1, "unknown tag %d (0%o)", tag, tag);
		return NULL;
	}
}

/*
 * See SYS;SYS:QMISC LISP for the canonical definition of
 * DESCRIBE-NUMERIC-DESCRIPTOR-WORD and DESCRIBE-FEF.
 */

static void
describe_numeric_descriptor_word(int n)
{
	int min;
	int max;

	printf("\n  ");
	if (ARG_DESC_QUOTED_REST & n)
		printf("Quoted rest arg, ");
	if (ARG_DESC_EVALED_REST & n)
		printf("Evaluated rest arg, ");
	if (ARG_DESC_FEF_QUOTE_HAIR & n)
		printf("Some args quoted, ");
	if (ARG_DESC_INTERPRETED & n)
		printf("Interpreted function, ");
	if (ARG_DESC_FEF_BIND_HAIR & n)
		printf("Linear enter must check ADL, ");
	max = ldb(ARG_DESC_MAX_ARGS_, n);
	min = ldb(ARG_DESC_MIN_ARGS_, n);
	if (max == min)
		printf("%o args.", max);
	else
		printf("Takes between %o and %o args.", min, max);
}

static void
describe_fef(int fef)
{
	uint32_t header;
	uint32_t length;
	uint32_t name;
	uint32_t fast_arg;
	uint32_t sv;
	uint32_t misc;

	assert(DATA_TYPE(fef) == DTP_FEF_POINTER);
	header = read_virt(fef);
	length = read_virt(fef + FEFHI_STORAGE_LENGTH);
	assert(DATA_TYPE(length) == DTP_FIX);
	name = read_virt(fef + FEFHI_FCTN_NAME);
	fast_arg = read_virt(fef + FEFHI_FAST_ARG_OPT);
	sv = read_virt(fef + FEFHI_SV_BITMAP);
	misc = read_virt(fef + FEFHI_MISC);
	printf("The FEF for function %s (%011o)\n", read_string(read_virt(name)), fef);
	printf("Initial relative PC: %o halfwords.\n", ldb(FEFH_PC_, header));
	/*
	 * -- Print out the fast arg option
	 */
	printf("The Fast Argument Option is ");
	if (ldb(FEFH_FAST_ARG, header) == 0)
		printf("not active, but here it is anyway:");
	else
		printf("active:");
	describe_numeric_descriptor_word(fast_arg);
	printf("\n");
	/*
	 * -- Randomness.
	 */
	printf("The length of the local block is %o\n", ldb(FEFHI_MS_LOCAL_BLOCK_LENGTH_, misc));
	printf("The total storage length of the FEF is %o\n", ldb(Q_POINTER_, length));
	/*
	 * -- Special variables
	 */
	if (ldb(FEFH_SV_BIND_, header) == 0)
		printf("There are no special variables present.");
	else {
		printf("There are special variables, ");
		if (ldb(FEFHI_SVM_ACTIVE_, sv) == 0)
			printf("but the S-V bit map is not active.");
		else
			printf("and the S-V bit map is active and contains: %o", ldb(FEFHI_SVM_BITS_, sv));
	}
	printf("\n");
	/*
	 * -- ADL.
	 */
	if (ldb(FEFH_NO_ADL_, header) == 0) {
		printf("There is an ADL:  It is %o long, and starts at %o\n", ldb(FEFHI_MS_BIND_DESC_LENGTH_, misc), ldb(FEFHI_MS_ARG_DESC_ORG_, misc));
	} else {
		printf("There is no ADL.\n");
	}
}

static int
disassemble_fef(uint32_t fef)
{
	uint32_t header;
	uint32_t length;
	unsigned short ib[512];
	uint32_t o;
	int j;
	int tag;
	int icount;
	uint32_t max;

	header = read_virt(fef);
	tag = DATA_TYPE(header);
	if (tag != DTP_HEADER)
		errx(1, "%011o: address does not point (%011o) to a DTP-HEADER", fef, header);
	length = read_virt(fef + FEFHI_STORAGE_LENGTH);
	assert(DATA_TYPE(length) == DTP_FIX);
	o = ldb(FEFH_PC_, header);
	max = ldb(Q_POINTER_, length);
	/*
	 * Populate buffer with all (FEF, macrocode, etc) insn.
	 */
	j = 0;
	for (uint32_t i = 0; i < max; i++) {
		uint32_t inst;

		inst = read_virt(fef + i);
		ib[j++] = inst;
		ib[j++] = inst >> 16;
	}
	/*
	 * Dump macroinstruction portion of this function.
	 */
	icount = (max - o / 2) * 2;
	for (uint32_t i = o; i < o + (icount - 1); i++) {
		uint32_t loc;

		loc = fef + i / 2;
		printf("\t%s\n", disassemble_instruction(fef, loc, ib[i], 0UL));
	}
	return 0;
}

static void
describe_syscom(void)
{
	printf("SYSTEM-COMMUNICATION-AREA-QS (%011o):\n", 0400);
	for (int i = 0; system_communication_area_qs[i].name; i++) {
		system_communication_area_qs[i].a = 0400 + i;
		system_communication_area_qs[i].v = read_virt(system_communication_area_qs[i].a);
		printf("\t%s (%011o): %011o (%s)\n", system_communication_area_qs[i].name, system_communication_area_qs[i].a, system_communication_area_qs[i].v, strdtp(DATA_TYPE(system_communication_area_qs[i].v)));
	}
}

static void
describe_scratch_pad(void)
{
	printf("\nSCRATCH-PAD (%011o):\n", 01000);
	for (int i = 0; scratch_pad[i].name; i++) {
		scratch_pad[i].a = 01000 + i;
		scratch_pad[i].v = read_virt(scratch_pad[i].a);
		printf("\t%s (%011o): %011o (%s)\n", scratch_pad[i].name, scratch_pad[i].a, scratch_pad[i].v, strdtp(DATA_TYPE(scratch_pad[i].v)));
	}
}

static void
describe_area_list(void)
{
	printf("\nAREA-LIST (%011o):\n", area_origin_pntr);
	for (int i = 0; area_list[i].name; i++) {
		area_list[i].a = area_origin_pntr + i;
		area_list[i].v = read_virt(area_list[i].a);
		printf("\t%s (%011o): %011o\n", area_list[i].name, area_list[i].a, area_list[i].v);
	}
}

static void
disassemble_mem(int addr)
{
	for (int i = -256; i < 256; i++) {
		uint32_t v;
		int tag;

		v = read_virt(addr + i);
		tag = DATA_TYPE(v);
		printf("\t%c%011o: %011o\t", addr == (addr + i) ? '*' : ' ', addr + i, v);
		char *dtpname = strdtp(tag);
		if (dtpname == NULL)
			printf("<unknown dtp>");
		else
			printf("%s", dtpname);
		printf("\n");
	}
}

static void
usage(void)
{
	fprintf(stderr, "usage: lod [OPTION]... FILE\n");
	fprintf(stderr, "describe a load band (LOD) file");
	fprintf(stderr, "\n");
	fprintf(stderr, "  -p ADDR        disassemble function at address ADDR (default: A-INITIAL-FEF)\n");
	fprintf(stderr, "  -m ADDR        dump surrounding memory at address ADDR\n");
	fprintf(stderr, "  -h             help message\n");
}

int
main(int argc, char *argv[])
{
	int c;
	uint32_t fef_addr;
	uint32_t mem_addr;
	int mflg;

	fef_addr = 0;
	mem_addr = 0;
	mflg = 0;
	disassemble_object_output_fun = &disassemble_object_output;
	printf("Dumping load band using using System %d%d.%d%d definitions (%d-bit pointers).\n", lispm_major, lispm_major1, lispm_minor, lispm_minor1, Q_POINTER_WIDTH);
	while ((c = getopt(argc, argv, "p:m:wh")) != -1) {
		switch (c) {
		case 'p':
			sscanf(optarg, "%o", &fef_addr);
			break;
		case 'm':
			sscanf(optarg, "%o", &mem_addr);
			mflg++;
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
	lodfd = open(argv[0], O_RDONLY);
	if (lodfd < 0) {
		perror(argv[0]);
		exit(1);
	}
	area_origin_pntr = read_virt(0400);
	describe_syscom();
	describe_scratch_pad();
	describe_area_list();
	initial_fef = read_virt(scratch_pad[0].v);
	if (fef_addr == 0)
		fef_addr = initial_fef;
	printf("\n");
	if (mflg == 0) {
		describe_fef(fef_addr);
		printf("\n");
		disassemble_fef(fef_addr);
	} else
		disassemble_mem(mem_addr);
	exit(0);
}
