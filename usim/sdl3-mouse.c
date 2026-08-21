/* sdl3_mouse.c --- SDL3 mouse
 */

#include <err.h>
#include <stdbool.h>
#include <stdlib.h>

#include <SDL3/SDL.h>

#include "iob.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

#include "sdl3-mouse.h"
#include "sdl3-video.h"

static int mouse_x = 0;
static int mouse_y = 0;
static int mouse_tail = 0;
static int mouse_middle = 0;
static int mouse_head = 0;

static int a_mouse_x;
static int a_mouse_y;
static int a_mouse_wakeup;
static int a_v_true;
static int a_mouse_cursor_state;

#define DTP_FIX_VAL(x) ((x) & ((1 << 24) - 1))
#define DTP_FIX(x) (DTP_FIX_VAL(x) | 5 << 25)
#define INTERN(x,y) if (sym_find(&sym_mcr, x, &y)) errx(1, "can't find %s in microcode symbols", x)

static void
sdl3_mouse_warp_if_needed()
{
	// tv:mouse-warp changes the cursor state and sets A-MOUSE-X/Y
	
	// defined in sys/ucadrr/uc-parameters.lisp
	// 0 disabled, 1 open, 2 off, 3 on
	enum { Sdisabled = 0, Sopen, Soff, Son };
	static int prevstate = Sdisabled;

	int state = DTP_FIX_VAL(amem[a_mouse_cursor_state]);
	if (state != prevstate && state == Son) {
		int mouse_warp_x = DTP_FIX_VAL(amem[a_mouse_x]);
		int mouse_warp_y = DTP_FIX_VAL(amem[a_mouse_y]);
		SDL_WarpMouseInWindow(main_window, mouse_warp_x, mouse_warp_y);
	}
	prevstate = state;
}

static void
sdl3_mouse_set_mouse_ready()
{
	// not sure if this is correct place to check and apply warp here
	sdl3_mouse_warp_if_needed();

	// when screen is explicitly scaled (sdl3_scale != 1)
	// mouse_x and mouse_y are reported in scaled coordinates
	// but CADR expects unscaled values
	amem[a_mouse_x] = DTP_FIX(((int)(mouse_x / sdl3_video_scale)));
	amem[a_mouse_y] = DTP_FIX(((int)(mouse_y / sdl3_video_scale)));
	amem[a_mouse_wakeup] = amem[a_v_true];
	
	// setting x,y in mouse_x and mouse_y of IOB makes pointer move strangely
	iob_set_mouse_ready(
			0, 0, 
			0, 0, 
			mouse_head, mouse_middle, mouse_tail);
}

bool
sdl3_mouse_init(void)
{
	INTERN("A-MOUSE-X", a_mouse_x);
	INTERN("A-MOUSE-Y", a_mouse_y);
	INTERN("A-MOUSE-WAKEUP", a_mouse_wakeup);
	INTERN("A-V-TRUE", a_v_true);
	INTERN("A-MOUSE-CURSOR-STATE", a_mouse_cursor_state);

	return true;
}

void
sdl3_mouse_quit(void)
{
}

void
sdl3_mouse_motion(SDL_Event *event)
{
	mouse_x = event->motion.x;
	mouse_y = event->motion.y;
	sdl3_mouse_set_mouse_ready();
}

void
sdl3_mouse_button_down(SDL_Event *event)
{
	switch (event->button.button) {
		case 1: mouse_tail = 0; break;
		case 2: mouse_middle = 0; break;
		case 3: mouse_head = 0; break;
	}
	sdl3_mouse_set_mouse_ready();
}

void
sdl3_mouse_button_up(SDL_Event *event)
{
	switch (event->button.button) {
		case 1: mouse_tail = 1; break;
		case 2: mouse_middle = 1; break;
		case 3: mouse_head = 1; break;
	}
	sdl3_mouse_set_mouse_ready();
}
