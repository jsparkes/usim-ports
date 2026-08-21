/* bus-adaptor.c --- CADR bus adaptor
 *
 * All bus operations (including main memory) is initiated from bus-adaptor.
 * It connects uvmem to devices on the bus.
 * bus-adaptor knows the memory map.
 * bus-adaptor calls Xbus devices either with paddr or register offset.
 * bus-adaptor calls Unibus devices with unibus addresses.
 *
 * Notes about memory addresses:
 *
 * - vaddr is 24-bit virtual address, word addressing
 * - paddr is 22-bit physical address, word addressing
 * - (physical) page number is 14-bit (16K pages, 0-040000)
 * - uaddr is 16-bit unibus address, byte addressing
 *
 * - because uaddr is 16-bit and byte addressing, it can be found like this:
 *   unibus pages start from 037000
 *	 uaddr = (((pn - 037000) << 8) + (paddr & 0xFF)) << 1;
 *	 it is << 1 not << 2 because only even addresses are used
 *
 * Attention: return 0 for unknown/onpopulated memory addresses
 * if ff is returned, color tv probe fails in system 301 and 300
 */

#include <assert.h>
#include <err.h>
#include <stdint.h>

#include "bus-interface.h"
#include "colortv.h"
#include "config.h"
#include "diagnostic-interface.h"
#include "disk-controller.h"
#include "iob.h"
#include "main-memory.h"
#include "tape-controller.h"
#include "unibus-mapping.h"
#include "tv.h"
#include "ucode.h"
#include "utrace.h"
#include "usim.h"

static void
bus_adaptor_xbusio_rw(bool write, uint32_t paddr, uint32_t *pv)
{
#ifdef MONITOR_XBUSIO_ADDR
    if (paddr == MONITOR_XBUSIO_ADDR)
    {
        if (write)
        {
            warnx("bus-adaptor: xbusio write paddr:#o%08o, data:#o%o", 
                    paddr, *pv);
        }
    }
#endif

    // "The unibus and xbus can be referred to through the virtual memory 
    // of the Lisp machine. When this is done, ...<stuff about unibus>...
    // The xbus i/o space runs from 77000000 to 77377777. 

    // The xbus i/o space contains 32-bit words just like Lisp machine memory."
    // uint32_t *pv
    
    // rather than sending paddr to devices below
    // the register offsets are calculated
    // one reason is, there can be multiple instances of the same device
    // such as disk
    // second is, the registers addresses are documented not paddr
    
    // Main TV screen
    // window/shwarm.lisp:MAIN-SCREEN-BUFFER-ADDRESS IO-SPACE-VIRTUAl-ADDRESS
    if (017000000 <= paddr && paddr <= 017077777)
    {
        uint32_t video_offset = paddr - 017000000;
        if (write) tv_screen_write(video_offset, *pv);
        else tv_screen_read(video_offset, pv);
    }
    // Color TV
    else if (colortv_enabled && 017200000 <= paddr && paddr <= 017277777)
    {
        uint32_t video_offset = paddr - 017200000;
        if (write) colortv_screen_write(video_offset, *pv);
        else colortv_screen_read(video_offset, pv);
    }
    // Color TV control
    else if (colortv_enabled && 017377750 <= paddr && paddr <= 017377757)
    {
        const uint32_t tv_offset = paddr - 017377750;
        if (write) colortv_control_write(tv_offset, *pv);
        else colortv_control_read(tv_offset, pv);
    }
    // Main TV control
    // window/shwarm.lisp:MAIN-SCREEN-CONTROL-ADDRESS 377760
    else if (017377760 <= paddr && paddr <= 017377767)
    {
        const uint32_t tv_offset = paddr - 017377760;
        if (write) tv_control_write(tv_offset, *pv);
        else tv_control_read(tv_offset, pv);
    }

    // Second disk controller would be here
    // second disk controller is not supported
    // disk units 8-15 are not supported
    // and it will just stall when accessed
    // e.g. (print-disk-label 8) will stall
    
    // (First) Disk control, for disk units 0-7
    else if (017377774 <= paddr && paddr <= 017377777) 
    {
        const uint32_t disk_controller_offset = paddr - 017377774;
        if (write) disk_controller_write(disk_controller_offset, *pv);
        else disk_controller_read(disk_controller_offset, pv);
    }
    // others, not implemented, not mapped
    else
    {
        // do not errx here, there are things probed on the bus
        if (write)
        {
#ifdef NDEBUG
            // this always happen at startup
            // probably due to the PROM code below:
            // (JUMP-LESS-THAN VMA A-400 PAGE-0-PARITY-FIX) ;This does one extra location, too bad.
            // suppress it in release builds
            if (paddr != 017377400)
            {
#endif
                warnx("xbus: write unknown paddr:#o%08o v:#o%o", paddr, *pv);
#ifdef NDEBUG
            }
#endif
        }
        else
        {
#ifdef NDEBUG
            // this always happen at startup
            // probably due to the PROM code below:
            // (JUMP-LESS-THAN VMA A-400 PAGE-0-PARITY-FIX) ;This does one extra location, too bad.
            // suppress it in release builds
            if (paddr != 017377400)
            {
#endif
                warnx("xbus: read unknown paddr:#o%08o", paddr);
#ifdef NDEBUG
            }
#endif
            *pv = 0;
        }

        bus_interface_set_xbus_nxm();
    }

#ifdef MONITOR_XBUSIO_ADDR
    if (paddr == MONITOR_XBUSIO_ADDR)
    {
        if (!write)
        {
            warnx("bus-adaptor: xbusio  read paddr:#o%08o, data:#o%o", paddr, *pv);
        }
    }
#endif
}

