/* uvmem.c --- CADR virtual memory routines
 *
 * handles all virtual memory related operations.
 *
 * uvmem is the bridge between the processor (uexec) and bus (bus-adaptor).
 */

#include <assert.h>
#include <err.h>
#include <stdint.h>
#include <string.h>

#include "bus-adaptor.h"
#include "config.h"
#include "dump.h"
#include "machine-control.h"
#include "main-memory.h"
#include "ucode.h"
#include "utrace.h"
#include "uvmem.h"

// see also ucadr/uc-page-fault.lisp for bit positions

// "The First Level Map contains 2048 5-bit locations, and is addressed by 
// bits <23-13> of the VMA or MD."
//
// block = VMA[23:13] = 11-bit, 2048 blocks in total
//
uint32_t l1_map[2048];

// "The Second Level Map contains 1024 24-bit locations, and is addressed by 
// the concatenation of the output from the First Level Map and bits <12-8> of
// the VMA or MD."
//
// "The output of the Second Level Map consists of:"
// - MAP<23> = access permission
// - MAP<22> = write permission
// - MAP<21-14> = available for software, not used for memory mapping
//              = bits 15 and 14 can be used by DISPATCH instruction
// - MAP<13-0> = physical page number
//
// The physical address sent to memory is the concatenation of the physical 
// page number and bits 7-0 of the virtual address.
// 
// In summary:
// 
// L1_index = vaddr[23:13]
// L1_data  = 5-bit = L1[L1_index]
// L2_index = concat(L1_data, vaddr[12:8])
// L2_data  = 14-bit = L2[L2_index] = physical page number, 16K (2^14) pages
// paddr    = concat(L2_data, vaddr[7:0])
//
// Although 16K pages are possible, in reality there cannot be that much 
// main memory (16K*256W=4MW). Because some of these pages are used for other 
// xbus and unibus devices. Thus, actual main memory is 2MW.
//
// L2 output also contains access and write permission bits
// 
uint32_t l2_map[1024];

/*
 * resolves a virtual address to physical address
 *
 * bus operates on physical address, so this is needed for any bus read/write
 *
 * vaddr is 24-bit virtual address
 * pl1_data, if not NULL, will contain L1 map entry data
 * pl2_data, if not NULL, will contain L2 map entry data
 * pphysical_page_number, if not NULL, will contain the physical page number
 * pwrite_permission, if not NULL, will be true if there is write permission (bit is 1)
 * paccess_permission, if not NULL, will be true if there is access permission (bit is 1)
 * 
 * returns paddr, 22-bit physical address
 *
 * l2_data is actually enough to find all other information
 * but it is more convenient to use this function like this
 */
uint32_t
uvmem_vtop(
		uint32_t vaddr,
		uint32_t *pl1_data,
		uint32_t *pl2_data,
		uint32_t *pphysical_page_number,
		bool *pwrite_permission,
		bool *paccess_permission)
{
	// hardware does not care the upper 8 bits
	vaddr = vaddr & 0x00FFFFFF;

	const uint32_t l1_index = (vaddr >> 13) & 03777;
	const uint32_t l1_data = l1_map[l1_index] & 037;

	assert ((l1_data & 0xFFFFFFE0) == 0);

	const uint32_t l2_index = (l1_data << 5) | ((vaddr >> 8) & 037);
	const uint32_t l2_data = l2_map[l2_index];

	assert ((l2_data & 0xFF000000) == 0);

	const uint32_t physical_page_number = l2_data & 0x3FFF;
	const bool write_permission = (l2_data & (1<<22)) != 0;
	const bool access_permission = (l2_data & (1<<23)) != 0;

	if (pl1_data != NULL) *pl1_data = l1_data;
	if (pl2_data != NULL) *pl2_data = l2_data;
	if (pphysical_page_number != NULL) *pphysical_page_number = physical_page_number;
	if (pwrite_permission != NULL) *pwrite_permission = write_permission;
	if (paccess_permission != NULL) *paccess_permission = access_permission;

	return (physical_page_number << 8) | (vaddr & 0xFF);
}

/* updates an L1 and/or L2 map entry
 *
 * called from uexec for VMA-WRITE-MAP and MEMORY-DATA-WRITE-MAP instructions
 *
 * it is called by uexec with vma=vma_reg and md=md_reg
 */

