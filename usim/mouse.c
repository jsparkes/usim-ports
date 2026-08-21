/* mouse.c --- mouse interface
 */

#include <err.h>
#include <stdio.h>

#include "iob.h"
#include "idle.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

int mouse_y;			/* Current Y coordinate position of the mouse. */
int mouse_rawx;			/* Raw X encoder inputs. */
int mouse_rawy;			/* Raw Y encoder inputs. */
int mouse_x;			/* Current X coordinate position of the mouse. */

int mouse_tail;			/* Tail switch (1=pressed). */
int mouse_middle;		/* Middle switch. */
int mouse_head;			/* Head switch. */

int is_mouse_warp;
int mouse_warp_x;
int mouse_warp_y;

static int a_mouse_x;
static int a_mouse_y;
static int a_mouse_wakeup;
static int a_v_true;
static int a_mouse_cursor_state;

#define DTP_FIX_VAL(x) ((x) & ((1 << 24) - 1))
#define DTP_FIX(x) (DTP_FIX_VAL(x) | 5 << 25)
#define INTERN(x,y) if (sym_find(&sym_mcr, x, &y)) errx(1, "can't find %s in microcode symbols", x)

void
mouse_event(int x, int y, int buttons)
{
	DEBUG(TRACE_MOUSE, "mouse_event(x = 0%o, y = 0%o, buttons = 0%o)\n", x, y, buttons);
	idle_mouse_activity();
	iob_csr |= 1 << 4;
	assert_unibus_interrupt(0264);
	amem[a_mouse_x] = DTP_FIX(x);
	amem[a_mouse_y] = DTP_FIX(y);
	amem[a_mouse_wakeup] = amem[a_v_true];
	if (buttons == 1)
		mouse_tail ^= 1;
	if (buttons == 2)
		mouse_middle ^= 1;
	if (buttons == 3)
		mouse_head ^= 1;
	DEBUG(TRACE_MOUSE, "mouse_event() - mouse = (0%o, 0%o), mouse (raw) = (0%o, 0%o), h/m/t = %d%d%d\n", mouse_x, mouse_y, mouse_rawx, mouse_rawy, mouse_head, mouse_middle, mouse_tail);
}

void
mouse_poll(void)
{
	static int prevstate;
	int state;
	enum
	{ Sdisabled, Sopen, Soff, Son };

	state = DTP_FIX_VAL(amem[a_mouse_cursor_state]);
	if (state != prevstate && state == Son) {
		is_mouse_warp = 1;
		mouse_warp_x = DTP_FIX_VAL(amem[a_mouse_x]);
		mouse_warp_y = DTP_FIX_VAL(amem[a_mouse_y]);
	}
	prevstate = state;
}

void
mouse_init(void)
{
	INTERN("A-MOUSE-X", a_mouse_x);
	INTERN("A-MOUSE-Y", a_mouse_y);
	INTERN("A-MOUSE-WAKEUP", a_mouse_wakeup);
	INTERN("A-V-TRUE", a_v_true);
	INTERN("A-MOUSE-CURSOR-STATE", a_mouse_cursor_state);
}