static void
bus_adaptor_xbus_rw(bool write, uint32_t paddr, uint32_t *pv)
{
    const uint32_t pn = (paddr >> 8) & 0x3FFF;

    if (/*000000 <= pn &&*/ pn <= 035773)
    {
        if (write) main_memory_write(paddr, *pv);
        else main_memory_read(paddr, pv);
    }
    // xbus i/o
    else if (036000 <= pn && pn <= 036777)
    {
        bus_adaptor_xbusio_rw(write, paddr, pv);
    }
    else
    {
        if (write)
        {
            warnx("xbus: write unknown paddr:#o%08o v:#o%o", paddr, *pv);
        }
        else
        {
            warnx("xbus: read unknown paddr:#o%08o", paddr);
            *pv = 0;
        }

        bus_interface_set_xbus_nxm();
    }
}

// using uaddr is ideal for unibus, because it is also what is documented
static void
bus_adaptor_unibus_rw(bool write, uint32_t uaddr, uint16_t *pv)
{
#ifdef MONITOR_UNIBUS_ADDR
    if (uaddr == MONITOR_UNIBUS_ADDR)
    {
        if (write)
        {
            warnx("bus-adaptor: unibus write uaddr:#o%06o, data:#o%o",
                    uaddr, *pv);
        }
    }
#endif

    // Unibus Map
    if (0140000 <= uaddr && uaddr <= 0177777)
    {
        // "Unibus locations 140000-177777 are divided into 16 pages 
        // which can be mapped anywhere in Xbus physical address space. Each page 
        // is 512 16-bit words or 256 32-bit words long, the same size as 
        // the pages of the CADR virtual memory."
        const size_t page_no = (uaddr - 0140000) / 02000;

        // corresponding mapping register
        const uint16_t mapping_register = unibus_mapping_registers[page_no];

        // "Bit 15 is the map-valid bit. If this is 0, this mapping register is 
        // not set up, and will not respond to the Unibus; 
        // NXM timeout will occur and an Error Status bit will be set."
        const bool map_valid = (((mapping_register >> 15) & 1) != 0);

        // "Bit 14 is the write-permit bit. If this is 0, this mapping register 
        // will not respond to Unibus writes; NXM timeout will occur and 
        // an Error Status bit will be set."
        const bool write_permit = (((mapping_register >> 14) & 1) != 0);

        // "Bits 13-0 contain the Xbus page number."
        const uint16_t xbus_page_number = mapping_register & 037777;

        // "These bits (xbus page number) are concatenated with bits 9-2 of 
        // the Unibus address to produce the mapped Xbus address."
        // xbus_page_number[13:0] | uaddr[9:2]
        // 14 bit | 8 bit = 22 bit
        const uint32_t paddr = (((uint32_t)xbus_page_number) << 8) | (((uint32_t)uaddr >>2 ) & 0377);

        DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: mapping register[%d] = #o%06o = %s %s pn=#o%06o uaddr:#o%06o paddr:#o%08o\n",
                page_no,
                mapping_register,
                map_valid ? "MAP_VALID" : "map_valid",
                write_permit ? "WRITE_PERMIT" : "write_permit", 
                xbus_page_number,
                uaddr,
                paddr);

        if (!map_valid)
        {
            if (!write) *pv = 0;
            bus_interface_set_unibus_map_error();
            return;
        }
        
        if (write && !write_permit)
        {
            bus_interface_set_unibus_map_error();
            return;
        }

        const bool hiword = (((uaddr >> 1) & 1) != 0);

        uint32_t v32;

        // this is above xbos io space
        // "An additional feature is that writing an Xbus address of 17400000 
        // or higher through the Unibus map writes into CADR’s MD register. 
        // This provides a 32-bit parallel data path into the processor for 
        // diagnostic purposes. These Xbus addresses are otherwise unusable, 
        // because they are used by the processor to address the Unibus."
        if (xbus_page_number >= 037000)
        {
            if (write)
            {
                if (hiword)
                {
                    v32 = md_reg & 0x0000FFFF;
                    v32 |= ((((uint32_t)*pv) << 16) & 0xFFFF0000);
                }
                else
                {
                    v32 = md_reg & 0xFFFF0000;
                    v32 |= ((uint32_t)*pv & 0x0000FFFF);
                }

                md_reg = v32;

                DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: dma write MD (%s) md_reg=#o%011o *pv=#o%06o\n", 
                        hiword ? "HI" : "LO", md_reg, *pv);
            }
            else
            {
                if (hiword)
                {
                    *pv = (md_reg >> 16) & 0x0000FFFF;
                }
                else
                {
                    *pv = md_reg & 0x0000FFFF;
                }

                DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: dma read MD (%s) md_reg=#o%011o *pv=#o%06o\n", 
                        hiword ? "HI" : "LO", md_reg, *pv);
            }
        }
        // "Each Xbus location occupies 4 Unibus byte addresses. It takes 
        // two 16-bit Unibus cycles to read or write one 32-bit Xbus location. 
        // 16 buffers (one for each page) are provided to hold the data 
        // between the two Unibus cycles. As long as each page is only in 
        // use by a single bus-master, the right thing will happen."
        //
        // I think, first low address is accessed, then the high.
        // For read, low address initiates a xbus read, 
        //  high will return from the buffer.
        // For write, low address will be stored in the buffer, 
        //  high address initiates a xbus write.
        // At least this is how DBG-READ-XBUS and DBG-WRITE-XBUS
        //  is implemented.
        else
        {
            if (write)
            {
                if (hiword)
                {
                    uint16_t cached_lo = unibus_mapping_buffers[page_no];
                    uint32_t v32 = ((((uint32_t)*pv) << 16) & 0xFFFF0000) | (uint32_t)cached_lo;

                    bus_adaptor_xbus_rw(true, paddr, &v32);

                    DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: dma write (HI) paddr:#o%08o LO=#o%06o *pv=#o%06o v32=#o%011o\n", 
                            paddr, cached_lo, *pv, v32);
                }
                else
                {
                    // cache low word
                    unibus_mapping_buffers[page_no] = *pv;

                    DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: dma write (LO) paddr:#o%08o *pv=#o%06o\n", 
                            paddr, *pv);
                }
            }
            else
            {
                if (hiword)
                {
                    *pv = unibus_mapping_buffers[page_no];

                    DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: dma read (HI) paddr:#o%08o *pv=#o%06o\n", 
                            paddr, *pv);
                }
                else
                {
                    bus_adaptor_xbus_rw(false, paddr, &v32);

                    *pv = (uint16_t)(v32 & 0x0000FFFF);

                    // cache low word
                    unibus_mapping_buffers[page_no] = (uint16_t)((v32>>16) & 0x0000FFFF);

                    DEBUG(TRACE_UNIBUS_MAPPING, "unibus-mapping: dma read (LO) paddr:#o%08o v32=#o%011o *pv=#o%06o HI=#o%06o\n", 
                            paddr, v32, *pv, unibus_mapping_buffers[page_no]);
                }
            }
        }
    }
    else if (0764000 <= uaddr && uaddr <= 0764176)
    {
        if (write) iob_unibus_write(uaddr, *pv);
        else iob_unibus_read(uaddr, pv);
    }
    else if (0766000 <= uaddr && uaddr <= 0766036)
    {
        if (write) diagnostic_interface_write(uaddr, *pv);
        else diagnostic_interface_read(uaddr, pv);
    }
    else if (0766040 <= uaddr && uaddr <= 0766136)
    {
        if (write) bus_interface_write(uaddr, *pv);
        else bus_interface_read(uaddr, pv);
    }
    else if (0766140 <= uaddr && uaddr <= 0766176)
    {
        if (write) unibus_mapping_write(uaddr, *pv);
        else unibus_mapping_read(uaddr, pv);
    }
    else if (0772520 <= uaddr && uaddr <= 0772532)
    {
        if (write) tape_controller_unibus_write(uaddr, *pv);
        else tape_controller_unibus_read(uaddr, pv);
    }
    else
    {
        // do not errx here, there are things probed on the bus
        if (write)
        {
            warnx("unibus: write unknown uaddr:%06o v:%o", uaddr, *pv);
        }
        else
        {
#ifdef NDEBUG
            // this always happen at startup
            // it is an undocumented address
            // suppress it in release builds
            if (uaddr != 0400000)
            {
#endif
                warnx("unibus: read unknown uaddr:%06o", uaddr);
#ifdef NDEBUG
            }
#endif
            *pv = 0;
        }

        bus_interface_set_unibus_nxm();
    }

