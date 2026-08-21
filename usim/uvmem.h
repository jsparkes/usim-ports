#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "config.h"
#include "dump.h"

extern uint32_t l1_map[2048];
extern uint32_t l2_map[1024];

uint32_t uvmem_vtop(
        uint32_t vaddr, 
        uint32_t *pl1_data, 
        uint32_t *pl2_data, 
        uint32_t *pphysical_page_number,
        bool *pwrite_permission,
        bool *paccess_permission);

void uvmem_write_map(uint32_t, uint32_t);

void vm(bool write, int vaddr, uint32_t *pv);

static inline void
vmRead(uint32_t vaddr, uint32_t *pv)
{
#ifndef DISABLE_TRACE_MEMORY
	trace_memory_location(false, vaddr, pv, lc);
#endif
	vm(false, vaddr, pv);
}

static inline void
vmWrite(uint32_t vaddr, uint32_t v)
{
#ifndef DISABLE_TRACE_MEMORY
	trace_memory_location(true, vaddr, &v, lc);
#endif
	vm(true, vaddr, &v);
}
