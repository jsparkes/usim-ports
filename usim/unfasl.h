#pragma once

#include <stdint.h>

#include "usym.h"

extern char *uinst_desc(uint64_t, symtab_t *);
extern char *disassemble_instruction(uint32_t, uint32_t, uint32_t, uint32_t);
extern char *(*disassemble_object_output_fun)(uint32_t, uint32_t);
