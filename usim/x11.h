#pragma once

#include <stdbool.h>

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/keysym.h>

extern bool x11_grab_keyboard;

extern void x11_init(void);
extern void x11_event(void);
extern void x11_beep(void);

void x11_update_window_title(char *);
