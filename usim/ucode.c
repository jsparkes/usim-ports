/* ucode.c --- CADR simulator main loop
 */

#include <err.h>
#include <fcntl.h>
#include <limits.h>
#include <semaphore.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

#include "bus-adaptor.h"
#include "config.h"
#include "diagnostic-interface.h"
#include "disk-controller.h"
#include "dump.h"
#include "idle.h"
#include "iob.h"
#include "machine-control.h"
#include "main-memory.h"
#include "misc.h"
#include "sched.h"
#include "tv.h"
#include "ucfg.h"
#include "uch11.h"
#include "ucode.h"
#include "udiss.h"
#include "usim.h"
#include "utrace.h"
#include "uvmem.h"

// cycles do not reset even with reset
uint64_t machine_cycles;

void
ucode_init(void)
{
    DEBUG(TRACE_USIM, "Loading PROM Code: %s\n", ucfg.ucode_prommcr_filename);
	ucode_load_prom_from_file(ucfg.ucode_prommcr_filename);

    DEBUG(TRACE_USIM, "Loading PROM Symbols: %s\n", ucfg.ucode_promsym_filename);
	sym_read_file(&sym_prom, ucfg.ucode_promsym_filename);

    DEBUG(TRACE_USIM, "Loading UCODE Symbols: %s\n", ucfg.ucode_mcrsym_filename);
	sym_read_file(&sym_mcr, ucfg.ucode_mcrsym_filename);
}

void
ucode_run(void)
{
	machine_cycles = 0;

    // set the initial conditions
    machine_control_boot();

    machine_control_performance_report_start();

    // this loop is the main clock (not the processor)
	while (!machine_state.halted) 
	{
        uexec_step();

        if (idle_enabled) idle_check(machine_cycles);

		if ((machine_cycles & 0x0ffff) == 0) 
        {
			iob_poll();
			tv_poll();
		}

#ifndef DISABLE_DUMP_AT_NPC 
        if (machine_state.promdisabled) check_npc_dump(npc);
#endif

#ifndef DISABLE_FULL_TRACE_AFTER_LC
        if (machine_state.promdisabled) check_enable_full_tracing_after_lc(lc);
#endif

		machine_cycles++;
	}

    machine_control_performance_report_stop();
}

void
ucode_load_prom_from_file(char *fn)
{
	int fd = open(fn, O_RDONLY | O_BINARY);
	if (fd < 0) 
    {
		errx(1, "ucode: cannot load prom file %s\n", fn);
	}
	__attribute__((unused)) uint32_t code = read32pdp(fd);
	uint32_t start = read32pdp(fd);
	uint32_t size = read32pdp(fd);
	INFO(TRACE_USIM, "ucode: prom (%s) loaded: code: %d, start: %d, size: %d\n", fn, code, start, size);
	int loc = start;
	for (uint32_t i = 0; i < size; i++) 
    {
		uint16_t w1;
		uint16_t w2;
		uint16_t w3;
		uint16_t w4;

		w1 = read16le(fd);
		w2 = read16le(fd);
		w3 = read16le(fd);
		w4 = read16le(fd);
		prom[loc] = ((uint64_t) w1 << 48) | ((uint64_t) w2 << 32) | ((uint64_t) w3 << 16) | ((uint64_t) w4 << 0);
		loc++;
	}
}

int interrupt_status_reg;
int interrupt_pending_flag;

void
set_interrupt_status_reg(int new)
{
	interrupt_status_reg = new;
    // if Xbus or Unibus interrupt is requested, then set pending flag
	interrupt_pending_flag = (interrupt_status_reg & 0140000) ? 1 : 0;
}

void
assert_unibus_interrupt(int vector)
{
    // "02000: Enable Unibus Interrupts. A 1 here causes bit 
    // 15 (Unibus interrupt) to be set when the bus interface accepts 
    // a Unibus interrupt. This bit is not reset by power-up."

    // interrupts enabled ?
	if (interrupt_status_reg & 02000) 
    {
		DEBUG(TRACE_INT, "assert: unibus interrupt (enabled)\n");

        // clear existing vector number
        // set interrupt pending
        // set vector number

        // "01774: Bits 9-2 contain the vector address of the last Unibus 
        // interrupt accepted by the bus interface or simulated by 
        // the PDP-11 program."

        // "0100000: Unibus Interrupt. A 1 indicates that a Unibus interrupt 
        // has been accepted by the bus interface or simulated by a 
        // PDP-11 program, and is awaiting processing by the CADR program."

		set_interrupt_status_reg((interrupt_status_reg & ~01774) | 0100000 | (vector & 01774));

	} else {
		DEBUG(TRACE_INT, "assert: unibus interrupt (disabled)\n");
	}
}

void
deassert_unibus_interrupt(void)
{
    // if there is an interrupt pending, clear it and the vector number
	if (interrupt_status_reg & 0100000) 
    {
		DEBUG(TRACE_INT, "deassert: unibus interrupt\n");
		set_interrupt_status_reg(interrupt_status_reg & ~(01774 | 0100000));
	}
}

void
assert_xbus_interrupt(void)
{
    // "40000: Xbus Interrupt (read only). This bit is 
    // the interrupt-request line on the Xbus."

    // set interrupt request/pending
	DEBUG(TRACE_INT, "assert: xbus interrupt (%o)\n", interrupt_status_reg);
	set_interrupt_status_reg(interrupt_status_reg | 040000);
}

void
deassert_xbus_interrupt(void)
{
    // if there is an interrupt pending, clear it
	if (interrupt_status_reg & 040000) 
    {
		DEBUG(TRACE_INT, "deassert: xbus interrupt\n");
		set_interrupt_status_reg(interrupt_status_reg & ~040000);
	}
}
