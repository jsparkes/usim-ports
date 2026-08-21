/* dump.c --- All dump and state save/restore routines
 */

#include <assert.h>
#include <err.h>
#include <fcntl.h>
#include <semaphore.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

#include "config.h"
#include "diagnostic-interface.h"
#include "dump.h"
#include "machine-control.h"
#include "main-memory.h"
#include "misc.h"
#include "tv.h"
#include "ucfg.h"
#include "ucode.h"
#include "udiss.h"
#include "usim.h"
#include "usym.h"
#include "utrace.h"
#include "uvmem.h"

uint32_t full_trace_lc = 0;
uint32_t full_trace_repeat_counter = 0;
uint32_t full_trace_last_lc = 0;

void
save_state_to_numbered_file(char *suffix, uint32_t info)
{
    char filename[1024] = {0};
	static int dump_nr = 0;
	snprintf(filename, sizeof(filename), "usim-%03d-%s-%u.state", dump_nr++, suffix, info);
	save_state(filename);
	printf(".. dumped to file %s\n", filename);
}

static void
printlbl(symtype_t type, uint32_t loc, bool loc_imem)
{
	char *l;
	int offset;

	l = sym_find_by_type_val(loc_imem ? &sym_mcr : &sym_prom, type, loc, &offset);
	if (l == NULL) {
		printf("%03o", loc);
	} else {
		if (offset == 0)
			printf("(%s)", l);
		else
			printf("(%s %o)", l, offset);
	}
}

// PC HISTORY

#define MAX_PC_HISTORY 4096

struct
{
	uint32_t pc;
    bool pc_imem;
} pc_history[MAX_PC_HISTORY];

size_t pc_history_head = 0;

void
record_pc_history(uint32_t pc, bool pc_imem)
{
	pc_history[pc_history_head].pc = pc;
	pc_history[pc_history_head].pc_imem = pc_imem;
	pc_history_head = (pc_history_head + 1) % MAX_PC_HISTORY;
}

// if length == 0, prints all history
static void
show_pc_history(bool verbose, size_t length)
{
	printf("Micro PC History (OPC's), oldest first:	\n");

    size_t head = pc_history_head;

    // if length is given, move back in time by moving head back length items
    // since the newest is at head-1
    // we should start from head-length, so
    // head-length, head-length+1, .... head-1, there are length items
    if (length > 0)
    {
        head = pc_history_head - length;
        if (head < 0) head += MAX_PC_HISTORY;
        assert (head >= 0);
    }

    for (size_t i = 0; i < MAX_PC_HISTORY; i++) 
    {
        if ((length > 0) && (i == length)) break;

        const uint32_t pc = pc_history[head].pc;
        const bool pc_imem = pc_history[head].pc_imem;

        head = (head + 1) % MAX_PC_HISTORY;
        
        // pc=0 means this element is not (and never) recorded
        // so find one recorded, this is the oldest record
        if (pc == 0) continue;

        printf("   %05o\t", pc);
        printlbl(IMEM, pc, pc_imem);
        if (!pc_imem) printf("\t...in the PROM.");
        printf("\n");

        if (verbose || (length > 0)) {
            printf("\t%s\n", disassemble_pc2(pc, pc_imem));
        }
    }
}

// LC HISTORY

#define MAX_LC_HISTORY 20000

struct
{
	unsigned short instr;
	uint32_t lc;
} lc_history[MAX_LC_HISTORY];

size_t lc_history_head = 0;

void
record_lc_history(void)
{
	unsigned int instr;
	{
		bool opff = machine_state.vmaok;
		vmRead(lc >> 2, &instr);
		machine_state.vmaok = opff;
	}
	lc_history[lc_history_head].instr = (lc & 2) ? (instr >> 16) & 0xffff : (instr & 0xffff);
	lc_history[lc_history_head].lc = lc;
	lc_history_head = (lc_history_head + 1) % MAX_LC_HISTORY;
}

