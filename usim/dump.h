#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

extern uint32_t full_trace_lc;
extern uint32_t full_trace_repeat_counter;
extern uint32_t full_trace_last_lc;

extern size_t lc_dump_index;
extern size_t npc_dump_index;

// length is the length of pc history to print
// if length=0, all history is printed
void dump_state(bool verbose, size_t length);

// save state to "/tmp/usim-000-suffix-info.state"
void save_state_to_numbered_file(char *suffix, uint32_t info);

// for save/restore state, if filename=NULL:
// - if usim:state_filename is set, it is used as filename
// - if usim:state_filename is not set, usim-<CHAOS_MYNAME>.state is filename
void save_state(char *filename);
void restore_state(char *filename);

// similar to above, 
// - if usim:screenshot_filename, it is used
// - if not, usim-<CHAOS_MYNAME>.pbm is filename
void save_screenshot(void);

// the functions below are required for saving state
// save pc history
void record_pc_history(uint32_t pc, bool pc_imem);
// save lc history
void record_lc_history(void);
// save pdl history
void trace_pdlptr_pop(int value);
void trace_pdlptr_push(int value);
void trace_pdlidx_read(int value);
void trace_pdlptr_read(int value);
void trace_pdlidx_write(int value);
void trace_pdlptr_write(int value);

// the following functions are for:
// - optional enable full tracing after LC (-f)
// - optional dump at LC (-l)
// - optional dump at (N)PC (-u)
// - optional trace memory (-t)
// they have a performance impact and can be totally disabled in config.h with:
// DISABLE_FULL_TRACING_AFTER_LC, DISABLE_DUMP_AT_LC, 
// DISABLE_DUMP_AT_NPC and DISABLE_TRACE_MEMORY

void check_enable_full_tracing_after_lc(uint32_t lc);

void add_dump_lc(uint32_t lc);
void check_lc_dump(uint32_t lc);

void add_dump_npc(uint32_t npc);
void check_npc_dump(uint32_t npc);

void add_trace_vmem(uint32_t vmem);
void trace_memory_location(bool write, uint32_t vaddr, uint32_t *pv, int lc);

// this is to trace ucode which is normally enabled 
// with level=DEBUG, facility=TRACE_UCODE 
// it has a performance impact and can be totally disabled in config.h with:
// DISABLE_TRACE_UCODE
void trace_ucode(void);