#ifdef MONITOR_UNIBUS_ADDR
    if (uaddr == MONITOR_UNIBUS_ADDR)
    {
        if (!write)
        {
            warnx("bus-adaptor: unibus  read uaddr:#o%06o, data:#o%o", 
                    uaddr, *pv);
        }
    }
#endif
}

static void
bus_adaptor_rw(bool write, uint32_t paddr, uint32_t *pv)
{
    assert ((paddr & 0xFFC00000) == 0);

    const uint32_t pn = (paddr >> 8) & 0x3FFF;
    
    // xbus main memory
    if (/*000000 <= pn &&*/ pn <= 035773)
    {
        bus_adaptor_xbus_rw(write, paddr, pv);
    }
    // 035774-035777 is handled in uvmem
    // xbus i/o
    else if (036000 <= pn && pn <= 036777)
    {
        bus_adaptor_xbus_rw(write, paddr, pv);
    }
    // Unibus
    else if (037000 <= pn && pn <= 037777)
    {
        // "The unibus and xbus can be referred to through the virtual memory 
        // of the Lisp machine. When this is done, the unibus runs from 
        // address 77400000 to address 77777777."

        // "The unibus is made of 16-bit words, and each word is stored in the 
        // least significant bits of one Lisp machine word."
        uint16_t pv16;
        if (write) pv16 = *pv & 0xFFFF;

        // "So, to convert a unibus address to a Lisp machine virtual address, 
        // you must divide by two (bytes to words) and then add 77400000."
        //
        // however the above comment is not true when machine is booting (prom)
        // for example the first access is to the mode register with vaddr=00001005
        // but virtual mapping is setup so pn=37766
        // so it is best to make this calculation from the page number
        const uint32_t uaddr = (((pn - 037000) << 8) + (paddr & 0xFF)) << 1;

        bus_adaptor_unibus_rw(write, uaddr, &pv16);

        if (!write) *pv = 0x00000000 | pv16;
    }
    else
    {
        errx(1, "vm: %s impossible paddr:#o%08o pn:#o%08o", 
                write ? "write" : "read", paddr, pn);
    }
}

void
bus_adaptor_read(uint32_t paddr, uint32_t *pv)
{
    bus_adaptor_rw(false, paddr, pv);
}

void
bus_adaptor_write(uint32_t paddr, uint32_t v)
{
    bus_adaptor_rw(true, paddr, &v);
}

void
bus_adaptor_unibus_read(uint32_t uaddr, uint16_t *pv)
{
    bus_adaptor_unibus_rw(false, uaddr, pv);
}

void
bus_adaptor_unibus_write(uint32_t uaddr, uint16_t v)
{
    bus_adaptor_unibus_rw(true, uaddr, &v);
}