static void
show_lc_history(void)
{
	int head;

	printf("Complete backtrace follows:\n\n");
	head = lc_history_head;
	for (int i = 0; i < MAX_LC_HISTORY; i++) {
		unsigned short instr;
		uint32_t loc;

		instr = lc_history[head].instr;
		loc = lc_history[head].lc & 0377777777;
		head = (head + 1) % MAX_LC_HISTORY;
		/*
		 * Skip printing out obviously empty entries.
		 */
		if (loc == 0 && instr == 0)
			continue;
#if 1
		printf("%11o %06o\n", loc, instr);
#else
		/* This is still slightly buggy and causes crashes. */
		printf("\t%s\n", disassemble_instruction(0, loc, instr, 0UL));
#endif
	}
	printf("\n");
}

static void
show_spc_stack(void)
{
	if (spcptr == 0)
		return;
	printf("Backtrace of microcode subroutine stack:\n");
	for (int i = spcptr; i >= 0; i--) {
		uint32_t pc;
		pc = spc[i] & 037777;
		printf("%2o %011o ", i, spc[i]);
		printlbl(IMEM, pc, true);
		printf("\n");
	}
}

static void
show_mmem(void)
{
	printf("M-MEM:\n");
	/* *INDENT-OFF* */
	for (int i = 0; i < 32; i += 4) {
		printf("\tM[%02o] %011o %011o %011o %011o\n", i,
		    mmem[i + 0], mmem[i + 1],
		    mmem[i + 2], mmem[i + 3]);
	}
	/* *INDENT-ON* */
	printf("\n");
}

static void
show_amem(void)
{
	printf("A-MEM:\n");
	/* *INDENT-OFF* */
	for (int i = 0; i < 1024; i += 4) {
		int skipped;

		printf("\tA[%04o] %011o %011o %011o %011o\n", i,
		    amem[i + 0], amem[i + 1],
		    amem[i + 2], amem[i + 3]);
		skipped = 0;
		while (
			amem[i + 0] == amem[i + 0 + 4] &&
			amem[i + 1] == amem[i + 1 + 4] &&
			amem[i + 2] == amem[i + 2 + 4] &&
			amem[i + 3] == amem[i + 3 + 4] &&
			i < 1024) {
			if (skipped == 0)
				printf("\t...\n");
			skipped++;
			i += 4;
		}
	}
	/* *INDENT-ON* */
	printf("\n");
}

static void
show_ammem_sym(void)
{
	printf("A/M-MEMORY BY SYMBOL:\n");
	for (int i = 0; i < 1024; i++) {
		char *l;

		l = sym_find_by_type_val(machine_state.promdisabled ? &sym_mcr : &sym_prom, 
                AMEM, i, NULL);
		if (l != NULL) {
			printf("\t%04o %-40s %011o", i, l, amem[i]);
			if (i < 32) {
				l = sym_find_by_type_val(machine_state.promdisabled ? &sym_mcr : &sym_prom, 
                        MMEM, i, NULL);
				if (l != NULL) {
					printf("  %-40s %011o", l, mmem[i]);
				}
			}
			printf("\n");
		}
	}
	printf("\n");
}

static void
show_spc(void)
{
	printf("SPC STACK:\n");
	printf("\tSPC POINTER: %o\n", spcptr);
	/* *INDENT-OFF* */
	for (int i = 0; i < 32; i += 4) {
		printf("\tSPC[%02o] %011o %011o %011o %011o\n", i,
		    spc[i + 0], spc[i + 1],
		    spc[i + 2], spc[i + 3]);
	}
	/* *INDENT-ON* */
	printf("\n");
}

