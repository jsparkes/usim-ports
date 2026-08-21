/* x11.c --- X11 routines used by the TV and KBD interfaces
 */

#include <err.h>
#include <stdio.h>
#include <stdlib.h>

#include "config.h"
#include "cadet.h"
#include "idle.h"
#include "kbd.h"
#include "misc.h"
#include "knight.h"
#include "machine-control.h"
#include "mouse.h"
#include "tv.h"
#include "ucfg.h"
#include "ucode.h"
#include "utrace.h"
#include "x11.h"

#define EVENT_MASK							\
	ExposureMask |							\
	KeyPressMask | KeyReleaseMask |					\
	PointerMotionMask | ButtonPressMask | ButtonReleaseMask |	\
	EnterWindowMask | LeaveWindowMask

typedef struct DisplayState
{
	unsigned char *data;
	int linesize;
	int depth;
	int width;
	int height;
} DisplayState;

static Display *display;
static Window window;
static int bitmap_order;
static int color_depth;
static Visual *visual;
static GC gc;
static GC idle_gc;
static XImage *ximage;
unsigned long Foreground;
unsigned long Background;

bool x11_grab_keyboard;

/* Wrappers and utlity functions for X11.
 */

int
x11_query_keymap(char k[32])
{
	return XQueryKeymap(display, k);
}

KeyCode
x11_keysym_to_keycode(KeySym keysym)
{
	return XKeysymToKeycode(display, keysym);
}

XModifierKeymap *
x11_get_modifier_mapping(void)
{
	return XGetModifierMapping(display);
}

/*
 * Returns the X11 modifier index of KEYCODE, or -1 if not found.
 */
int
x11_bucky(KeyCode keycode)
{
	XModifierKeymap *modmap;

	modmap = XGetModifierMapping(display);
	for (int modifier = 0; modifier < 8; modifier++) {
		for (int i = 0; i < modmap->max_keypermod; i++) {
			if (keycode == modmap->modifiermap[modifier * modmap->max_keypermod + i]) {
				XFreeModifiermap(modmap);
				return modifier;
			}
		}
	}
	XFreeModifiermap(modmap);
	return -1;
}

/*
 * Check if all keys are up - too expensive?
 */
bool
cadet_allup_key(void)
{
	bool allup;
	int mods;
	int shifts;
	XModifierKeymap *modmap;
	char keymap[32];

	allup = true;
	mods = 0;
	shifts = 0;
	modmap = x11_get_modifier_mapping();
	x11_query_keymap(keymap);
	/*
	 * For each modifier (and in turn, each keycode associated
	 * with that modifier), check and see if it is down.  If that
	 * is the case, clear the set key from KEYMAP.
	 *
	 * Also keep track if we should do an all-up event, and track
	 * the modifiers for later.
	 */
	for (int modifier = 0; modifier < 8; modifier++) {
		int bucky;

		bucky = kbd_modifier_map[modifier];
		for (int i = 0; i < modmap->max_keypermod; i++) {
			KeyCode keycode;

			keycode = modmap->modifiermap[modifier * modmap->max_keypermod + i];
			if (keymap[keycode / 8] & (1 << keycode % 8)) {
				keymap[keycode / 8] &= ~(1 << keycode % 8);	/* Clear the key in KEYMAP. */
				DEBUG(TRACE_KBD, "cadet_allup_key() - bucky pressed (%d); keycode = %d\n", bucky, keycode);
				cadet_press_bucky(bucky, &mods, &shifts);
			}
		}
	}
	XFreeModifiermap(modmap);
	/*
	 * Check if any other key than modifiers (that got cleared
	 * above) are set.  If that is the case, do not generate an
	 * all-up event.
	 */
	for (int i = 0; i < 32; i++) {
		if (keymap[i] != 0) {
			DEBUG(TRACE_KBD, "cadet_allup_key() - found a key that is up which is not a shift; keymap[%d] = 0%o\n", i, keymap[i]);
			allup = false;
			break;
		}
	}
	if (allup == true) {
		DEBUG(TRACE_KBD, "cadet_allup_key() - all-up event; mods = 0%o, shifts = 0%o\n", mods, shifts);
		cadet_shifts = shifts;	/* Keep track of shifts.  */
		cadet_allup_event(mods);	/* Generate all-up event. */
	}
	return allup;
}

