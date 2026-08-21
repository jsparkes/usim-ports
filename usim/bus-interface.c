/* bus-interface.c --- CADR bus interface
 *
 */

#include <assert.h>
#include <err.h>
#include <stdbool.h>
#include <stdint.h>

#include "bus-interface.h"
#include "disk-controller.h"
#include "iob.h"
#include "lashup.h"
#include "lashup-debugger.h"
#include "main-memory.h"
#include "tape-controller.h"
#include "tv.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

// "... accumulated error status bits from previous bus cycles."
// "Note that these bits are not cleared by power up."
// only Xbus NXM Error, Unibus NXM Error and Unibus Map Error are used in usim
// others are parity errors, no parity errors in usim
static uint16_t bus_error_status = 0;

// this is controlled by the debugger
static bool nxm_inhibited = false;

// used to detect 1-0 transition of reset bit
static bool modifier_reset = false;

// data = 766100 rw, is not kept because it is the last register accessed
// and it actually executes the operation over socket

// this mirrors the bus_error_status of debuggee
static uint16_t debuggee_bus_error_status;	// 766104 ro

static bool addr17;	// 766110<1> wo															
// reset unibus and bus interface = 766110<2> wo is not kept because it is 
// executed directly
// timeout inhibit = 766110<3> wo is not kept because it is executed directly

static uint16_t addr;	// 766114 wo

static uint32_t
bus_interface_get_debuggee_uaddr()
{
    return ((addr17 ? ((uint32_t)1) : ((uint32_t)0)) << 17) | (((uint32_t)addr) << 1);
}

uint16_t
bus_interface_get_bus_error_status(void)
{
    return bus_error_status;
}

bool
bus_interface_is_xbus_nxm(void)
{
    return (bus_error_status & 01) != 0;
}

bool
bus_interface_is_unibus_nxm(void)
{
    return (bus_error_status & 010) != 0;
}

bool
bus_interface_is_unibus_map_error(void)
{
    return (bus_error_status & 040) != 0;
}

void
bus_interface_reset_bus_error_status(void)
{
    bus_error_status = 0;
}

void
bus_interface_set_debuggee_bus_error_status(uint16_t status)
{
    debuggee_bus_error_status = status;
}

void
bus_interface_set_nxm_inhibit(bool inhibit)
{
    nxm_inhibited = inhibit;
}

void
bus_interface_set_xbus_nxm(void)
{
    if (nxm_inhibited) return;

	// "Xbus NXM Error. 
	// Set when an Xbus cycle times out for lack of response."
	bus_error_status |= 01;
}

// see io/unibus.lisp:UNIBUS-EXISTS-P
void
bus_interface_set_unibus_nxm(void)
{
    if (nxm_inhibited) return;

	// "Unibus NXM Error. 
	// Set when a Unibus cycle times out for lack of response."
	bus_error_status |= 010;
}

void
bus_interface_set_unibus_map_error(void)
{
	// "Unibus Map Error. Set when an attempt to perform an Xbus cycle through
	// the Unibus map is refused because the map specifies invalid or 
	// write-protected."
	bus_error_status |= 040;
}

void
bus_interface_read(uint32_t uaddr, uint16_t *pv)
{
    switch (uaddr) 
	{
        case 0766040:
            *pv = interrupt_status_reg;
			break;

        // 0766042: is write-only

		case 0766044:
            DEBUG(TRACE_BUS_INTERFACE, "bus-interface: read bus status: %06o\n", 
                    bus_error_status);
            *pv = bus_error_status;
			break;

        case 0766100:
            {
                const uint32_t debuggee_uaddr = bus_interface_get_debuggee_uaddr();
                if (lashup_debugger_read(debuggee_uaddr, pv))
                {
                    DEBUG(TRACE_BUS_INTERFACE, "bus-interface: read data: %06o from %06o\n", 
                            *pv, uaddr);
                }
                else
                {
                    WARNING(TRACE_USIM, "bus-interface: read failed\n");
                    *pv = 0;
                }
            }
            break;

        // status for bus cycles executed on the debuggee's busses
        // read only
        // cleared by writing into debuggee's 766044 (error status)
        case 0766104:
            *pv = debuggee_bus_error_status;
            DEBUG(TRACE_BUS_INTERFACE, "bus-interface: read status: %06o\n", *pv);
            break;

		default:
			warnx("bus-interface: read invalid uaddr:%6o", uaddr);
            bus_interface_set_unibus_nxm();
	}

}

