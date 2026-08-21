#pragma once

#include <stdbool.h>

extern int cadet_shifts;

extern void cadet_init(void);
extern void cadet_allup_event(int);
extern void cadet_process_key(int, int, int, bool (*)(void));
extern void cadet_press_bucky(int, int *, int *);
