/* cadet.c --- Space Cadet (aka new) keyboard translation
 */

#include <stdint.h>
#include <string.h>
#include <stdbool.h>

#include "iob.h"
#include "kbd.h"
#include "lmch.h"
#include "misc.h"
#include "ucode.h"
#include "utrace.h"

/*
 * Second index in CADET_KBD_MAP, gives which shift must be generated.
 */
#define CADET_IX_UNSHIFT	0
#define CADET_IX_SHIFT		1
#define CADET_IX_TOP		2
#define CADET_IX_GREEK		3

#define CADET_ALLUP_SHIFT	(1 << 0)
#define CADET_ALLUP_GREEK	(1 << 1)
#define CADET_ALLUP_TOP		(1 << 2)
#define CADET_ALLUP_CAPS_LOCK	(1 << 3)
#define CADET_ALLUP_CONTROL	(1 << 4)
#define CADET_ALLUP_META	(1 << 5)
#define CADET_ALLUP_SUPER	(1 << 6)
#define CADET_ALLUP_HYPER	(1 << 7)
#define CADET_ALLUP_ALTLOCK	(1 << 8)
#define CADET_ALLUP_MODELOCK	(1 << 9)
#define CADET_ALLUP_REPEAT	(1 << 10)

#define CADET_LEFT_SHIFT	024
#define CADET_LEFT_GREEK	044
#define CADET_LEFT_TOP		0104
#define CADET_LEFT_CONTROL	020
#define CADET_LEFT_META		045
#define CADET_LEFT_SUPER	05
#define CADET_LEFT_HYPER	0145
#define CADET_RIGHT_SHIFT	025
#define CADET_RIGHT_GREEK	035
#define CADET_RIGHT_TOP		0155
#define CADET_RIGHT_CONTROL	026
#define CADET_RIGHT_META	0165
#define CADET_RIGHT_SUPER	065
#define CADET_RIGHT_HYPER	0175
#define CADET_CAPSLOCK		0125
#define CADET_ALTLOCK		015
#define CADET_MODELOCK		03

static unsigned short cadet_kbd_map[256][2];
static unsigned short cadet_modifier_map[11];

int cadet_shifts = CADET_IX_UNSHIFT;

void
cadet_allup_event(int mods)
{
	int v;

	v = (1 << 15) | (mods & 01777);
	if (iob_csr & (1 << 5))
		kbd_queue_key_event(v);	/* Already something there, queue this. */
	else {
		kbd_scancode = (1 << 16) | v;
		DEBUG(TRACE_KBD, "cadet_allup_event() - kbd_scancode = 0%o\n", kbd_scancode);
		if (iob_csr & (1 << 2)) {
			iob_csr |= 1 << 5;
			assert_unibus_interrupt(0260);
		}
	}
}

static void
cadet_queue_all_keys_up(void)
{
	cadet_shifts = CADET_IX_UNSHIFT;	/* Hmm... */
	kbd_queue_key_event((1 << 15) | 0);
}

void
cadet_press_bucky(int bucky, int *mods, int *shifts)
{
	switch (bucky) {
	case KBD_SHIFT:
		*mods |= CADET_ALLUP_SHIFT;
		*shifts |= (1 << CADET_IX_SHIFT);
		break;
	case KBD_TOP:
		*mods |= CADET_ALLUP_TOP;
		*shifts |= (1 << CADET_IX_TOP);
		break;
	case KBD_CONTROL:
		*mods |= CADET_ALLUP_CONTROL;
		break;
	case KBD_META:
		*mods |= CADET_ALLUP_META;
		break;
	case KBD_SHIFT_LOCK:
		*mods |= CADET_ALLUP_CAPS_LOCK;
		break;
	case KBD_MODE_LOCK:
		*mods |= CADET_ALLUP_MODELOCK;
		break;
	case KBD_GREEK:
		*mods |= CADET_ALLUP_GREEK;
		*shifts |= (1 << CADET_IX_GREEK);
		break;
	case KBD_REPEAT:
		break;
	case KBD_ALT_LOCK:
		*mods |= CADET_ALLUP_ALTLOCK;
		break;
	case KBD_HYPER:
		*mods |= CADET_ALLUP_HYPER;
		break;
	case KBD_SUPER:
		*mods |= CADET_ALLUP_SUPER;
		break;
	default:
		WARNING(TRACE_KBD, "kbd (cadet): unknown bucky key: 0%o\n", bucky);
		break;
	}
}

static void
cadet_process_shift(int scc, int keydown)
{
	int shift;
	DEBUG(TRACE_KBD, "cadet_process_shift(scc = 0%o, keydown = %d)\n", scc, keydown);
	switch (scc) {
	case CADET_LEFT_GREEK: case CADET_RIGHT_GREEK: shift = (1 << CADET_IX_GREEK); break;
	case CADET_LEFT_TOP:   case CADET_RIGHT_TOP:   shift = (1 << CADET_IX_TOP);   break;
	case CADET_LEFT_SHIFT: case CADET_RIGHT_SHIFT: shift = (1 << CADET_IX_SHIFT); break;
	default: return;
	}
	if (keydown) {
		cadet_shifts |= shift;
	} else {
		cadet_shifts &= ~shift;
	}
}