void
bus_interface_write(uint32_t uaddr, uint16_t v)
{
    switch (uaddr) 
	{
        case 0766040:
            // "Writing this location writes into bits 0 and 10-13 
            // (mask 36001) of the above register. This is used to 
            // change the "interrupt level" and to re-enable acceptance 
            // of Unibus interrupts after processing an interrupt."
            set_interrupt_status_reg((interrupt_status_reg & ~0036001) | (v & 0036001));
			break;

		case 0766042:
            // "Writing this location writes into bits 2-9 and 15 
            // (mask 101774) of the above register. This is used to simulate 
            // Unibus interrupts and to clear bit 15 (Unibus Interrupt) 
            // after processing an interrupt."
            set_interrupt_status_reg((interrupt_status_reg & ~0101774) | (v & 0101774));
			break;

		case 0766044:
            DEBUG(TRACE_BUS_INTERFACE, "bus-interface: write (clear) bus status\n");
            // "Writing this location ignores the data written and 
            // clears the status bits."
            bus_error_status = 0;

            // "766104 -> These bits are cleared by writing into location 766044
            // (Error Status) on the debuggee's Unibus."
            debuggee_bus_error_status = 0;
			break;

        case 0766100:
            {
                const uint32_t debuggee_uaddr = bus_interface_get_debuggee_uaddr();
                DEBUG(TRACE_BUS_INTERFACE, "bus-interface: write data: %06o to %06o\n", v, uaddr);
                if (lashup_debugger_write(debuggee_uaddr, v))
                {
                    // writing into debuggee's error status (766044)
                    // resets debugger's status also
                    if (debuggee_uaddr == 0766044)
                    {
                        debuggee_bus_error_status = 0;
                    }
                }
            }
            break;

        case 0766102:
            {
                const uint8_t cmd = ((v >> 8) & 0377);
                const uint8_t param = (v & 0377);

                DEBUG(TRACE_BUS_INTERFACE, "bus-interface: remote usim command: #o%o, param: #o%o\n");

                lashup_debugger_usim(cmd, param);
            }
            break;
        
        case 0766110:
            {
                addr17 = ((v & 01) != 0);
                const bool reset = ((v & 02) != 0);
                const bool timeout_inhibit = ((v & 04) != 0);
                const bool debuggee_mark = ((v & 010) != 0);
                const bool debugger_mark = ((v & 020) != 0);
                const bool ping = ((v & 040) != 0);
                const uint8_t mark_symbol = ((v >> 8) & 0377);
                DEBUG(TRACE_BUS_INTERFACE, "bus-interface: write modifier bits %06o: addr17:%d reset:%d timeout_inhibit:%d\n", 
                        v, addr17, reset, timeout_inhibit);

                if (debugger_mark) lashup_debugger_mark_debugger(mark_symbol); 
                if (debuggee_mark) lashup_debugger_mark_debuggee(mark_symbol);
                // break so timeout request is not sent
                if (debugger_mark || debuggee_mark) break;

                if (ping)
                {
                    lashup_debugger_ping();
                    break;
                }

                if ((modifier_reset) && (!reset))
                {
                    if (!lashup_debugger_reset_unibus_and_bus_interface())
                    {
                        DEBUG(TRACE_BUS_INTERFACE, "bus-interface: cannot execute reset\n");
                    }
                }
                modifier_reset = reset;
                uint16_t timeout_inhibit_data = timeout_inhibit ? 1 : 0; 
                if (!lashup_debugger_inhibit_nxm(timeout_inhibit_data))
                {
                    DEBUG(TRACE_BUS_INTERFACE, "bus-interface: cannot execute timeout inhibit\n");
                }
            }
            break;

        case 0766112:
            {
                __attribute__((unused)) const uint8_t cmd = ((v >> 8) & 0377);
                __attribute__((unused)) const uint8_t param = (v & 0377);
                DEBUG(TRACE_BUS_INTERFACE, "bus-interface: local usim command: #o%o, param: #o%o\n",
                        cmd, param);
            }
            break;


        case 0766114:
            addr = v;
            DEBUG(TRACE_BUS_INTERFACE, "bus-interface: write address: %06o (uaddr:%6o)\n", 
                    v, bus_interface_get_debuggee_uaddr());
            break;

		default:
			warnx("bus-interface: write invalid uaddr:%6o", uaddr);
            bus_interface_set_unibus_nxm();
	}

}

void 
bus_interface_bus_reset(void)
{
    bus_error_status = 0;
    nxm_inhibited = false;
    modifier_reset = false;
    debuggee_bus_error_status = 0;
    addr17 = 0;
    addr = 0;

    // Unibus reset (BUS.INIT L)
    iob_bus_reset();
    tape_controller_bus_reset();
    
    // Xbus reset (XBUS.INIT L)
    main_memory_bus_reset();
    disk_controller_bus_reset();
    tv_bus_reset();
}
