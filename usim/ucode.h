#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/queue.h>

#include "usym.h"

extern uint64_t machine_cycles;

void ucode_init(void);
bool ucode_machrun(void);
void ucode_run(void);
void ucode_load_prom_from_file(char *);

extern int interrupt_status_reg;
extern int interrupt_pending_flag;
extern void set_interrupt_status_reg(int new);

extern void assert_unibus_interrupt(int);
extern void deassert_unibus_interrupt(void);
extern void assert_xbus_interrupt(void);
extern void deassert_xbus_interrupt(void);

/// MARK: uexec.c

extern bool uexec_has_run_once;

extern uint64_t p0;
extern uint32_t p0_pc;
extern bool p0_imem;

extern uint64_t p1;
extern uint32_t p1_pc;
extern bool p1_imem;

extern uint64_t iwr;

extern uint32_t npc;

extern int mdata;
extern int adata;

extern uint64_t debug_ir;

extern bool prom_enabled_flag;
extern uint64_t prom[512];
extern uint64_t imem[16 * 1024];

extern uint32_t amem[1024];
extern uint32_t mmem[32];
extern uint32_t dmem[2048];

extern uint32_t pdl[1024];

extern uint32_t spc[32];
extern uint32_t spcptr;

extern uint32_t pdl_pointer;
extern uint32_t pdl_index;
extern uint32_t vma_reg;
extern uint32_t md_reg;
extern uint32_t lc;
extern uint32_t oa_reg_high;
extern uint32_t oa_reg_low;

extern uint32_t opc;

extern uint32_t out;
extern uint32_t q;

extern bool inhibit;

void uexec_shift_opcs(uint32_t input);
void uexec_step();
