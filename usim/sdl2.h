#pragma once

#include <SDL.h>

enum {
	SDL2_SCALE_NEAREST = 0,
	SDL2_SCALE_LINEAR = 1,
};

extern double sdl2_scale;
extern int sdl2_scale_filter;
extern int sdl2_allow_resize;

extern void sdl2_init(void);
extern void sdl2_event(void);

extern void sdl2_beep(int);

extern int sdl2_keysym_to_xk(SDL_KeyboardEvent e);

void sdl2_update_window_title(char *);
