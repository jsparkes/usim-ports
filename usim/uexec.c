/* uexec.c --- CADR simulator
 *
 * 'The time has come,' the Walrus said,
 *   'To talk of many things:
 * Of shoes -- and ships -- and sealing wax --
 *   Of cabbages -- and kings --
 * And why the sea is boiling hot --
 *   And whether pigs have wings.'
 *       -- Lewis Carroll, The Walrus and Carpenter
 *
 * (and then, they ate all the clams :-)
 */

#include <err.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "bus-interface.h"
#include "config.h"
#include "diagnostic-interface.h"
#include "dump.h"
#include "m32.h"
#include "machine-control.h"
#include "misc.h"
#include "ucode.h"
#include "udiss.h"
#include "usim.h"
#include "utrace.h"
#include "uvmem.h"

bool uexec_has_run_once = false;

uint64_t p0;
uint32_t p0_pc;
bool p0_imem;

uint64_t p1;
uint32_t p1_pc;
bool p1_imem;
uint32_t npc;

uint64_t debug_ir;
uint64_t iwr;

uint64_t prom[512];
uint64_t imem[16 * 1024];

uint32_t amem[1024];
uint32_t aaddr;
int adata;

uint32_t mmem[32];
uint32_t maddr;
int mdata;

uint32_t dmem[2048];

uint32_t pdl[1024];

uint32_t spc[32];
// SPC pointer is 5-bit
uint32_t spcptr;

// functional sources and destinations
// source index, destination index
// 0, not used as destination
// dispatch constant is 10-bit, loaded from IR<41-32>
uint32_t dispatch_constant;
// 02, 014
// PDL pointer is 10-bit
uint32_t pdl_pointer;
// 03, 013 
// PDL index is 10-bit
uint32_t pdl_index;
// 010, 020
// VMA register is 32-bit
uint32_t vma_reg;
// 012, 030
// MD register is 32-bit
uint32_t md_reg;
// 013, 01
// location counter is 26-bit
uint32_t lc;
// OA register is 48 bits (same as IR)
// however its high and low parts are accessed separately
// both for writing to it (as functional destination)
// and for modifying IR (by ORing with it)
// not used as source, 016, OA_reg<25-0>
uint32_t oa_reg_low;
// not used as source, 017, OA_reg<47-26>
uint32_t oa_reg_high;

uint32_t opc;

uint32_t q;
uint32_t old_q;
uint32_t interrupt_control;

bool inhibit;

uint32_t op;
bool popj;

uint32_t new_md;
uint32_t new_md_delay;

// carry is 1 bit but it makes it easier to keep it as uint
uint32_t alu_carry;
uint32_t alu_out;

bool oal;
bool oah;

uint32_t out;

static inline uint64_t
ir(uint32_t pos, uint32_t len)
{
	return ((uint64_t) (p0 >> pos)) & ((1 << len) - 1);
}

static inline void
pushSPC(uint32_t pc)
{
	spcptr = (spcptr + 1) & 037;
	spc[spcptr] = pc;
}

static inline uint32_t
popSPC(void)
{
	uint32_t v;

	v = spc[spcptr];
	spcptr = (spcptr - 1) & 037;
	return v;
}

int
lcbytemode(void)
{
	if (interrupt_control & (1 << 29)) {
		int ir4;
		int ir3;
		int lc1;
		int lc0;
		int pos;

		ir4 = (p0 >> 4) & 1;
		ir3 = (p0 >> 3) & 1;
		lc1 = (lc >> 1) & 1;
		lc0 = (lc >> 0) & 1;
		pos = p0 & 007;
		pos |= ((ir4 ^ (lc1 ^ lc0)) << 4) | ((ir3 ^ lc0) << 3);
		DEBUG(TRACE_MICROCODE, "byte-mode, pos %o\n", pos);
		return pos;
	} else {
		int ir4;
		int lc1;
		int pos;

		ir4 = (p0 >> 4) & 1;
		lc1 = (lc >> 1) & 1;
		pos = p0 & 017;
		pos |= ((ir4 ^ lc1) ? 0 : 1) << 4;
		DEBUG(TRACE_MICROCODE, "16b-mode, pos %o\n", pos);
		return pos;
	}
}

/*
 * Advance the LC register, following the rules; will read next VMA if
 * needed.
 */
