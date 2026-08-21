#pragma once

#include <stdbool.h>
#include <stdio.h>

#include "usym.h"

/*
 * Supported system versions:
 */
#define LISPM_SYSTEM78 7800L
#define LISPM_SYSTEM98 9800L
#define LISPM_SYSTEM99 9900L
#define LISPM_SYSTEM300 9999L	/* Yeah, I know ... */

#define LISPM_SYSTEM LISPM_SYSTEM300

#if LISPM_SYSTEM >= 9800L
#define Q_POINTER_WIDTH 031
#else
#define Q_POINTER_WIDTH 030
#endif

extern char usim_state_filename[];
extern char usim_sys_directory[];
extern char usim_fs_root_directory[];

extern symtab_t sym_mcr;
extern symtab_t sym_prom;

extern char window_title[];
extern bool apply_new_window_title;

extern bool colortv_enabled;
extern bool verbose_dump_state_flag;
extern bool warm_boot_flag;
extern char* warm_boot_filename;
extern bool headless;
extern bool auto_boot;
extern bool auto_power_off;

void update_display_refresh_rate(int);
void usim_update_window_title(void);

char* disassemble_pc(uint32_t pc);
char* disassemble_pc2(uint32_t pc, bool pc_imem);
char* disassemble_inst(uint64_t u);
char* disassemble_inst2(uint64_t u, bool pc_imem);

void usim_enable_print_machine_state(bool);