static void
show_pdl(void)
{
	printf("PDL MEMORY:\n");
	printf("\tPDL POINTER: %o, PDL INDEX: %o\n", pdl_pointer, pdl_index);
	/* *INDENT-OFF* */
	for (int i = 0; i < 1024; i += 4) {
		int skipped;

		printf("\tPDL[%04o] %011o %011o %011o %011o\n", i,
		    pdl[i + 0], pdl[i + 1],
		    pdl[i + 2], pdl[i + 3]);
		skipped = 0;
		while (
			pdl[i + 0] == pdl[i + 0 + 4] &&
			pdl[i + 1] == pdl[i + 1 + 4] &&
			pdl[i + 2] == pdl[i + 2 + 4] &&
			pdl[i + 3] == pdl[i + 3 + 4] &&
			i < 1024) {
			if (skipped == 0)
				printf("\t...\n");
			skipped++;
			i += 4;
		}
	}
	/* *INDENT-ON* */
	printf("\n");
}

static void
show_l1_map(void)
{
	printf("L1 MAP:\n");
	/* *INDENT-OFF* */
	for (int i = 0; i < 2048; i += 4) {
		int skipped;

		printf("\tL1[%04o] %011o %011o %011o %011o\n", i,
		    l1_map[i + 0], l1_map[i + 1],
		    l1_map[i + 2], l1_map[i + 3]);
		skipped = 0;
		while (
			l1_map[i + 0] == l1_map[i + 0 + 4] &&
			l1_map[i + 1] == l1_map[i + 1 + 4] &&
			l1_map[i + 2] == l1_map[i + 2 + 4] &&
			l1_map[i + 3] == l1_map[i + 3 + 4] &&
			i < 2048) {
			if (skipped == 0)
				printf("\t...\n");
			skipped++;
			i += 4;
		}
	}
	/* *INDENT-ON* */
	printf("\n");
}

static void
show_l2_map(void)
{
	printf("L2 MAP:\n");
	/* *INDENT-OFF* */
	for (int i = 0; i < 1024; i += 4) {
		int skipped;

		printf("\tL2[%04o] %011o %011o %011o %011o\n", i,
		    l2_map[i + 0], l2_map[i + 1],
		    l2_map[i + 2], l2_map[i + 3]);
		skipped = 0;
		while (
			l2_map[i + 0] == l2_map[i + 0 + 4] &&
			l2_map[i + 1] == l2_map[i + 1 + 4] &&
			l2_map[i + 2] == l2_map[i + 2 + 4] &&
			l2_map[i + 3] == l2_map[i + 3 + 4] &&
			i < 1024) {
			if (skipped == 0)
				printf("\t...\n");
			skipped++;
			i += 4;
		}
	}
	/* *INDENT-ON* */
	printf("\n");
}



// PDL HISTORY

#define MAX_PDL_HISTORY 4096

struct
{
	int read_write_indexer_npc;
	int index;
	int value;
	uint32_t lc;
} pdl_history[MAX_PDL_HISTORY];

size_t pdl_history_next = 0;

#define USE_PDL_PTR 1
#define USE_PDL_INDEX 2

#define PDL_READ 0
#define PDL_WRITE 1
#define PDL_PUSH 2
#define PDL_POP 3

static void
trace_pdl(int read_write, int indexer, int value)
{
	pdl_history[pdl_history_next].read_write_indexer_npc = read_write << 24 | indexer << 16 | pc_history[(pc_history_head - 1 + MAX_PC_HISTORY) % MAX_PC_HISTORY].pc;
	pdl_history[pdl_history_next].index = (indexer == USE_PDL_PTR) ? pdl_pointer : pdl_index;
	pdl_history[pdl_history_next].value = value;
	pdl_history[pdl_history_next].lc = lc_history[(lc_history_head - 1 + MAX_LC_HISTORY) % MAX_LC_HISTORY].lc;
	pdl_history_next++;
	if (pdl_history_next >= MAX_PDL_HISTORY)
		pdl_history_next = 0;
}

void
trace_pdlptr_pop(int value)
{
	trace_pdl(PDL_POP, USE_PDL_PTR, value);
}

void
trace_pdlptr_push(int value)
{
	trace_pdl(PDL_PUSH, USE_PDL_PTR, value);
}

void
trace_pdlidx_read(int value)
{
	trace_pdl(PDL_READ, USE_PDL_INDEX, value);
}