/*
 * Takes E, converts it into a LM (hardware) keycode and sends it to
 * the IOB KBD.
 */
static void
process_key(XEvent *e, int keydown)
{
	KeySym keysym;
	KeyCode keycode;
	static XComposeStatus status;
	int bi;
	unsigned char buf[5];

#ifndef DISABLE_IDLE
	idle_keyboard_activity();
#endif

	XLookupString(&e->xkey, (char *) buf, sizeof(buf), &keysym, &status);
	keycode = x11_keysym_to_keycode(keysym);
	if (keycode == NoSymbol || keysym > NELEM(kbd_map)) {
		NOTICE(TRACE_USIM, "kbd (cadet@x11): unable to translate to keycode (keysym = 0%o)\n", keysym);
		return;
	}
	if (kbd_type == 0) {
		if (!keydown)
			return;
		bi = 0;
		if (e->xkey.state & ShiftMask)
			knight_process_bucky(ShiftMapIndex, &bi);
		if (e->xkey.state & LockMask)
			knight_process_bucky(LockMapIndex, &bi);
		if (e->xkey.state & ControlMask)
			knight_process_bucky(ControlMapIndex, &bi);
		if (e->xkey.state & Mod1Mask)
			knight_process_bucky(Mod1MapIndex, &bi);
		if (e->xkey.state & Mod2Mask)
			knight_process_bucky(Mod2MapIndex, &bi);
		if (e->xkey.state & Mod3Mask)
			knight_process_bucky(Mod3MapIndex, &bi);
		if (e->xkey.state & Mod4Mask)
			knight_process_bucky(Mod4MapIndex, &bi);
		if (e->xkey.state & Mod5Mask)
			knight_process_bucky(Mod5MapIndex, &bi);
		knight_process_key(keysym, bi, keydown);
	} else {
		bi = x11_bucky(keycode);
		cadet_process_key(keysym, bi, keydown, &cadet_allup_key);
	}
}

void
x11_beep(void)
{
#if 0
	XKeyboardControl kc;
	XKeyboardControl okc;
	static int onoff = 100;

	onoff = -onoff;

	XGetKeyboardControl(display, &okc);
	kc.key_click_percent = 0;	/* 0 - 100 */
	kc.bell_percent = 100;	/* 0 - 100 */
	kc.bell_pitch = 755;	/* Hz */
	kc.bell_duration = 10;	/* milliseconds */
	XChangeKeyboardControl(display, KBBellPercent | KBBellPitch | KBBellDuration, &kc);
	XBell(display, onoff);	/* display, percent */
	XChangeKeyboardControl(display, KBBellPercent | KBBellPitch | KBBellDuration, &okc);
#endif
}

void
update(int u_minh, int u_minv, int hs, int vs)
{
	XPutImage(display, window, gc, ximage, u_minh, u_minv, u_minh, u_minv, hs, vs);
	XFlush(display);
}

void
x11_event(void)
{
    if (apply_new_window_title)
    {
        x11_update_window_title(window_title);
        apply_new_window_title = false;
    }

	XEvent e;

	tv_update_screen(&update);
	kbd_dequeue_key_event();
	if (is_mouse_warp) {
		is_mouse_warp = 0;
		XWarpPointer(display, None, window, 0, 0, 0, 0, mouse_warp_x, mouse_warp_y);
	}
	while (XCheckWindowEvent(display, window, EVENT_MASK, &e)) {
		switch (e.type) {
		case Expose:
			XPutImage(display, window, gc, ximage, 0, 0, 0, 0, tv_width, tv_height);
			XFlush(display);
			break;
		case KeyPress:
			process_key(&e, 1);
			break;
		case KeyRelease:
			process_key(&e, 0);
			break;
		case MotionNotify:
		case ButtonPress:
		case ButtonRelease:
			mouse_event(e.xbutton.x, e.xbutton.y, e.xbutton.button);
			break;
		case EnterNotify:
			/* Switching between windows, assume all keys up */
			if (kbd_type == 1)
				cadet_allup_key();
			if (x11_grab_keyboard == true)
				XGrabKeyboard(display, window, True, GrabModeAsync, GrabModeAsync, CurrentTime);
			break;
		case LeaveNotify:
			if (x11_grab_keyboard == true)
				XUngrabKeyboard(display, CurrentTime);
			break;
		default:
			break;
		}
	}
}

