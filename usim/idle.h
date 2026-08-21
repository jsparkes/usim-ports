#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>

extern bool idle_enabled;
extern size_t idle_cycles;
extern size_t idle_quantum;
extern size_t idle_timeout;

void idle_init(void);
void idle_quit(void);
void idle_check(uint64_t);
void idle_activity(void);
bool idle_is_idle(void);

#ifdef WITH_SDL3

extern uint64_t idle_count;

#define idle_keyboard_activity() idle_activity()
#define idle_mouse_activity() idle_activity()
#define idle_disk_activity() idle_activity()
#define idle_chaos_activity() idle_activity()

#else

#define idle_keyboard_activity() idle_activity()
#define idle_mouse_activity() idle_activity()
#define idle_disk_activity() //idle_activity_with_source(3)
#define idle_chaos_activity() //idle_activity_with_source(3)
 
#ifdef WITH_X11
void idle_register_fd(int);
#endif

#endif
