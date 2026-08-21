#pragma once

#include <stdint.h>

extern uint32_t iob_csr;

extern uint16_t the_60_cycle_clock;

void iob_init(void);
void iob_quit(void);
void iob_poll(void);
void iob_unibus_read(uint32_t, uint16_t *);
void iob_unibus_write(uint32_t, uint16_t);
void iob_bus_reset(void);
void iob_prepare_for_warm_boot(void);

#if WITH_SDL3

#include <stdbool.h>

bool iob_is_keyboard_ready_set();
void iob_set_keyboard_ready(uint32_t scancode);
void iob_clear_keyboard_ready();

bool iob_is_mouse_ready_set();
void 
iob_set_mouse_ready
(
 int mouse_x, 
 int mouse_rawx,
 int mouse_y,
 int mouse_rawy,
 int mouse_head,
 int mouse_middle,
 int mouse_tail
);
void iob_clear_mouse_ready();

#endif
