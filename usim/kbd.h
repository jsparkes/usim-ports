#pragma once

#include <stdint.h>

/*
 * This is an index into knight_modifier_map or cadet_modifier_map,
 * which is used via kbd_modifier_map (which if KBD_NoSymbol means the
 * modifier is unmapped).
 */
#define KBD_NoSymbol    -1
#define KBD_SHIFT	0	/* SHIFT BITS */
#define KBD_TOP		1	/* TOP BITS */
#define KBD_CONTROL	2	/* CONTROL BITS */
#define KBD_META	3	/* META BITS */
#define KBD_SHIFT_LOCK	4	/* SHIFT LOCK / CAPS LOCK ON CADET */
/* The	following do not exsit on the Knight keyboard. */
#define KBD_MODE_LOCK	5
#define KBD_GREEK	6
#define KBD_REPEAT	7
#define KBD_ALT_LOCK	8
#define KBD_HYPER	9
#define KBD_SUPER	10

extern int kbd_type;
extern uint32_t kbd_scancode;

extern int kbd_map[65535];
extern int kbd_modifier_map[8];

extern void kbd_init(void);
extern void kbd_default_map(void);
extern void kbd_event(int, int);

extern void kbd_queue_key_event(int);
extern void kbd_dequeue_key_event(void);

extern void kbd_warm_boot_key(void);

extern int kbd_lmchar(const char *);