int
advanceLC(int ppc)
{
	int old_lc;
	record_lc_history();
	old_lc = lc & 0377777777;	/* LC is 26 bits. */

#ifndef DISABLE_DUMP_AT_LC
    check_lc_dump(lc);
#endif

	if (interrupt_control & (1 << 29)) {
		lc++;	/* Byte mode. */
	} else {
		lc += 2;	/* 16-bit mode. */
	}
	/*
	 * NEED-FETCH?
	 */
	if (lc & (1UL << 31UL)) {
		lc &= ~(1UL << 31UL);
		vma_reg = old_lc >> 2;
		vmRead(old_lc >> 2, &new_md);
		new_md_delay = 2;
		DEBUG(TRACE_UCODE, "advanceLC() read vma %011o -> %011o\n", old_lc >> 2, new_md);
	} else {
		/*
		 * Force skipping 2 instruction (PF + SET-MD).
		 */
		ppc |= 2;
		DEBUG(TRACE_UCODE, "advanceLC() no read; md = %011o\n", md_reg);
	}
	{
		int lc0b;
		int lc1;
		int last_byte_in_word;

		/*
		 * This is ugly, but follows the hardware logic (I
		 * need to distill it to intent but it seems correct).
		 */
		lc0b = (interrupt_control & (1 << 29) ? 1 : 0) &	/* Byte mode. */
			((lc & 1) ? 1 : 0);	/* LC0. */
		lc1 = (lc & 2) ? 1 : 0;
		last_byte_in_word = (~lc0b & ~lc1) & 1;
		DEBUG(TRACE_UCODE, "lc0b %d, lc1 %d, last_byte_in_word %d\n", lc0b, lc1, last_byte_in_word);
		if (last_byte_in_word)
			/*
			 * Set NEED-FETCH.
			 */
			lc |= (1UL << 31UL);
	}
	return ppc;
}

// M "functional" read
uint32_t
mfread(uint32_t addr)
{
	uint32_t res;

	switch (addr & 037) {
	case 0:
		return dispatch_constant;
	case 1:
		return (spcptr << 24) | (spc[spcptr] & 01777777);
	case 2:
		return pdl_pointer & 01777;
	case 3:
		return pdl_index & 01777;
	case 5:
		DEBUG(TRACE_MICROCODE, "reading pdl[%o] -> %o\n", pdl_index, pdl[pdl_index]);
		res = pdl[pdl_index];
		trace_pdlidx_read(mdata);
		return res;
	case 6:
		return opc;
	case 7:
		return q;
	case 010:
		return vma_reg;
	case 011:
        {   /* MEMORY-MAP-DATA */
            uint32_t l1_data;
            uint32_t l2_data;
            bool write_permission;
            bool access_permission;

            uvmem_vtop(md_reg, &l1_data, &l2_data, NULL, &write_permission, &access_permission);

            return ((write_permission ? 0 : (1 << 31)) |
                    (access_permission ? 0 : (1 << 30)) |
                    // <29> is connected to HI in the schematics, so has to be 1
                    (1 << 29) |
                    ((l1_data & 037) << 24) |
                    (l2_data & 077777777));
        }
    case 012:
		return md_reg;
	case 013:
		return (interrupt_control & (1 << 29)) ? lc : lc & ~1;
	case 014:
		res = (spcptr << 24) | (spc[spcptr] & 01777777);
		DEBUG(TRACE_MICROCODE, "reading spc[%o] + ptr -> %o\n", spcptr, mdata);
		spcptr = (spcptr - 1) & 037;
		return res;
	case 015:		/* ??? */
		res = 0;
		return res;
	case 024:
		DEBUG(TRACE_MICROCODE, "reading pdl[%o] -> %o, pop\n", pdl_pointer, pdl[pdl_pointer]);
		res = pdl[pdl_pointer];
		trace_pdlptr_pop(mdata);
		pdl_pointer = (pdl_pointer - 1) & 01777;
		return res;
	case 025:
		DEBUG(TRACE_MICROCODE, "reading pdl[%o] -> %o\n", pdl_pointer, pdl[pdl_pointer]);
		res = pdl[pdl_pointer];
		trace_pdlptr_read(mdata);
		return res;
	case 026:
		res = 0; 	/* ??? */
		return res;
		
	}
	err(1, "unknown MF register (%o) read\n", addr);
}

