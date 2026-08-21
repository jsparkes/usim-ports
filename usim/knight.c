/* knight.c --- Knight (aka old) keyboard */

#include <err.h>
#include <stdio.h>
#include <string.h>

#include "kbd.h"
#include "lmch.h"
#include "utrace.h"

#define KNIGHT_VANILLA		0	/* VANILLA */
#define KNIGHT_SHIFT		0300	/* SHIFT BITS */
#define KNIGHT_TOP		01400	/* TOP BITS */
#define KNIGHT_CONTROL		06000	/* CONTROL BITS */
#define KNIGHT_META		030000	/* META BITS */
#define KNIGHT_SHIFT_LOCK	040000	/* SHIFT LOCK */

enum
{
#define X(n, v) KNIGHT_##n = v,
#include "knight.defs"
#undef X
};

static unsigned short knight_kbd_map[256];
static unsigned short knight_modifier_map[5];

void
knight_process_bucky(int k, int *extra)
{
	switch (k) {
	case KBD_SHIFT:
	case KBD_TOP:
	case KBD_CONTROL:
	case KBD_META:
		*extra |= knight_modifier_map[k];
		break;
	case KBD_SHIFT_LOCK:
		*extra ^= knight_modifier_map[k];
		break;
	default:
		WARNING(TRACE_KBD, "kbd (knight): unknown bucky key: 0%o\n", k);
		break;
	}
}

void
knight_process_key(int keysym, int bi, int keydown)
{
	int kc;			/* Knight scancode. */
	int lmchar;		/* Lisp machine character to insert */

	DEBUG(TRACE_KBD, "knight_process_key() - keysym = 0%o, bi = %d\n", keysym, bi);
	if (bi != -1) {
		DEBUG(TRACE_KBD, "knight_process_key() - bucky short-circuit\n");
		return;
	}
	lmchar = kbd_map[keysym];
	DEBUG(TRACE_KBD, "knight_process_key() - kbd_map[%d] (lmchar) = 0%o\n", keysym, kbd_map[keysym]);
	if (lmchar > LMCH_CODE_LIMIT || lmchar == LMCH_NoSymbol) {
		WARNING(TRACE_KBD, "kbd (knight): unable to translate keycode: 0%o\n", lmchar);
		return;
	}
	kc = knight_kbd_map[lmchar];
	DEBUG(TRACE_KBD, "knight_process_key() - kc = 0%o\n", kc);
	/*
	 * Keep Control and Meta bits, Shift is in the scancode table
	 */
	kc |= bi & ~KNIGHT_SHIFT;
	/*
	 * ... but if Control or Meta, add in Shift.
	 */
	if (bi & (17 << 10))
		kc |= bi;
	kc |= 0xffff0000;
	DEBUG(TRACE_KBD, "knight_process_key() - kbd_event(kc = 0%o, keydown = %d)\n", kc, keydown);
	kbd_event(kc, keydown);
}

void
knight_init(void)
{
	NOTICE(TRACE_USIM, "kbd (knight): initializing keyboard\n");
	/*
	 * Setup mapping from Knight scan-codes to Lisp Machine
	 * characters.
	 */
	knight_modifier_map[KBD_SHIFT] = KNIGHT_SHIFT;
	knight_modifier_map[KBD_TOP] = KNIGHT_TOP;
	knight_modifier_map[KBD_CONTROL] = KNIGHT_CONTROL;
	knight_modifier_map[KBD_META] = KNIGHT_META;
	knight_modifier_map[KBD_SHIFT_LOCK] = KNIGHT_SHIFT_LOCK;
#define X(n, ign0) knight_kbd_map[LMCH_##n] = KNIGHT_##n;
#include "knight.defs"
#undef X
}
