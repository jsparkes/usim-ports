#pragma once

#include <stdbool.h>
#include <stdint.h>

extern bool colortv_vsync;
extern bool colortv_hsync;

extern uint32_t colortv_screen_buffer[];
extern uint32_t colortv_color_map[];
extern const uint32_t colortv_width;
extern const uint32_t colortv_height;

void colortv_init(void);
void colortv_quit(void);
void colortv_screen_read(uint32_t, uint32_t *);
void colortv_screen_write(uint32_t, uint32_t);
void colortv_control_read(uint32_t, uint32_t *);
void colortv_control_write(uint32_t, uint32_t);
void colortv_assert_interrupt(void);
void colortv_bus_reset(void);