void
mfwrite(uint32_t dest, uint64_t data)
{
	switch (dest >> 5) {
	case 0: return;
	case 1:		/* LOCATION-COUNTER LC (location counter) 26 bits. */
		DEBUG(TRACE_UCODE, "writing LC <- %o\n", data);
		lc = (lc & ~0377777777) | (data & 0377777777);
		if (interrupt_control & (1 << 29)) {
			/*
			 * ---!!! Not sure about byte mode...
			 */
		} else {
			/*
			 * In half word mode, low order bit is
			 * ignored.
			 */
			lc &= ~1;
		}
		/*
		 * Set NEED-FETCH.
		 */
		lc |= (1UL << 31UL);
		return;
	case 2:		/* INTERRUPT-CONTROL Interrupt Control <29-26>. */
        {
            DEBUG(TRACE_UCODE, "writing IC <- %o\n", data);

            interrupt_control = data;

            // "1 if a sequence break (macrocode interrupt signal) is pending. 
            // This flag does nothing except contribute to the JUMP condition. 
            // This reflects bit 26 of the Interrupt Control register."
            if (interrupt_control & (1 << 26)) 
            {
                DEBUG(TRACE_UCODE, "usim: ic.sequence break\n");
            }

            // "1 if external interrupt requests are allowed to contribute to 
            // the JUMP condition. This reflects bit 27 of 
            // the Interrupt Control register."
            if (interrupt_control & (1 << 27)) 
            {
                DEBUG(TRACE_UCODE, "usim: ic.interrupt enable\n");
            }
            
            // "<28>, BUS-RESET, generates a RESET signal on the Unibus (BUS INIT L) 
            // and on the Xbus (XBUS.INIT L), and resets the bus interface, 
            // when it is written 1 and then 0. The machine also resets the busses 
            // when it is powered up."
            // "This reflects bit 28 of the Interrupt Control register, 
            // which is set to 1 to reset the bus interface, the Unibus, and the Xbus."
            // detect 1-0 transition
            if (interrupt_control & (1 << 28))
            {
                INFO(TRACE_USIM, "usim: ic.bus reset\n");
                bus_interface_bus_reset();
            }

            // "1 if the instruction stream is in 8-bit units, 0 if it is 
            // in 16-bit units. This reflects bit 29 of 
            // the Interrupt Control register."
            if (interrupt_control & (1 << 29)) 
            {
                DEBUG(TRACE_UCODE, "usim: ic.lc byte mode\n");
            }

            // "This is 1 if the next time the instruction stream is advanced, 
            // a new word will be fetched from main memory. This is a function 
            // of the low 2 bits of LC, of byte mode, and of whether the LC 
            // has been written into since an instruction word was last fetched 
            // from main memory."
            if (interrupt_control & (1 << 31)) 
            {
                DEBUG(TRACE_UCODE, "usim: ic.need fetch\n");
            }

            lc = (lc & ~(017 << 26)) |	/* Preserve flags. */
                (interrupt_control & (017 << 26));
        }
		return;
	case 010:		/* C-PDL-BUFFER-POINTER PDL (addressed by pointer) */
		DEBUG(TRACE_UCODE, "writing pdl[%o] <- %o\n", pdl_pointer, data);
		trace_pdlptr_write(data);
		pdl[pdl_pointer] = data;
		return;
	case 011:		/* C-PDL-BUFFER-POINTER-PUSH PDL (addressed by pointer, push) */
		pdl_pointer = (pdl_pointer + 1) & 01777;
		DEBUG(TRACE_UCODE, "writing pdl[%o] <- %o, push\n", pdl_pointer, data);
		trace_pdlptr_push(data);
		pdl[pdl_pointer] = data;
		return;
	case 012:		/* C-PDL-BUFFER-INDEX PDL (address by index). */
		DEBUG(TRACE_UCODE, "writing pdl[%o] <- %o\n", pdl_index, data);
		pdl[pdl_index] = data;
		trace_pdlidx_write(data);
		return;
	case 013:		/* PDL-BUFFER-INDEX PDL index. */
		DEBUG(TRACE_UCODE, "pdl-index <- %o\n", data);
		pdl_index = data & 01777;
		return;
	case 014:		/* PDL-BUFFER-POINTER PDL pointer. */
		DEBUG(TRACE_UCODE, "pdl-ptr <- %o\n", data);
		pdl_pointer = data & 01777;
		return;
	case 015:		/* MICRO-STACK-DATA-PUSH SPC data, push. */
		pushSPC(data);
		return;
	case 016:		/* OA-REG-LO Next instruction modifier (lo). */
		oa_reg_low = data & 0377777777; // OA<25-0>
		oal = true;
		DEBUG(TRACE_UCODE, "setting oa_reg lo %o\n", oa_reg_low);
		return;
	case 017:		/* OA-REG-HI Next instruction modifier (hi). */
		oa_reg_high = data & 037777777; // OA<47-26>
		oah = true;
		DEBUG(TRACE_UCODE, "setting oa_reg hi %o\n", oa_reg_high);
		return;
	case 020:		/* VMA VMA register (memory address). */
		vma_reg = data;
		return;
	case 021:		/* VMA-START-READ VMA register, start main memory read. */
		vma_reg = data;
		vmRead(vma_reg, &new_md);
		new_md_delay = 2;
		return;
	case 022:		/* VMA-START-WRITE VMA register, start main memory write. */
		vma_reg = data;
		vmWrite(vma_reg, md_reg);
		return;
	case 023:		/* VMA-WRITE-MAP VMA register, write map. */
		vma_reg = data;
		DEBUG(TRACE_UCODE, "vma-write-map md=%o, vma=%o (addr %o)\n", md_reg, vma_reg, md_reg >> 13);
		uvmem_write_map(vma_reg, md_reg);
		return;
	case 030:		/* MEMORY-DATA MD register (memory data). */
		md_reg = data;
		DEBUG(TRACE_UCODE, "md<-%o\n", md_reg);
		return;
	case 031:		/* MEMORY-DATA-START-READ */
		md_reg = data;
		vmRead(vma_reg, &new_md);
		new_md_delay = 2;
		return;
	case 032:		/* MEMORY-DATA-START-WRITE */
		md_reg = data;
		vmWrite(vma_reg, md_reg);
		return;
	case 033:		/* MEMORY-DATA-WRITE-MAP MD register, write map (like 23). */
		md_reg = data;
		DEBUG(TRACE_UCODE, "memory-data-write-map md=%o, vma=%o (addr %o)\n", md_reg, vma_reg, md_reg >> 13);
		uvmem_write_map(vma_reg, md_reg);
		return;
	}
	warn("unknown MF register (%o) write (%llo)\n", dest, data);
}

