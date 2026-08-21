#pragma once

#include <stdint.h>

extern uint16_t unibus_mapping_registers[16];
extern uint16_t unibus_mapping_buffers[16];

void unibus_mapping_read(uint32_t offset, uint16_t *pv);
void unibus_mapping_write(uint32_t offset, uint16_t v);
