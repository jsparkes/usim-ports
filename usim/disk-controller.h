#pragma once

#include <stdint.h>

// disk controller is implicitly connected to disk_unit[0..7]
#define NUMBER_OF_DISK_UNITS 8

void disk_controller_init(void);
void disk_controller_quit(void);

void disk_controller_poll(void);
void disk_controller_read(uint32_t offset, uint32_t *pv);
void disk_controller_write(uint32_t offset, uint32_t v);
void disk_controller_bus_reset(void);