void
trace_pdlptr_read(int value)
{
	trace_pdl(PDL_READ, USE_PDL_PTR, value);
}

void
trace_pdlidx_write(int value)
{
	trace_pdl(PDL_WRITE, USE_PDL_INDEX, value);
}

void
trace_pdlptr_write(int value)
{
	trace_pdl(PDL_WRITE, USE_PDL_PTR, value);
}


void
check_enable_full_tracing_after_lc(uint32_t lc)
{
    if (full_trace_lc > 0
            && (lc & 0xffffff) == full_trace_lc
            && ++full_trace_repeat_counter == 4
            && full_trace_last_lc != (lc & 0xffffff)) 
    {
        printf("Enabling full tracing at lc #x%x\n", lc);
        trace_level = LOG_DEBUG;
        trace_facilities = TRACE_UCODE | TRACE_MICROCODE;
    }
    full_trace_last_lc = lc;
}

// LC DUMP VALUES

uint32_t lc_dump_values[256] = { 0 };
size_t lc_dump_index = 0;

void
add_dump_lc(uint32_t lc)
{
	printf("adding lc dump value #x%x\n", lc);
	lc_dump_values[lc_dump_index++] = lc;
}

void
check_lc_dump(uint32_t lc)
{
	for (size_t i = 0; i < lc_dump_index; i++) 
    {
		if (lc == lc_dump_values[i]) 
        {
			printf("LC reached #x%x, dumping state...\n", lc);
            save_state_to_numbered_file("lc", lc);
		}
	}
}

// NPC DUMP VALUES

uint32_t npc_dump_values[256] = { 0 };
size_t npc_dump_index = 0;

void
add_dump_npc(uint32_t npc)
{
	printf("adding npc dump value #x%x\n", npc);
	npc_dump_values[npc_dump_index++] = npc;
}

void
check_npc_dump(uint32_t npc)
{
	for (size_t i = 0; i < npc_dump_index; i++) {
		if (npc == npc_dump_values[i]) {
            save_state_to_numbered_file("npc", npc);
		}
	}
}

// TRACE UCODE

void
trace_ucode(void)
{
	if (trace_level == LOG_DEBUG && (trace_facilities & TRACE_UCODE)) 
    {
		char *uinst = disassemble_pc2(p0, p0_imem);
		printf("%s", uinst);
		printlbl(IMEM, p0_pc, p0_imem);
		printf("\n");
	}
}

// TRACE VMEM

static uint32_t vmem_trace_values[256] = { 0 };
static size_t vmem_trace_index = 0;

void
add_trace_vmem(uint32_t vmem)
{
	printf("adding vmem trace value #x%x\n", vmem);
	vmem_trace_values[vmem_trace_index++] = vmem;
}

void
trace_memory_location(bool write, uint32_t vaddr, uint32_t *pv, int lc)
{
	uint32_t ptr = vaddr & 0xffffff;
	for (size_t i = 0; i < vmem_trace_index; i++) {
		if (ptr == vmem_trace_values[i]) {
			if (write)
            {
				printf("wr %x: %x lc %x\n", vaddr, *pv, lc);
            }
			else
            {
				printf("rd %x: %x lc %x\n", vaddr, *pv, lc);
            }
		}
	}
}

// SAVE/RESTORE STATE

#define DUMP_FILE_MAGIC		str4("LMDF")
#define DUMP_FILE_VERSION	0x0001

static bool restored = false;

