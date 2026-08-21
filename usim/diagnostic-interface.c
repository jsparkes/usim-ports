/* diagnostic-interface.c --- CADR diagnostic interface
 *
 */

#include <assert.h>
#include <err.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "bus-interface.h"
#include "diagnostic-interface.h"
#include "dump.h"
#include "machine-control.h"
#include "main-memory.h"
#include "ucfg.h"
#include "ucode.h"
#include "udiss.h"
#include "usim.h"
#include "utrace.h"

#ifndef NDEBUG

static uint16_t param1;
static uint16_t param2;
static uint16_t param3;

#endif

// names with _ are active_low, so if _err is true, there is no err
static uint16_t
get_flag_register_1()
{
	// waiting for memory, never happens in usim, always true
	const bool _wait = true;
	// level-2 map parity error, never happens in usim, always true
	const bool _v1pe = true;
	// level-1 map parity error, never happens in usim, always true
	const bool _v0pe = true;
	// highok, always true during usim is running
    const bool highok = true;
	// -stathalt = 0 if the machine has been stopped by the statistics counter
    // false since there is no stat counter
    const bool stathalt = false;
    // err = 1 if an error condition is present
    // only error condition in usim is halted caused by illop
    // which terminates usim
	const bool err = false;
	// ssdone = 1 if a single-step operation has been completed
    // no single stepping
    const bool ssdone = false;
	// srun = 1 if the machine is trying to run
    const bool srun = false;
    // -higherr = 1, if there was highok at the last clock, always true in usim
	const bool _higherr = true;
	// below are all parity errors, never happens in usim, always true
	const bool _mempe = true;
	const bool _ipe = true;
	const bool _dpe = true;
	const bool _spe = true;
	const bool _pdlpe = true;
	const bool _mpe = true;
	const bool _ape = true;

	uint16_t reg = 0;

	if (_wait)      reg |= 0x8000;
	if (_v1pe)		reg |= 0x4000;
	if (_v0pe)		reg |= 0x2000;
	if (highok)		reg |= 0x1000;
	if (!stathalt)	reg |= 0x0800;
	if (err)		reg |= 0x0400;
	if (ssdone)		reg |= 0x0200;
	if (srun)		reg |= 0x0100;
	if (_higherr)	reg |= 0x0080;
	if (_mempe)		reg |= 0x0040;
	if (_ipe)		reg |= 0x0020;
	if (_dpe)		reg |= 0x0010;
	if (_spe)		reg |= 0x0008;
	if (_pdlpe)		reg |= 0x0004;
	if (_mpe)		reg |= 0x0002;
	if (_ape)		reg |= 0x0001;

	return reg;
}

// names with _ are active_low, so if _err is true, there is no err
static uint16_t
get_flag_register_2()
{
	// 15 unused
	// 14 unused
	// 13 wmapd
    bool wmapd = false;
	// 12 destspcd
    bool destspcd = false;
	// 11 iwrited
    bool iwrited = false;
	// 10 imodd
    bool imodd = false;
	// 9 pdlwroted
    bool pdlwrited = false;
	// 8 spushd
    bool spushd = false;
	// 7
	// 6
    // this should not matter
	const bool ir_parity = false;
    // 4 nop
    bool nop = false;
	// -vmaok: last cycle had a page fault	
    bool vmaok = machine_state.vmaok;
	// jcond: 1 if the jump-condition is satisfied
    bool jcond = false;
	// pcs - next PC source
    uint32_t pcs = 0;

	uint16_t reg = 0;

	if (wmapd)		    reg |= 0x2000;
	if (destspcd)	    reg |= 0x1000;
	if (iwrited)	    reg |= 0x0800;
	if (imodd)		    reg |= 0x0400;
	if (pdlwrited)	    reg |= 0x0200;
	if (spushd)		    reg |= 0x0100;
	if (ir_parity)	    reg |= 0x0020;
	if (nop)		    reg |= 0x0010;
	if (vmaok)	        reg |= 0x0008;
	if (jcond)	        reg |= 0x0004;

	reg |= pcs;

	return reg;
}

#ifndef NDEBUG

static void
dump_main_memory()
{
	uint32_t paddr = (param2 << 16) | param1;
	uint32_t count = param3;

	printf("spy: dump main memory from  #o%06o (0x%06x) to #o%06o (0x%06x), count=%d\n", 
		paddr, paddr, paddr + count, paddr + count, count);

	for (uint32_t i = 0; i < count; i++)
	{
		uint32_t value;
		main_memory_read(paddr, &value);
		printf("#o%06o 0x%06x: #o%011o 0x%08x - %d\n", paddr, paddr, value, value, i);
		paddr++;
	}
}

#endif