/*
 * Write value to decoded destination.
 */
void
writeDest(uint32_t dest)
{
	// means if IR<25> == 1, 
	if (dest & 04000) {
		amem[dest & 03777] = out;
	}
	else
	{
		mfwrite(dest, out);
		mmem[dest & 037] = amem[dest & 037] = out;
	}
}

/// MARK: ALU

void
qControl(void)
{
	old_q = q;
	switch (ir(0, 2)) {
	case 1:
		DEBUG(TRACE_MICROCODE, "q<<\n");
		q <<= 1;
		/*
		 * Inverse of ALU sign.
		 */
		if ((alu_out & 0x80000000) == 0)
			q |= 1;
		break;
	case 2:
		DEBUG(TRACE_MICROCODE, "q>>\n");
		q >>= 1;
		if (alu_out & 1)
			q |= 0x80000000;
		break;
	case 3:
		DEBUG(TRACE_MICROCODE, "q<-alu\n");
		q = alu_out;
		break;
	}
}

void
outControl(void)
{
	switch ((p0 >> 12) & 3) {
	case 0:
		WARNING(TRACE_MICROCODE, "out == 0!\n");
		out = rol32(mdata, p0 & 037);
		break;
	case 1:
		out = alu_out;
		break;
	case 2:
		/*
		 * "ALU output shifted right one, with
		 * the correct sign shifted in,
		 * regardless of overflow."
		 */
		out = (alu_out >> 1) | (alu_carry ? 0x80000000 : 0);
		break;
	case 3:
		out = (alu_out << 1) | ((old_q & 0x80000000) ? 1 : 0);
		break;
	}
}

