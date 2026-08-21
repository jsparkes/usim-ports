/* kbd.c --- keyboard handling */

#include <err.h>

#include "iob.h"
#include "kbd.h"
#include "cadet.h"
#include "knight.h"
#include "lmch.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

int kbd_type = 1;		/* Default is "cadet". */
uint32_t kbd_scancode;

#define KEY_QUEUE_LEN 10

static int key_queue[KEY_QUEUE_LEN];
static int key_queue_optr;
static int key_queue_iptr;
static int key_queue_free = KEY_QUEUE_LEN;

/*
 * Translation map for the host keyboard to a corresponding Lisp
 * Machine character or modifier.
 */
int kbd_map[65535];
int kbd_modifier_map[8];

void
kbd_queue_key_event(int ev)
{
	int v;

	v = (1 << 16) | ev;
	if (key_queue_free > 0) {
		DEBUG(TRACE_KBD, "kbd_queue_key_event() - queuing 0%o (queue length before %d)\n", v, KEY_QUEUE_LEN - key_queue_free);
		key_queue_free--;
		key_queue[key_queue_optr] = v;
		key_queue_optr = (key_queue_optr + 1) % KEY_QUEUE_LEN;
	} else {
		WARNING(TRACE_KBD, "kbd_queue_key_event() - iob key queue full!\n");
		if (!(iob_csr & (1 << 5)) && (iob_csr & (1 << 2))) {
			iob_csr |= 1 << 5;
			WARNING(TRACE_KBD, "kbd_queue_key_event() - generating interrupt\n");
			assert_unibus_interrupt(0260);
		}
	}
}

void
kbd_dequeue_key_event(void)
{
	if (iob_csr & (1 << 5))	/* Already something to be read. */
		return;
	if (key_queue_free < KEY_QUEUE_LEN) {
		int v;

		v = key_queue[key_queue_iptr];
		DEBUG(TRACE_KBD, "kbd_dequeue_key_event() - dequeuing 0%o (queue length before %d)\n", v, KEY_QUEUE_LEN - key_queue_free);
		key_queue_iptr = (key_queue_iptr + 1) % KEY_QUEUE_LEN;
		key_queue_free++;
		kbd_scancode = (1 << 16) | v;
		if (iob_csr & (1 << 2)) {	/* Keyboard interrupt enabled? */
			iob_csr |= 1 << 5;
			WARNING(TRACE_KBD, "kbd_dequeue_key_event() - generating interrupt (queue length after %d)\n", KEY_QUEUE_LEN - key_queue_free);
			assert_unibus_interrupt(0260);
		}
	}
}

void
kbd_event(int code, int keydown)
{
	int v;

	DEBUG(TRACE_KBD, "kbd_event(code=%o, keydown=%o)\n", code, keydown);
	v = ((!keydown) << 8) | code;
	if (iob_csr & (1 << 5))
		kbd_queue_key_event(v);	/* Already something there, queue this. */
	else {
		kbd_scancode = (1 << 16) | v;
		DEBUG(TRACE_KBD, "kbd_event() - kbd_scancode = 0%o\n", kbd_scancode);
		if (iob_csr & (1 << 2)) {
			iob_csr |= 1 << 5;
			assert_unibus_interrupt(0260);
		}
	}
}

// kbd_cold_boot_key -- ???