void
diagnostic_interface_read(uint32_t uaddr, uint16_t *pv)
{
    switch (uaddr) 
	{
		case 0766000:
			*pv = p0 & 0xFFFF;
			INFO(TRACE_SPY, "spy: read IR<15-0>: %06o IR=%018o\n", *pv, p0);
			break;

		case 0766002:
			*pv = (p0 >> 16) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read IR<31-16>: %06o IR=%018o\n", *pv, p0);
			break;

		case 0766004:
			*pv = (p0 >> 32) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read IR<47-32>: %06o IR=%018o\n", *pv, p0);
			break;

		case 0766006: // not used
			break;

		case 0766010:
			*pv = opc;
			INFO(TRACE_SPY, "spy: read OPC: %06o\n", *pv);
			break;

		case 0766012:
			*pv = npc;
			INFO(TRACE_SPY, "spy: read PC: %06o\n", *pv);
			break;

		case 0766014:
			*pv = out & 0xFFFF;
			INFO(TRACE_SPY, "spy: read OB<15-0>: %06o OB=%011o\n", *pv, out);
			break;

		case 0766016:
			*pv = (out >> 16) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read OB<31-16>: %06o OB=%011o\n", *pv, out);
			break;

		case 0766020:
			*pv = get_flag_register_1();
			INFO(TRACE_SPY, "spy: read flag register 1: 0x%04x\n", *pv);
			break;

		case 0766022:
			*pv = get_flag_register_2();
			INFO(TRACE_SPY, "spy: read flag register 2: 0x%04x\n", *pv);
			break;

		case 0766024:
			*pv = ((uint32_t)mdata) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read M<15-0>: %06o M=%011o\n", 
					*pv, (uint32_t)mdata);
			break;

		case 0766026:
			*pv = (((uint32_t)mdata) >> 16) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read M<31-16>: %06o M=%011o\n", 
					*pv, (uint32_t)mdata);
			break;

		case 0766030:
			*pv = ((uint32_t)adata) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read A<15-0>: %06o A=%011o\n", 
					*pv, (uint32_t)adata);
			break;

		case 0766032:
			*pv = (((uint32_t)adata) >> 16) & 0xFFFF;
			INFO(TRACE_SPY, "spy: read A<31-16>: %06o A=%011o\n", 
					*pv, (uint32_t)adata);
			break;

		case 0766034:
            errx(1, "Statistics counter is not implemented");
			break;
            
		case 0766036:
            errx(1, "Statistics counter is not implemented");
			break;
        
		default:
			warnx("spy: read invalid uaddr:%6o", uaddr);
            bus_interface_set_unibus_nxm();
	}

}

void
diagnostic_interface_write(uint32_t uaddr, uint16_t v)
{
    switch (uaddr) 
	{
		case 0766000:
			INFO(TRACE_SPY, "spy: write DEBUG-IR<15-0>: %06o\n", v);
			debug_ir = (debug_ir & 0xFFFFFFFFFFFF0000) | ((uint64_t)v);
			break;

		case 0766002:
			INFO(TRACE_SPY, "spy: write DEBUG-IR<31-16>: %06o\n", v);
			debug_ir = (debug_ir & 0xFFFFFFFF0000FFFF) | ((uint64_t)v << 16);
			break;

		case 0766004:
			INFO(TRACE_SPY, "spy: write DEBUG-IR<47-32>: %06o\n", v);
			debug_ir = (debug_ir & 0xFFFF0000FFFFFFFF) | ((uint64_t)v << 32);
			INFO(TRACE_SPY, "spy: DEBUG-IR<47-0>: %018o %s\n", 
					debug_ir, uinst_desc(debug_ir, &sym_mcr));
			break;

		case 0766006:
            INFO(TRACE_SPY, "spy: write clock control register: 0%02o\n", v);
            if (v != 1)
            {
                errx(1, "Clock control register can only be loaded with 1 (RUN)");
            }
            DEBUG(TRACE_USIM, "spy: clock control register=RUN\n");
			break;

		case 0766010:
            INFO(TRACE_SPY, "spy: write OPC control register: 0%06o\n", v);
            errx(1, "OPC control register should not be touched");
			break;

		case 0766012:
            NOTICE(TRACE_SPY, "spy: write mode register: 0%02o\n", v);
            machine_state.promdisabled = ((v & (1<<5)) != 0);
            DEBUG(TRACE_USIM, "spy: promdisabled=%s\n", 
                    machine_state.promdisabled ? "true" : "false");
			break;

		case 0766014: // not used
			break;

		case 0766016: // not used
			break;
			
#ifndef NDEBUG

		// usim specific debug feature
		// 
		// write parameters first
		// for dump main memory
		// write low address to 0766020
		// write high address to 0766022
		// write count to 0766024
		case 0766020:
			switch (v)
			{
				// nop
				case 0: break;
				case 1: dump_main_memory(); break;
				default:
					warnx("spy: invalid usim debug command: %06o\n", v);
			}
			break;

		case 0766022: param1 = v; break;
		case 0766024: param2 = v; break;
		case 0766026: param3 = v; break;

#endif

		default:
			warnx("spy: write invalid uaddr:%6o", uaddr);
            bus_interface_set_unibus_nxm();
	}
}