void
restore_state(char *filename)
{
    if (filename == NULL) 
    {
        restore_state(usim_state_filename);
        return;
    }

	int fd;
	uint32_t magic;
	uint32_t version;
	int32_t s;

	if (restored == true) {
		DEBUG(TRACE_USIM, "mem: state already restored\n");
		return;
	}
	restored = true;
	NOTICE(TRACE_USIM, "usim: restoring state from %s\n", filename);
	fd = open(filename, O_RDONLY);
	if (fd < 0) {
		WARNING(TRACE_USIM, "usim: failed to open state file\n");
		return;
	}
	magic = read32le(fd);
	version = read32le(fd);
	if (magic != DUMP_FILE_MAGIC) {
		WARNING(TRACE_USIM, "usim: magic value in state file is not right\n");
		return;
	}
	if (version != DUMP_FILE_VERSION) {
		WARNING(TRACE_USIM, "usim: version value in state file is not right\n");
		return;
	}
	s = dump_find_segment(fd, str4("PMEM"));
    // cannot find PMEM ?
	if (s < 0)
		return;
    // size should be a multiple of page (256 words)
    if ((s & 0xFF) != 0)
    {
        WARNING(TRACE_USIM, "usim: the size of PMEM segment in state file is not a multiple of 256\n");
        return;
    }
    if (s > (NUMBER_OF_MAX_MAIN_MEMORY_PAGES * 256))
    {
        WARNING(TRACE_USIM, "usim: the size of PMEM segment in state file is greater than %d\n", NUMBER_OF_MAX_MAIN_MEMORY_PAGES);
        return;
    }
    // s is number of words
    uint32_t num_memory_pages = s / 256;
    // if required, change the installed memory so the dumped memory fits
    if (main_memory_npages != num_memory_pages)
    {
        NOTICE(TRACE_USIM, "usim: changing the number of physical memory pages to %d (was %d) to load the state file\n", 
                num_memory_pages,
                main_memory_npages);
        main_memory_npages = num_memory_pages;
    }
	for (uint32_t i = 0; i < main_memory_npages; i++) {
		main_memory_load_page_from_file(i, fd);
	}
	NOTICE(TRACE_USIM, "usim: restored %i physical pages\n", num_memory_pages);
	close(fd);
}

void
dump_state(bool verbose, size_t length)
{
    const size_t head = (pc_history_head - 1 + MAX_PC_HISTORY) % MAX_PC_HISTORY;
    const uint32_t pc = pc_history[head].pc;
    const bool pc_imem = pc_history[head].pc_imem;
	printf("***********************************************\n");
	printf("PC=%05o ", pc);
	printlbl(IMEM, pc, pc_imem);
    if (!pc_imem) printf("\t...in the PROM.");
	printf("\n");
    printf("IR=%s\n", disassemble_pc2(pc, pc_imem));
	show_pc_history(verbose, length);
	show_spc_stack();
	if (verbose) {
		show_lc_history();
		show_mmem();
		show_amem();
		show_ammem_sym();
		show_spc();
		show_pdl();
		show_l1_map();
		show_l2_map();
	}
}

static void
dump_ucode_things(int fd)
{
	dump_write_value(fd, str4("MCPC"), pc_history[(pc_history_head - 1 + MAX_PC_HISTORY) % MAX_PC_HISTORY].pc);	/* Microcode PC */
	dump_write_value(fd, str4("USTP"), spcptr);	/* Microcode stack pointer */
	dump_write_value(fd, str4("RMD_"), md_reg);	/* MD register */
	dump_write_value(fd, str4("RVMA"), vma_reg);	/* VMA register */
	dump_write_value(fd, str4("RQ__"), q);	/* Q register */
	dump_write_value(fd, str4("ROPC"), opc); /* OPC register */
	dump_write_value(fd, str4("ROAL"), oa_reg_low);	/* OA-REG-LOW register */
	dump_write_value(fd, str4("ROAH"), oa_reg_high);	/* OA-REG-HIGH register */
	dump_write_segment(fd, str4("DMEM"), sizeof(dmem) / 4, (uint32_t *) dmem);	/* Dispatch memory */
	dump_write_segment(fd, str4("IMEM"), sizeof(imem) / 4, (uint32_t *) imem);	/* Instruction memory */
	dump_write_segment(fd, str4("USTK"), sizeof(spc) / 4, (uint32_t *) spc);	/* Microcode stack */
	dump_write_header(fd, str4("PCHL"), MAX_PC_HISTORY);	/* Microcode PC history list */
	for (uint32_t i = 0; i < MAX_PC_HISTORY; i++) {
		write32le(fd, pc_history[(pc_history_head - i - 1 + MAX_PC_HISTORY) % MAX_PC_HISTORY].pc);
	}
	dump_write_header(fd, str4("PDHL"), (4 * MAX_PDL_HISTORY));	/* PDL action history list */
	for (uint32_t i = 0; i < MAX_PDL_HISTORY; i++) {
		write32le(fd, pdl_history[(pdl_history_next - i - 1 + MAX_PDL_HISTORY) % MAX_PDL_HISTORY].read_write_indexer_npc);
		write32le(fd, pdl_history[(pdl_history_next - i - 1 + MAX_PDL_HISTORY) % MAX_PDL_HISTORY].index);
		write32le(fd, pdl_history[(pdl_history_next - i - 1 + MAX_PDL_HISTORY) % MAX_PDL_HISTORY].value);
		write32le(fd, pdl_history[(pdl_history_next - i - 1 + MAX_PDL_HISTORY) % MAX_PDL_HISTORY].lc);
	}
}