/*
  You hold down all four control and meta keys on your keyboard,
and hit return if you want to use the same virtual memory as now,
or rubout if you want to load a new copy of virtual memory.  Hitting
other keys is undefined.

The incantations used for warm-booting and cold-booting involve holding
down all four control and meta keys simultaneously (the two to the left
of the space bar and the two to the right of the space bar), and striking
RUBOUT for a cold-boot or RETURN for a warm-boot.  This combination
of keys is extremely difficult to type accidentally.

RMS@MIT-AI 01/02/79 05:17:38
To: (BUG LISPM) at MIT-AI
C-M-C-M-digit should load the band for that digit.

;Enter here from the PROM.  Virtual memory is not valid yet.
(LOC 6)
PROM	(JUMP-NOT-EQUAL-XCT-NEXT Q-R A-ZERO PROM)    ;These 2 instructions duplicate the prom
       ((Q-R) ADD Q-R A-MINUS-ONE)
;;; Decide whether to restore virtual memory from saved band on disk, i.e.
;;; whether this is a cold boot or a warm boot.  If the keyboard has input
;;; available, and the character was RETURN (rather than RUBOUT), it's a warm boot.
	(CALL-XCT-NEXT PHYS-MEM-READ)
       ((VMA) (A-CONSTANT 17772045))		;Unibus address 764112 (KBD CSR)
	(JUMP-IF-BIT-CLEAR (BYTE-FIELD 1 5) MD	;If keyboard is not ready,
		COLD-BOOT)			; assume we are supposed to cold-boot
	(CALL-XCT-NEXT PHYS-MEM-READ)
       ((VMA) (A-CONSTANT 17772040))		;Unibus address 764100 (KBD LOW)
	((MD) (BYTE-FIELD 6 0) MD)		;Get keycode
	(JUMP-EQUAL MD (A-CONSTANT 46) COLD-BOOT)	;This is cold-boot if key is RUBOUT
	((MD) (A-CONSTANT 46))			;Standardize mode.  Mostly, set to NORMAL speed
	(CALL-XCT-NEXT PHYS-MEM-WRITE)		;40 is PROM-DISABLE, 2 is NORMAL speed.
       ((VMA) (A-CONSTANT 17773005))		;Unibus 766012
	(JUMP BEG0000)

// ukbd -- cadet prom

;Is request to boot machine if both controls and both metas are held
;down, along with rubout or return.  We have just sent the key-down codes
;for all of those keys.  We now send a boot character, then set a flag preventing
;sending of up-codes until the next down-code.  This gives the machine time
;to load microcode and read the character to see whether
;it is a warm or cold boot, before sending any other characters, such as up-codes.
;  meta		45 / 165
;  control	20 / 26
;  rubout	23 
;  return	136
; The locking keys are in bytes 1, 3, and 12, conveniently out of the way
;A boot code:
;  15-10	1
;  9-6		0
;  5-0		46 (octal) if cold, 62 (octal) if warm.

check-boot
	(mov r1 (/# bootflag))		;Establish addressibility for later
	(mov r0 (/# 64))		;Check one meta key
	(mov a @r0)
	(xrl a (/# 1_5))
	(jnz not-boot)
	(mov r0 (/# 76))		;Check other meta key
	(mov a @r0)
	(xrl a (/# 1_5))
	(jnz not-boot)
	(mov r0 (/# 62))		;Check byte containing controls and rubout
	(mov a @r0)
	(xrl a (/# (+ 1_0 1_6 1_3)))
	(jz cold-boot)			;Both controls and rubout => cold-boot
	(xrl a (/# 1_3))
	(jnz not-boot)
	(mov r0 (/# 73))		;Check for return
	(mov a @r0)
	(xrl a (/# 1_6))
	(jnz not-boot)
warm-boot
	(mov r2 (/# 62_1))
	(jmp send-boot)

cold-boot
	(mov r2 (/# 46_1))
send-boot
	(mov r3 (/# 174_1))		;1's in bits 14-10
	(mov r4 (/# 363))		;Source ID 1 (new keyboard), 1 in bit 15
	(mov @r1 (/# 377))		;Set bootflag
	(jmp send)			;Transmit character and return

not-boot
	(mov @r1 (/# 0))		;Clear bootflag
	(ret)

*/

void
kbd_warm_boot_key(void)		// Is this even needed?
{
#if 0
	// 062 == KNIGHT_cr? -- seems to be a keyboard event and not a "return"!
	kbd_event(050, 0);	/* Send a Return to get the machine booted. */
	iob_csr |= 1 << 5;	/* Set CSR<5>. */
#if 0
	assert_unibus_interrupt(0260);
#endif
#endif
}

/*
 * Takes a string, and returns the equivalent Lisp Machine character
 * code.  Returns LMCH_NoSymbol on error.
 */
int
kbd_lmchar(const char *key)
{
	for (int i = 0; lmchar_map[i].name; i++) {
		if (streq(key, lmchar_map[i].name) == true)
			return lmchar_map[i].lmchar;
	}
	return LMCH_NoSymbol;
}

#include <X11/keysym.h>		// for XK_FOO  meh
#include <X11/X.h>		// for FOOMapIndex  meh