void
x11_init(void)
{
	char *displayname;
	unsigned long bg_pixel;
	int xscreen;
	Window root;
	XEvent e;
	XGCValues gcvalues;
	XSetWindowAttributes attr;
	XSizeHints *size_hints;
	XTextProperty windowName;
	XTextProperty *pWindowName;
	XTextProperty iconName;
	XTextProperty *pIconName;
	XWMHints *wm_hints;
	char *window_name;
	char *icon_name;

	NOTICE(TRACE_USIM, "tv: using x11 backend for monitor and keyboard\n");
	bg_pixel = 0L;
	pWindowName = &windowName;
	pIconName = &iconName;
	window_name = "usim";
	icon_name = (char *) "CADR";
	displayname = getenv("DISPLAY");
	display = XOpenDisplay(displayname);
	if (display == NULL)
		errx(1, "failed to open display");
	idle_register_fd(XConnectionNumber(display));
	bitmap_order = BitmapBitOrder(display);
	xscreen = DefaultScreen(display);
	color_depth = DisplayPlanes(display, xscreen);
	tv_foreground = WhitePixel(display, xscreen);
	tv_background = BlackPixel(display, xscreen);
	root = RootWindow(display, xscreen);
	attr.event_mask = EVENT_MASK;
	window = XCreateWindow(display, root, 0, 0, tv_width, tv_height, 0, color_depth, InputOutput, visual, CWBorderPixel | CWEventMask, &attr);
	if (window == None)
		errx(1, "failed to open window");
	if (!XStringListToTextProperty(&window_name, 1, pWindowName))
		pWindowName = NULL;
	if (!XStringListToTextProperty(&icon_name, 1, pIconName))
		pIconName = NULL;
	size_hints = XAllocSizeHints();
	if (size_hints != NULL) {
		/*
		 * The window will not be resizable.
		 */
		size_hints->flags = PMinSize | PMaxSize;
		size_hints->min_width = size_hints->max_width = tv_width;
		size_hints->min_height = size_hints->max_height = tv_height;
	}
	wm_hints = XAllocWMHints();
	if (wm_hints != NULL) {
		wm_hints->initial_state = NormalState;
		wm_hints->input = True;
		wm_hints->flags = StateHint | InputHint;
	}
	XSetWMProperties(display, window, pWindowName, pIconName, NULL, 0, size_hints, wm_hints, NULL);
	XMapWindow(display, window);
	if ((window_position_x > 0) && (window_position_y > 0)) {
		NOTICE(TRACE_USIM, 
				"window position is explicitly set to %d,%d\n",
				window_position_x,
				window_position_y);
		XMoveWindow(display, window, window_position_x, window_position_y);
	}
	gc = XCreateGC(display, window, 0, &gcvalues);
	/*
	 * Fill window with the specified background color.
	 */
	bg_pixel = 0;
	XSetForeground(display, gc, bg_pixel);
	XFillRectangle(display, window, gc, 0, 0, tv_width, tv_height);
	/*
	 * Invisible cursor hack.
	 */
	{
		Cursor invisibleCursor;
		Pixmap bitmapNoData;
		XColor black;
		static char noData[] = { 0, 0, 0, 0, 0, 0, 0, 0 };

		black.red = black.green = black.blue = 0;
		bitmapNoData = XCreateBitmapFromData(display, window, noData, 8, 8);
		invisibleCursor = XCreatePixmapCursor(display, bitmapNoData, bitmapNoData, &black, &black, 0, 0);
		XDefineCursor(display, window, invisibleCursor);
		XFreeCursor(display, invisibleCursor);
		XFreePixmap(display, bitmapNoData);
	}
	/*
	 * Wait for first Expose event to do any drawing, then flush.
	 */
	do
		XNextEvent(display, &e);
	while (e.type != Expose || e.xexpose.count);
	XFlush(display);
	ximage = XCreateImage(display, visual, (unsigned) color_depth, ZPixmap, 0, (char *) tv_bitmap, tv_width, tv_height, 32, 0);
	ximage->byte_order = LSBFirst;
	idle_gc = XCreateGC(display, window, 0, 0);
}

void x11_update_window_title(char *window_title)
{
	XTextProperty windowName;
	if (XStringListToTextProperty(&window_title, 1, &windowName) != 0)
	{
		XSetWMName(display, window, &windowName);
	}
}
