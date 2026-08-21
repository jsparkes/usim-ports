#pragma once

#include <stdbool.h>

#include <SDL3/SDL.h>

// usim:scale configuration option
extern double sdl3_video_scale;
// usim:scale_filter configuration option
extern SDL_ScaleMode sdl3_video_scale_mode;
// usim:allow_resize configuration option
extern bool sdl3_video_allow_resize;

extern SDL_Window *main_window;
extern SDL_WindowID main_window_id;

extern uint32_t* tv_bitmap;

bool sdl3_video_init(void);
void sdl3_video_quit(void);
bool sdl3_video_present(void);

void sdl3_video_set_scalemode(SDL_ScaleMode scalemode);
void sdl3_video_update_window_title(char *window_title);
