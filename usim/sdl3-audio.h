#pragma once

#include <stdbool.h>

// usim:beep_amplitude configuration option
extern double sdl3_audio_beep_amplitude;
// usim:use_ascii_beep configuration option
extern bool sdl3_audio_use_ascii_beep;

bool sdl3_audio_init(void);
void sdl3_audio_quit(void);
void sdl3_audio_beep(int);