void
cadet_process_key(int keysym,	/* keysym, acts as index into kbd_map */
    int bi,			/* bucky, acts as index into modifier_map */
    int keydown,		/* if key is down or up */
    bool (*allup_key)(void)
	)
{
	int lmchar;		/* Lisp Machine charachter to insert */
	int scc;		/* Space Cadet scancode. */
	int wantshift;		/* Index into second column in cadet_kbd_map. */
	int shkey;		/* Shift key being pressed. */
	int oshift;

	if (!keydown && allup_key() == true)
		return;
	oshift = cadet_shifts;
	DEBUG(TRACE_KBD, "kbd (cadet): keysym = 0%o, bi = %d\n", keysym, bi);
	if (bi != -1) {
		int bucky;

		bucky = kbd_modifier_map[bi];
		if (bucky == KBD_NoSymbol) {
			WARNING(TRACE_KBD, "kbd (cadet): unbound modifier (keysym = 0%o)\n", keysym);
			return;
		}
		scc = cadet_modifier_map[bucky];
		cadet_process_shift(scc, keydown);
		DEBUG(TRACE_KBD, "kbd (cadet): bucky pressed; scc = 0%o, shifts = 0%o (previous: 0%o)\n", scc, cadet_shifts, oshift);
		kbd_event(scc, keydown);
		return;
	}
	lmchar = kbd_map[keysym];
	DEBUG(TRACE_KBD, "kbd (cadet): kbd_map[%d] (lmchar) = 0%o\n", keysym, kbd_map[keysym]);
	if (lmchar > LMCH_CODE_LIMIT || lmchar == LMCH_NoSymbol) {
		NOTICE(TRACE_KBD, "kbd (cadet): unable to translate to lispm key (keysym = 0%o)\n", keysym);
		return;
	}
	scc = cadet_kbd_map[lmchar][0];
	wantshift = cadet_kbd_map[lmchar][1];
	DEBUG(TRACE_KBD, "kbd (cadet): non bucky pressed; scc = 0%o, wantshift = %d\n", scc, wantshift);
	/*
	 * If modifiers correct, just post the event, else
	 * queue the event and post the appropriate shifts.
	 */
	if (((wantshift == CADET_IX_UNSHIFT) && ((cadet_shifts & (1 << CADET_IX_SHIFT)) == 0)) || (cadet_shifts & (1 << wantshift))) {
		kbd_event(scc, keydown);
		return;
	}
	/*
	 * Messy case: The Lisp Machine requires other shifts than we have
	 * pressed.
	 */
	shkey = CADET_IX_UNSHIFT;
	cadet_queue_all_keys_up();	/* All keys up. */
	if (wantshift != CADET_IX_UNSHIFT) {
		/*
		 * Press the right key.
		 */
		if (wantshift == CADET_IX_SHIFT)
			shkey = CADET_LEFT_SHIFT;
		else if (wantshift == CADET_IX_TOP)
			shkey = CADET_LEFT_TOP;
		else
			shkey = CADET_LEFT_GREEK;
		kbd_queue_key_event(shkey);
	}
	/*
	 *  Press/lift the key itself.
	 */
	kbd_queue_key_event(scc | (!keydown) << 8);
	if (shkey)
		kbd_queue_key_event(shkey | 1 << 8);	/* Lift the shift. */
	/*
	 * Re-press the previous keys.
	 */
	if (oshift & (1 << CADET_IX_TOP))
		kbd_queue_key_event(CADET_LEFT_TOP);
	if (oshift & (1 << CADET_IX_SHIFT))
		kbd_queue_key_event(CADET_LEFT_SHIFT);
	if (oshift & (1 << CADET_IX_GREEK))
		kbd_queue_key_event(CADET_LEFT_GREEK);
	cadet_shifts = oshift;
}

void
cadet_init(void)
{
	NOTICE(TRACE_USIM, "kbd: using new (space cadet) keyboard\n");
	/*
	 * Setup mapping from Space Cadet scan-codes to Lisp Machine
	 * characters.
	 */
	cadet_modifier_map[KBD_SHIFT] = CADET_LEFT_SHIFT;
	cadet_modifier_map[KBD_TOP] = CADET_LEFT_TOP;
	cadet_modifier_map[KBD_CONTROL] = CADET_LEFT_CONTROL;
	cadet_modifier_map[KBD_META] = CADET_LEFT_META;
	cadet_modifier_map[KBD_SUPER] = CADET_LEFT_SUPER;
	cadet_modifier_map[KBD_HYPER] = CADET_LEFT_HYPER;
	cadet_modifier_map[KBD_SHIFT_LOCK] = CADET_CAPSLOCK;
#define X(lmch, scc, wantshift)						\
	cadet_kbd_map[LMCH_##lmch][0] = scc; cadet_kbd_map[LMCH_##lmch][1] = wantshift;
#include "cadet.defs"
	/* Hacks... */
#if 0
	X(LMCH_bracketleft, 0166, CADET_IX_UNSHIFT);
	X(LMCH_bracketright, 0146, CADET_IX_UNSHIFT);
	X(LMCH_braceleft, 0132, CADET_IX_SHIFT);
	X(LMCH_braceright, 0137, CADET_IX_SHIFT);
#endif
#undef X
}
