#pragma once

#include <stdbool.h>

bool sdl3_mouse_init(void);
void sdl3_mouse_quit(void);

// event handlers
void sdl3_mouse_motion(SDL_Event *event);
void sdl3_mouse_button_down(SDL_Event *event);
void sdl3_mouse_button_up(SDL_Event *event);