void
kbd_default_map(void)
{
	for (int i = 0; i < (int) NELEM(kbd_map); i++)
		kbd_map[i] = LMCH_NoSymbol;
	for (int i = 0; i < (int) NELEM(kbd_modifier_map); i++)
		kbd_modifier_map[i] = KBD_NoSymbol;

	/* *INDENT-OFF* */
	/*
	 * Initialize keyboard modifiers
	 */

	kbd_modifier_map[ShiftMapIndex] = KBD_SHIFT;     /* Shift */
	kbd_modifier_map[LockMapIndex] = KBD_SHIFT_LOCK; /* Caps Lock */
	kbd_modifier_map[ControlMapIndex] = KBD_CONTROL; /* Control */
	kbd_modifier_map[Mod1MapIndex] = KBD_META;	     /* Alt */
	kbd_modifier_map[Mod2MapIndex] = KBD_NoSymbol;   /* Num Lock */
	kbd_modifier_map[Mod3MapIndex] = KBD_NoSymbol;   /* ??? */
	kbd_modifier_map[Mod4MapIndex] = KBD_TOP;	     /* Super */
	kbd_modifier_map[Mod5MapIndex] = KBD_NoSymbol;   /* AltGR */

	/*
	 * Initialize keyboard mapping.
	 *
	 * We cann't reuse lmch.defs here because the Lisp Machine
	 * charachter set has names and charachters that do not have a
	 * corresponding mapping in X11.
	 */

	/* Function keys */

	/*
	 * LispM "Escape" is actually Terminal, but in Zmacs this is more
	 * useful.
	 */
	kbd_map[XK_Escape] = LMCH_altmode;

	kbd_map[XK_F1] = LMCH_system;
	kbd_map[XK_F2] = LMCH_network;
	kbd_map[XK_F3] = LMCH_status;
	kbd_map[XK_F4] = LMCH_terminal;
	kbd_map[XK_F5] = LMCH_help;
	kbd_map[XK_F6] = LMCH_clear_input;
	kbd_map[XK_F7] = LMCH_page;
	kbd_map[XK_F8] = LMCH_hold_output;
	//kbd_map[XK_F9] = ?;
	kbd_map[XK_F10] = LMCH_abort;
	kbd_map[XK_F11] = LMCH_resume;
	kbd_map[XK_F12] = LMCH_break;

	kbd_map[XK_Page_Up] = LMCH_abort;
	kbd_map[XK_Page_Down] = LMCH_resume;
	kbd_map[XK_Home] = LMCH_break;	/* This is really handy next to the others */
	kbd_map[XK_End] = LMCH_end;

	/* This is natural */
	kbd_map[XK_Left] = LMCH_hand_left;
	kbd_map[XK_Right] = LMCH_hand_right;
	kbd_map[XK_Up] = LMCH_hand_up;
	kbd_map[XK_Down] = LMCH_hand_down;

	/* Unshifted */
	kbd_map[XK_grave] = LMCH_grave;
	kbd_map[XK_1] = LMCH_1;
	kbd_map[XK_2] = LMCH_2;
	kbd_map[XK_3] = LMCH_3;
	kbd_map[XK_4] = LMCH_4;
	kbd_map[XK_5] = LMCH_5;
	kbd_map[XK_6] = LMCH_6;
	kbd_map[XK_7] = LMCH_7;
	kbd_map[XK_8] = LMCH_8;
	kbd_map[XK_9] = LMCH_9;
	kbd_map[XK_0] = LMCH_0;
	kbd_map[XK_minus] = LMCH_minus;
	kbd_map[XK_equal] = LMCH_equal;

	kbd_map[XK_Tab] = LMCH_tab;
	kbd_map[XK_q] = LMCH_q;
	kbd_map[XK_w] = LMCH_w;
	kbd_map[XK_e] = LMCH_e;
	kbd_map[XK_r] = LMCH_r;
	kbd_map[XK_t] = LMCH_t;
	kbd_map[XK_y] = LMCH_y;
	kbd_map[XK_u] = LMCH_u;
	kbd_map[XK_i] = LMCH_i;
	kbd_map[XK_o] = LMCH_o;
	kbd_map[XK_p] = LMCH_p;
	kbd_map[XK_bracketleft] = LMCH_bracketleft;
	kbd_map[XK_bracketright] = LMCH_bracketright;
	kbd_map[XK_backslash] = LMCH_backslash;
	kbd_map[XK_BackSpace] = LMCH_rubout;

	kbd_map[XK_a] = LMCH_a;
	kbd_map[XK_s] = LMCH_s;
	kbd_map[XK_d] = LMCH_d;
	kbd_map[XK_f] = LMCH_f;
	kbd_map[XK_g] = LMCH_g;
	kbd_map[XK_h] = LMCH_h;
	kbd_map[XK_j] = LMCH_j;
	kbd_map[XK_k] = LMCH_k;
	kbd_map[XK_l] = LMCH_l;
	kbd_map[XK_semicolon] = LMCH_semicolon;
	kbd_map[XK_apostrophe] = LMCH_apostrophe;
	kbd_map[XK_Return] = LMCH_cr;

	kbd_map[XK_z] = LMCH_z;
	kbd_map[XK_x] = LMCH_x;
	kbd_map[XK_c] = LMCH_c;
	kbd_map[XK_v] = LMCH_v;
	kbd_map[XK_b] = LMCH_b;
	kbd_map[XK_n] = LMCH_n;
	kbd_map[XK_m] = LMCH_m;
	kbd_map[XK_comma] = LMCH_comma;
	kbd_map[XK_period] = LMCH_period;
	kbd_map[XK_slash] = LMCH_slash;

	/* Shifted */
	kbd_map[XK_asciitilde] = LMCH_asciitilde;
	kbd_map[XK_exclam] = LMCH_exclam;
	kbd_map[XK_at] = LMCH_at;
	kbd_map[XK_numbersign] = LMCH_numbersign;
	kbd_map[XK_dollar] = LMCH_dollar;
	kbd_map[XK_percent] = LMCH_percent;
	kbd_map[XK_asciicircum] = LMCH_asciicircum;
	kbd_map[XK_ampersand] = LMCH_ampersand;
	kbd_map[XK_asterisk] = LMCH_asterisk;
	kbd_map[XK_parenleft] = LMCH_parenleft;
	kbd_map[XK_parenright] = LMCH_parenright;
	kbd_map[XK_underscore] = LMCH_underscore;
	kbd_map[XK_plus] = LMCH_plus;

	kbd_map[XK_Q] = LMCH_Q;
	kbd_map[XK_W] = LMCH_W;
	kbd_map[XK_E] = LMCH_E;
	kbd_map[XK_R] = LMCH_R;
	kbd_map[XK_T] = LMCH_T;
	kbd_map[XK_Y] = LMCH_Y;
	kbd_map[XK_U] = LMCH_U;
	kbd_map[XK_I] = LMCH_I;
	kbd_map[XK_O] = LMCH_O;
	kbd_map[XK_P] = LMCH_P;
	kbd_map[XK_braceleft] = LMCH_braceleft;
	kbd_map[XK_braceright] = LMCH_braceright;
	kbd_map[XK_bar] = LMCH_bar;

	kbd_map[XK_A] = LMCH_A;
	kbd_map[XK_S] = LMCH_S;
	kbd_map[XK_D] = LMCH_D;
	kbd_map[XK_F] = LMCH_F;
	kbd_map[XK_G] = LMCH_G;
	kbd_map[XK_H] = LMCH_H;
	kbd_map[XK_J] = LMCH_J;
	kbd_map[XK_K] = LMCH_K;
	kbd_map[XK_L] = LMCH_L;
	kbd_map[XK_colon] = LMCH_colon;
	kbd_map[XK_quotedbl] = LMCH_quotedbl;

	kbd_map[XK_Z] = LMCH_Z;
	kbd_map[XK_X] = LMCH_X;
	kbd_map[XK_C] = LMCH_C;
	kbd_map[XK_V] = LMCH_V;
	kbd_map[XK_B] = LMCH_B;
	kbd_map[XK_N] = LMCH_N;
	kbd_map[XK_M] = LMCH_M;
	kbd_map[XK_less] = LMCH_less;
	kbd_map[XK_greater] = LMCH_greater;
	kbd_map[XK_question] = LMCH_question;

	kbd_map[XK_space] = LMCH_space;
	/* *INDENT-ON* */
}

void
kbd_init(void)
{
//      kbd_default_map();
	if (kbd_type == 0)
		knight_init();
	else
		cadet_init();
}