void
uvmem_write_map(uint32_t vma, uint32_t md)
{
	// "The first level map is written from bits <31-27> of the VMA, if VMA<26> is a 1."
	//
	// "When writing the second level map, the first level map supplied part of the address,
	// and must have been written previously. Therefore, it is not useful to write both at
	// the same time, although it is possible to set both bits to 1."
	//
	// "The second level map is written from VMA<23-0>, if VMA<25> is a 1."
		
	// ucadr/uc-page-fault.lisp:MAP-WRITE-ENABLE-FIRST-LEVEL-WRITE
	bool enable_first_level_write = (vma & (1 << 26)) != 0;
	// ucadr/uc-page-fault.lisp:MAP-WRITE-ENABLE-SECOND-LEVEL-WRITE
	bool enable_second_level_write = (vma & (1 << 25)) != 0;

	if (enable_first_level_write)
	{
		const uint32_t l1_index = (md >> 13) & 03777;
		// ucadr/uc-page-fault.lisp:MAP-WRITE-FIRST-LEVEL-MAP
		const uint32_t l1_data = (vma >> 27) & 037;
		l1_map[l1_index] = l1_data;
		DEBUG(TRACE_VM, "l1_map[#o%o] <- #o%o\n", l1_index, l1_data);
	}

	if (enable_second_level_write)
	{
		const uint32_t l1_index = (md >> 13) & 03777;
		const uint32_t l1_data = l1_map[l1_index];
		assert ((l1_data & 0xFFFFFFE0) == 0); // l1_data should be 5-bits
		const uint32_t l2_index = (l1_data << 5) | ((md >> 8) & 037);
		// ucadr/uc-page-fault.lisp:MAP-WRITE-SECOND-LEVEL-MAP
		const uint32_t l2_data = vma & 077777777;
		assert ((l2_data & 0xFF000000)  == 0); // l2_data should be 24-bits
		l2_map[l2_index] = l2_data;
		DEBUG(TRACE_VM, "l2_map[#o%o] <- #o%o\n", l2_index, l2_data);
	}
}

/*
 * read/write from/to virtual memory
 * 
 * vaddr is 24-bit 
 * v or *pv is 32-bit data
 *
 * Basically:
 * - resolves virtual to physical
 * - check access rights
 * - if this is a read/write to amem, execute it
 * - if not, execute bus read/write through bus adaptor
 */

void
vm(bool write, int vaddr, uint32_t *pv)
{
	// hardware does not care the upper 8 bits
	vaddr = vaddr & 0x00FFFFFF;

	bool write_permission;
	bool access_permission;
	uint32_t pn;
	uint32_t paddr = uvmem_vtop(
			vaddr, NULL, NULL, &pn, &write_permission, &access_permission);

	if (write) machine_state.vmaok = access_permission && write_permission;
	else machine_state.vmaok = access_permission;

	//printf("main vaddr:%08o pn:%o (awp:%d%d%d) paddr:%08o\n", vaddr, pn, access_fault_bit, write_fault_bit, !vmaok, paddr);
	
	if (!machine_state.vmaok)
	{
		*pv = 0;
        DEBUG(TRACE_VM, "vmRead(vaddr=#o%08o) %s %s fault\n", 
                vaddr, 
                access_permission ? "access_permitted" : "access_fault", 
                write_permission ? "write_permitted" : "write_fault");
	}
	// amem
	// this is here not in the bus adaptor because it is close to the processor
	else if (035774 <= pn && pn <= 035777)
	{
		uint32_t offset = ((pn - 035774) << 8) + (vaddr & 0xFF);
		assert (false);
		if (write) amem[offset] = *pv;
		else *pv = amem[offset];
	}
	else if (000000 <= pn && pn <= 037777)
	{
		// for tv screen, this is not working
		// for the access below
		// main vaddr:77051765 pn:36000 (awp:000) paddr:17000365
		// pn:036000 paddr:17000365 video_offset=365
		// actual paddr should be 17'051'765
		// no idea why
		
		if (pn == 036000)
		{
			paddr = 017000000 | (vaddr & 077777);
		}

		if (write) bus_adaptor_write(paddr, *pv);
		else bus_adaptor_read(paddr, pv);
	}
	else
	{
		errx(1, "vm: %s impossible vaddr:#o%08o pn:#o%08o", 
				write ? "write" : "read", vaddr, pn);
	}
}
