#pragma once

#include <stdbool.h>
#include <stdint.h>

extern int tv_monitor;

extern uint32_t tv_screen_buffer[];
extern uint32_t tv_width;
extern uint32_t tv_height;

extern uint32_t tv_foreground;
extern uint32_t tv_background;

#ifdef WITH_SDL3
#else
extern uint32_t tv_bitmap[];
#endif

// defined here because it is backend independent
extern int window_position_x;
extern int window_position_y;
extern int window_display;
extern bool window_always_on_top;

void tv_init(void);
void tv_quit(void);
void tv_poll(void);
void tv_screen_write(uint32_t, uint32_t);
void tv_screen_read(uint32_t, uint32_t *);
void tv_control_read(uint32_t, uint32_t *);
void tv_control_write(uint32_t, uint32_t);
void tv_save_screenshot(char *);
void tv_update_screen(void (*fn)(int, int, int, int));
void tv_assert_interrupt(void);
void tv_bus_reset(void);