// requires current state + lc_history
void
save_state(char *filename)
{
    if (filename == NULL) 
    {
        save_state(usim_state_filename);
        return;
    }

	int fd = open(filename, O_WRONLY | O_CREAT | O_TRUNC, 0666);
	if (fd < 0) {
		WARNING(TRACE_USIM, "usim: failed to save state file %s\n", filename);
		return;
	}
	NOTICE(TRACE_USIM, "usim: dumping state to %s\n", filename);
	write32le(fd, DUMP_FILE_MAGIC);
	write32le(fd, DUMP_FILE_VERSION);
	dump_write_value(fd, str4("PDLI"), pdl_index);	/* PDL index */
	dump_write_value(fd, str4("PDLP"), pdl_pointer);	/* PDL pointer */
	dump_write_value(fd, str4("LCLV"), lc);	/* LC - last value */
	dump_write_header(fd, str4("LCHL"), MAX_LC_HISTORY);	/* LC - history list */
	for (uint32_t i = 0; i < MAX_LC_HISTORY; i++) {
		write32le(fd, lc_history[(lc_history_head - i - 1 + MAX_LC_HISTORY) % MAX_LC_HISTORY].lc);
	}
	dump_ucode_things(fd);
	dump_write_segment(fd, str4("L1MP"), sizeof(l1_map) / 4, (uint32_t *) l1_map);	/* Level 1 Memory Map */
	dump_write_segment(fd, str4("L2MP"), sizeof(l2_map) / 4, (uint32_t *) l2_map);	/* Level 2 Memory Map */
	dump_write_segment(fd, str4("PDLM"), sizeof(pdl) / 4, (uint32_t *) pdl);	/* PDL Memory */
	dump_write_segment(fd, str4("AMEM"), sizeof(amem) / 4, (uint32_t *) amem);	/* A-Memory */
	dump_write_segment(fd, str4("MMEM"), sizeof(mmem) / 4, (uint32_t *) mmem);	/* M-Memory */
	dump_write_header(fd, str4("PMEM"), main_memory_npages * 256);	/* Physical Memory */
	for (uint32_t i = 0; i < main_memory_npages; i++)
	{
		main_memory_save_page_to_file(i, fd);
	}
	/*
	 * Dummy End-of-File marker tag.  This must be the last tag
	 * written to the file
	 */
	dump_write_header(fd, str4("EOF_"), 0);
	fsync(fd);
	close(fd);
}

void
save_screenshot(void)
{
    char filename[1024] = {0};

    if (streq(ucfg.usim_screenshot_filename, "")) 
    {
        if (snprintf(filename, sizeof(filename), "usim-%s.pbm", ucfg.chaos_myname) > 0)
        {
            char* fn = strlwr(filename);
            tv_save_screenshot(fn);
        }
        else
        {
            warnx("state file cannot be saved, cannot prepare filename with chaos myname");
            return;
        }
    }
    else
    {
        tv_save_screenshot(ucfg.usim_screenshot_filename);
    }
}
