#pragma once

#include <stdint.h>

bool tape_controller_init(void);
void tape_controller_quit(void);
void tape_controller_bus_reset(void);

void tape_controller_unibus_read(uint32_t paddr, uint16_t *);
void tape_controller_unibus_write(uint32_t paddr, uint16_t);

void tape_controller_set_mts_eof(void);

bool tape_controller_is_mts_eot(void);
void tape_controller_set_mts_eot(void);
void tape_controller_reset_mts_eot(void);

void tape_controller_set_mts_bte(void);