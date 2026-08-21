#pragma once

#include <stdbool.h>
#include <stdint.h>

#include <SDL3/SDL.h>

// cadet scancode is 7-bit wide
typedef uint8_t Cadet_Scancode;
extern const Cadet_Scancode cadet_scancode_null;

extern SDL_Scancode sdl3_special_key_scancode;

extern const uint32_t cold_boot_scancode;
extern const uint32_t warm_boot_scancode;

void sdl3_keyboard_early_init(void);
bool sdl3_keyboard_init(void);
void sdl3_keyboard_quit(void);

Cadet_Scancode sdl3_keyboard_get_cadet_scancode_from_name(const char*);
bool sdl3_keyboard_map_add(const char*, Cadet_Scancode);

// event handlers
void sdl3_keyboard_key_down(SDL_Event *event);
void sdl3_keyboard_key_up(SDL_Event *event);