void
arithOps(uint32_t op)
{
	int cin = ir(2, 1);

	int64_t lv;

	switch (op) {
	case 020:
		alu_out = cin ? 0 : -1;
		alu_carry = 0;
		break;
	case 021:
		lv = (int64_t) (mdata & adata) - (cin ? 0 : 1);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 022:
		lv = (int64_t) (mdata & ~adata) - (cin ? 0 : 1);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 023:
		lv = (int64_t) mdata - (cin ? 0 : 1);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 024:
		lv = (int64_t) (mdata | ~adata) + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 025:
		lv = (int64_t) (mdata | ~adata) + (mdata & adata) + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 026:		/* [M-A-1] [SUB] */
		sub32(mdata, adata, cin, alu_out, alu_carry);
		break;
	case 027:
		lv = (int64_t) (mdata | ~adata) + mdata + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 030:
		lv = (int64_t) (mdata | adata) + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 031:		/* [ADD] [M+A+1] */
		add32(mdata, adata, cin, alu_out, alu_carry);
		break;
	case 032:
		lv = (int64_t) (mdata | adata) + (mdata & ~adata) + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 033:
		lv = (int64_t) (mdata | adata) + mdata + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 034:		/* [M+1] */
		alu_out = mdata + (cin ? 1 : 0);
		alu_carry = 0;
		if (mdata == (int) 0xffffffff && cin)
			alu_carry = 1;
		break;
	case 035:
		lv = (int64_t) mdata + (mdata & adata) + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 036:
		lv = (int64_t) mdata + (mdata | ~adata) + (cin ? 1 : 0);
		alu_out = (uint32_t) lv;
		alu_carry = (lv >> 32) ? 1 : 0;
		break;
	case 037:		/* [M+M] [M+M+1] */
		add32(mdata, mdata, cin, alu_out, alu_carry);
		break;
	}
}

void
logiOps(uint32_t op)
{
	switch (op) {
	case 000:
		alu_out = 0;
		break;
	case 001:
		alu_out = mdata & adata;
		break;
	case 002:
		alu_out = mdata & ~adata;
		break;
	case 003:
		alu_out = mdata;
		break;
	case 004:
		alu_out = ~mdata & adata;
		break;
	case 005:
		alu_out = adata;
		break;
	case 006:
		alu_out = mdata ^ adata;
		break;
	case 007:
		alu_out = mdata | adata;
		break;
	case 010:
		alu_out = ~adata & ~mdata;
		break;
	case 011:
		alu_out = adata == mdata;
		break;
	case 012:
		alu_out = ~adata;
		break;
	case 013:
		alu_out = mdata | ~adata;
		break;
	case 014:
		alu_out = ~mdata;
		break;
	case 015:
		alu_out = ~mdata | adata;
		break;
	case 016:
		alu_out = ~mdata | ~adata;
		break;
	case 017:
		alu_out = ~0;
		break;
	}
}

void
divOps(uint32_t	op)
{
	int cin = ir(2, 1);

	switch (op) {
	case 040:		// Multiply step.
		if (q & 1) {
			add32(adata, mdata, cin, alu_out, alu_carry);
		} else {
			alu_out = mdata;
			alu_carry = alu_out & 0x80000000 ? 1 : 0;
		}
		break;
	case 041:		// Divide step.
		if (q & 1) {
			sub32(mdata, abs32(adata), !cin, alu_out, alu_carry);
		} else {
			add32(mdata, abs32(adata), cin, alu_out, alu_carry);
		}
		break;
	case 045:		// Remainder correction.
		if (q & 1) {
			alu_carry = 0;
		} else {
			add32((int32_t)alu_out, abs32(adata), cin, alu_out, alu_carry);
		}
		break;
	case 051:		// Initial divide step.
		DEBUG(TRACE_MICROCODE, "divide-first-step\n");
		DEBUG(TRACE_MICROCODE, "divide: %o / %o \n", q, adata);
		sub32(mdata, abs32(adata), !cin, alu_out, alu_carry);
		DEBUG(TRACE_MICROCODE, "alu_out %08x %o %d\n", alu_out, alu_out, alu_out);
		break;
	}
}

void
alu(void)
{
	uint32_t dest = ir(14, 12);
	uint32_t aluop = ir(3, 6);

	alu_carry = 0;
	switch (aluop) {
	case 000 ... 017:
		logiOps(aluop);
		break;
	case 020 ... 037:
		arithOps(aluop);
		break;
	case 040:
	case 041:
	case 045:
	case 051:
		divOps(aluop);
		break;
	}
	qControl();
	outControl();
	writeDest(dest);
	DEBUG(TRACE_MICROCODE, "alu_out 0x%08x, alu_carry %d, q 0x%08x\n", alu_out, alu_carry, q);
}

/// MARK: DISPATCH

