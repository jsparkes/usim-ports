/* unibus-mapping.c --- CADR Unibus mapping
 *
 *
 */

#include <assert.h>
#include <err.h>
#include <stdbool.h>
#include <stdint.h>

#include "bus-interface.h"
#include "ucode.h"
#include "unibus-mapping.h"
#include "utrace.h"

uint16_t unibus_mapping_registers[16];
uint16_t unibus_mapping_buffers[16];

void
unibus_mapping_rw(bool write, uint32_t uaddr, uint16_t *pv)
{
	switch (uaddr) 
	{
        // CADR XBUS <-> UNIBUS mapping registers
		// 16 registers for 16 pages (#o140000 - #o177777, each page #o2000 bytes)
		case 0766140:
		case 0766142:
		case 0766144:
		case 0766146:
		case 0766150:
		case 0766152:
		case 0766154:
		case 0766156:
		case 0766160:
		case 0766162:
		case 0766164:
		case 0766166:
		case 0766170:
		case 0766172:
		case 0766174:
		case 0766176:
			{
				const uint32_t page_no = (uaddr - 0766140) / 2;
				if (write) 
				{
					unibus_mapping_registers[page_no] = *pv;
#ifndef NDEBUG
					const bool map_valid = (((*pv >> 15) & 1) != 0);
					const bool write_permit = (((*pv >> 14) & 1) != 0);
					const uint16_t xbus_page_number = *pv & 037777;
					INFO(TRACE_UNIBUS_MAPPING, "unibus-mapping: write mapping_register[%d] = #o%06o = %s %s pn:#o%06o (0x%04x)\n",
							page_no,
							unibus_mapping_registers[page_no],
							map_valid ? "MAP_VALID" : "map_valid",
							write_permit ? "WRITE_PERMIT" : "write_permit", 
							xbus_page_number,
							xbus_page_number);
#endif                    
				}
				else
				{
					*pv = unibus_mapping_registers[page_no];
#ifndef NDEBUG
					const bool map_valid = (((*pv >> 15) & 1) != 0);
					const bool write_permit = (((*pv >> 14) & 1) != 0);
					const uint16_t xbus_page_number = *pv & 037777;
					INFO(TRACE_UNIBUS_MAPPING, "unibus-mapping: read mapping_register[%d] = #o%06o = %s %s pn:#o%06o (0x%04x)\n",
							page_no,
							*pv, 
							map_valid ? "MAP_VALID" : "map_valid",
							write_permit ? "WRITE_PERMIT" : "write_permit", 
							xbus_page_number,
							xbus_page_number);
#endif
				}
			}
			break;

		default:
			warnx("unibus-mapping: read invalid uaddr:#o%06o", uaddr);
			bus_interface_set_unibus_nxm();
	}
}

void
unibus_mapping_read(uint32_t uaddr, uint16_t *pv)
{
	unibus_mapping_rw(false, uaddr, pv);
}

void
unibus_mapping_write(uint32_t uaddr, uint16_t v)
{
	unibus_mapping_rw(true, uaddr, &v);
}