void
dsp(void)
{
	uint32_t pos = ir(0, 5);
	uint32_t len = ir(5, 3);
	uint32_t map = ir(8, 2);
	uint32_t disp_addr = ir(12, 11);
	uint32_t n_plus1 = ir(25, 1);
	uint32_t enable_ish = ir(24, 1);
	uint32_t disp_const = ir(32, 10);

	uint32_t mask;

	uint32_t r;
	uint32_t p;
	uint32_t n;
	uint32_t target;

	if (ir(10, 2) == 2) {
		DEBUG(TRACE_MICROCODE, "dmem[%o] <- %o\n", disp_addr, adata);
		dmem[disp_addr] = adata;
		return;
	}
	if (ir(10, 2) == 3)
		pos = lcbytemode();
	DEBUG(TRACE_MICROCODE, "m-src %o, ", mdata);
	/*
	 * Rotate M-SOURCE.
	 */
	mdata = rol32(mdata, pos);
	/*
	 * Generate mask.
	 */
	{
		int left_mask_index;

		left_mask_index = (len - 1) & 037;
		mask = ~0;
		mask >>= 31 - left_mask_index;
		if (len == 0)
			mask = 0;
	}
	/*
	 * Put LDB into DISPATCH-ADDR.
	 */
	disp_addr |= mdata & mask;
	DEBUG(TRACE_MICROCODE, "rotated %o, mask %o, result %o\n", mdata, mask, mdata & mask);
	/*
	 * Tweak DISPATCH-ADDR with L2 map bits.
	 */
	if (map) {
		uint32_t l2_map_bits;
		// the CADR doc says these are bit 14 and bit 15
		// but it seems like this has been changed (at least since System 46)
		// so bit 18 and bit 19 is correct
		int bit18;
		int bit19;

		uvmem_vtop(md_reg, NULL, &l2_map_bits, NULL, NULL, NULL);

		bit19 = ((l2_map_bits >> 19) & 1) ? 1 : 0;
		bit18 = ((l2_map_bits >> 18) & 1) ? 1 : 0;
		DEBUG(TRACE_MICROCODE, "md %o, l2_map_bits %o, b19 %o, b18 %o\n", md_reg, l2_map_bits, bit19, bit18);
		switch (map) {
		case 1:
			disp_addr |= bit18;
			break;
		case 2:
			disp_addr |= bit19;
			break;
		case 3:
			disp_addr |= bit18 | bit19;
			break;
		}
	}
	disp_addr &= 03777;
	DEBUG(TRACE_MICROCODE, "dispatch[%o] -> %o ", disp_addr, dmem[disp_addr]);
	disp_addr = dmem[disp_addr];
	dispatch_constant = disp_const;
	target = disp_addr & 037777;	/* 14 bits. */
	n = (disp_addr >> 14) & 1;
	p = (disp_addr >> 15) & 1;
	r = (disp_addr >> 16) & 1;
	DEBUG(TRACE_MICROCODE, "%s%s%s\n", n ? "N " : "", p ? "P " : "", r ? "R " : "");
	if (n_plus1 && n) {
		npc--;
	}
	/*
	 * Enable instruction sequence hardware.
	 */
	if (enable_ish) {
		DEBUG(TRACE_UCODE, "advancing LC due to DISPATCH\n");
		advanceLC(0);
	}
	if (n)
		inhibit = true;
	if (p && r)
		return;
	if (p) {
		if (!n)
			pushSPC(npc);
		else
			pushSPC(npc - 1);
	}
	if (r) {
		target = popSPC();
		if ((target >> 14) & 1) {
			DEBUG(TRACE_UCODE, "advancing LC due to microcode stack bit 14\n");
			target = advanceLC(target);
		}
		target &= 037777;
	}
	npc = target;
	popj = false;
}

/// MARK: JUMP

int
check_jcond(void)
{
	if (ir(5, 1) == 0) {
		int rot = ir(0, 5);

		DEBUG(TRACE_MICROCODE, "jump-if-bit; rot %o, before %o ", rot, mdata);
		mdata = rol32(mdata, rot);
		DEBUG(TRACE_MICROCODE, "after %o\n", mdata);
		return mdata & 1;
	}
	// Internal condition.
	switch (ir(0, 4)) {
	case 0:		/* illegal ??? */
		break;
	case 1:
		return mdata < adata;
	case 2:
		return mdata <= adata;
	case 3:
		return mdata == adata;
	case 4:
		return (!machine_state.vmaok);
	case 5:
		DEBUG(TRACE_MICROCODE, "jump i|pf\n");	/* pgf.or.int */
		return (!machine_state.vmaok) | (interrupt_control & (1 << 27) ? interrupt_pending_flag : 0);

	case 6:
		DEBUG(TRACE_MICROCODE, "jump i|pf|sb\n");	/* pgf.or.int.sb */
		return (!machine_state.vmaok) | (interrupt_control & (1 << 27) ? interrupt_pending_flag : 0) | (interrupt_control & (1 << 26));
	case 7:
		return 1;
	}
	err(1, "unknown jump (%llo) condition", ir(0, 4));
}

void
jmp(void)
{
	int target = ir(12, 14);
	int r = ir(9, 1);
	int p = ir(8, 1);
	int n = ir(7, 1);
	int invert_sense = ir(6, 1);

	int cond;

	DEBUG(TRACE_MICROCODE, "a=%o (%o), m=%o (%o)\n", aaddr, adata, maddr, mdata);
	if (ir(10, 2) == 1) {
		NOTICE(TRACE_USIM, "usim: illop, asserting halted\n");
        machine_state.halted = true;
	}

#ifndef NDEBUG
	if (ir(10, 2) == 3) {
		WARNING(TRACE_MICROCODE, "jump w/misc-3!\n");
	}
#endif
	/*
	 * P & R & jump-inst -> write ucode.
	 */
	if (p && r) {
		DEBUG(TRACE_MICROCODE, "u-code write; %Lo @ %o\n", iwr, target);
		imem[target] = iwr;
		return;
	}
	/*
	 * Jump condition.
	 */
	cond = check_jcond();
	if (invert_sense)
		cond = !cond;
	if (p && cond) {
		if (!n)
			pushSPC(npc);
		else
			pushSPC(npc - 1);
	}
	if (r && cond) {
		target = popSPC();
		if ((target >> 14) & 1) {
			DEBUG(TRACE_UCODE, "advancing LC due to microcode stack bit 14\n");
			target = advanceLC(target);
		}
		target &= 037777;
	}
	if (cond) {
		if (n)
			inhibit = true;
		npc = target;
		/*
		 * inhibit possible POPJ.
		 */
		popj = false;
	}
}

/// Mark: BYTE

uint32_t
msk(int pos)
{
	int widthm1 = ir(5, 5);

	int left_mask_index;
	uint32_t left_mask;
	uint32_t right_mask;
	int right_mask_index;

	right_mask_index = pos;
	left_mask_index = (right_mask_index + widthm1) & 037;	/* mod 32? */
	left_mask = ~0;
	right_mask = ~0;
	left_mask >>= 31 - left_mask_index;
	right_mask <<= right_mask_index;

	//DEBUG(TRACE_MICROCODE, "widthm1 %o, pos %o, mr_sr_bits %o\n", widthm1, pos, mr_sr_bits);
	DEBUG(TRACE_MICROCODE, "left_mask_index %o, right_mask_index %o\n", left_mask_index, right_mask_index);
	DEBUG(TRACE_MICROCODE, "left_mask %o, right_mask %o, mask %o\n", left_mask, right_mask, left_mask & right_mask);
	return left_mask & right_mask;
}

void
byt(void)
{
	uint32_t dest = ir(14, 12);
	uint32_t mr_sr_bits = ir(12, 2);
	uint32_t pos = ir(0, 5);	// p0 & 037;

	uint32_t mask;

	DEBUG(TRACE_MICROCODE, "a=%o (%o), m=%o (%o), dest=%o\n", aaddr, adata, maddr, mdata, dest);

	if (ir(10, 2) == 3)
		pos = lcbytemode();
	mask = msk(mr_sr_bits & 2 ? pos : 0);
	switch (mr_sr_bits) {
	case 0:
		WARNING(TRACE_MICROCODE, "mr_sr_bits == 0!\n");
		out = 0;
		break;
	case 1:
		DEBUG(TRACE_MICROCODE, "ldb; m %o\n", mdata);
		mdata = rol32(mdata, pos);
		out = (mdata & mask) | (adata & ~mask);
		DEBUG(TRACE_MICROCODE, "ldb; m-rot %o, mask %o, result %o\n", mdata, mask, out);
		break;
	case 2:
		out = (mdata & mask) | (adata & ~mask);
		DEBUG(TRACE_MICROCODE, "sel-dep; a %o, m %o, mask %o -> %o\n", adata, mdata, mask, out);
		break;
	case 3:
		DEBUG(TRACE_MICROCODE, "dpb; m %o, pos %o\n", mdata, pos);
		mdata = rol32(mdata, pos);
		out = (mdata & mask) | (adata & ~mask);
		DEBUG(TRACE_MICROCODE, "dpb; mask %o, result %o\n", mask, out);
		break;
	}
	writeDest(dest);
}

/// MARK: Step

void
incNPC(void)
{
    // transfer p1 to p0
    p0_imem = p1_imem;
    p0 = p1;
    p0_pc = p1_pc;

    p1_imem = machine_state.promdisabled;
    p1 = p1_imem ? imem[npc] : prom[npc];
    p1_pc = npc;

    // overflow PC at 14-bit since it is 14-bit
    if (npc == 037777) npc = 0;
    else npc++;

    opc = p0_pc;
}

void
uexec_step()
{
    uexec_has_run_once = true;

	// fetch instruction
	incNPC();

// this is used to dump the memories to hex files at a particular pc
#if 0
    if ((!machine_state.promdisabled) && (p0_pc == 0257))
    {
#define DUMP(WHAT) \
        { \
        FILE* fp = fopen(#WHAT".hex", "w"); \
        for (size_t i = 0; i < (sizeof(WHAT)/sizeof(WHAT[0])); i++) \
        { \
            for (size_t j = 0; j < sizeof(WHAT[0]); j++) \
            { \
                fprintf(fp, "%02x\n", (uint8_t)((WHAT[i] >> (8 * j)) & 0xFF)); \
            } \
        } \
        fclose(fp); \
        } 

        DUMP(mmem);
        DUMP(amem);
        DUMP(pdl);
        DUMP(spc);
        DUMP(l1_map);
        DUMP(l2_map);
        DUMP(imem);
        DUMP(dmem);

        machine_state.halted = true;
        return;
    }
#endif

	if (new_md_delay) 
    {
		new_md_delay--;
		if (new_md_delay == 0) md_reg = new_md;
	}

	// if this instrution is inhibited, return and continue from next
	if (inhibit == true) 
    {
		DEBUG(TRACE_MICROCODE, "inhibit; npc %o\n", npc);
		inhibit = false;
        return;
	}

	// program modification by conceptual registers (OA registers)
	if (oal == true)
    {
		DEBUG(TRACE_MICROCODE, "merging oa lo %o\n", oa_reg_low);
		oal = false;
		p0 |= (uint64_t) oa_reg_low;
	}

	if (oah == true)
    {
		DEBUG(TRACE_MICROCODE, "merging oa hi %o\n", oa_reg_high);
		oah = false;
		p0 |= (((uint64_t) oa_reg_high) << 26);
	}

#ifndef DISABLE_TRACE_UCODE
    trace_ucode();
#endif
    record_pc_history(p0_pc, p0_imem);
    // decode instruction
    // decode common fields
    // opcode (alu, jump, dispatch, byte) specific fields are decoded 
    // in opcode functions alu, jmp, dsp, byt
    // const uint64_t statistics = ir(46, 1);
    // const uint64_t ilong = ir(45, 1);
    op = ir(43, 2);
    popj = ir(42, 1) == 1;
    aaddr = ir(32, 10);
    const uint64_t msource = ir(31, 1);
    maddr = ir(26, 5);
    // const uint64_t misc_function = ir(10, 2);
    // read
    mdata = (msource == 0) ? mmem[maddr] : mfread(maddr);
    adata = amem[aaddr];
    // this is used either to write to IMEM
    // or load statistics counter
    iwr = ((uint64_t) (adata & 0177777) << 32) | (uint32_t) mdata;
    // write phase of the clock in the operations
    switch (op) 
    {
        case 0:
            alu();
            break;
        case 1:
            jmp();
            break;
        case 2:
            dsp();
            break;
        case 3:
            byt();
            break;
    }
    if (popj) 
    {
        DEBUG(TRACE_MICROCODE, "popj; ");
        __attribute__((unused)) const uint32_t old_npc = npc;
        npc = popSPC();
        if ((npc >> 14) & 1) 
        {
            DEBUG(TRACE_UCODE, "advancing LC due to POPJ (old npc = #x%x, new npc = #x%x)\n",
                    old_npc, npc);
            npc = advanceLC(npc);
        }
        npc &= 037777;
    }
}
