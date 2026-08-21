/* sdl2.c --- SDL2 routines used by the TV and KBD interfaces
 */

#include <err.h>

#include <X11/keysym.h>		// for XK_FOO  meh
#include <X11/X.h>		// for FOOMapIndex  meh

#include <SDL.h>

#include "config.h"
#include "icon.h"
#include "tv.h"
#include "kbd.h"
#include "cadet.h"
#include "knight.h"
#include "mouse.h"
#include "utrace.h"
#include "idle.h"
#include "misc.h"
#include "sdl2.h"
#include "usim.h"

SDL_Window *window;
SDL_Renderer *renderer;
SDL_Texture *texture;

double sdl2_scale = 1.0f;
int sdl2_scale_filter = SDL2_SCALE_LINEAR;
int sdl2_allow_resize = 0;

int
sdl2_bucky(SDL_KeyboardEvent e)
{
	int mod = SDL_GetModState();
	if (mod & KMOD_CTRL && (e.keysym.sym == SDLK_LCTRL || e.keysym.sym == SDLK_RCTRL))
		return ControlMapIndex;
	if (mod & KMOD_SHIFT && (e.keysym.sym == SDLK_LSHIFT || e.keysym.sym == SDLK_RSHIFT))
		return ShiftMapIndex;
	if (mod & KMOD_ALT && (e.keysym.sym == SDLK_LALT || e.keysym.sym == SDLK_RALT))
		return Mod1MapIndex;
//      if (mod & KMOD_NUM) return Mod2MapIndex;
//      if (mod & KMOD_MODE) return Mod3MapIndex;
	if (mod & KMOD_GUI && (e.keysym.sym == SDLK_LGUI || e.keysym.sym == SDLK_RGUI))
		return Mod4MapIndex;
//      if (mod & KMOD_SCROLL) return Mod5MapIndex;
	return -1;
}

int
sdl2_keysym_to_xk(SDL_KeyboardEvent e)
{
	/* *INDENT-OFF* */
	switch (e.keysym.sym) {
	case SDLK_ESCAPE: return XK_Escape;
	case SDLK_F1: return XK_F1;
	case SDLK_F2: return XK_F2;
	case SDLK_F3: return XK_F3;
	case SDLK_F4: return XK_F4;
	case SDLK_F5: return XK_F5;
	case SDLK_F6: return XK_F6;
	case SDLK_F7: return XK_F7;
	case SDLK_F8: return XK_F8;
	case SDLK_F9: return XK_F9;
	case SDLK_F10: return XK_F10;
	case SDLK_F11: return XK_F11;
	case SDLK_F12: return XK_F12;
	case SDLK_F13: return XK_F13;
	case SDLK_F14: return XK_F14;
	case SDLK_F15: return XK_F15;
	case SDLK_PAGEUP: return XK_Page_Up;
	case SDLK_PAGEDOWN: return XK_Page_Down;
	case SDLK_HOME: return XK_Home;
	case SDLK_END: return XK_End;
	case SDLK_PRINTSCREEN: return XK_Print;
	case SDLK_SCROLLLOCK: return XK_Scroll_Lock;
	case SDLK_PAUSE: return XK_Pause;
	case SDLK_LEFT: return XK_Left;
	case SDLK_RIGHT: return XK_Right;
	case SDLK_UP: return XK_Up;
	case SDLK_DOWN: return XK_Down;
	case SDLK_BACKQUOTE: return e.keysym.mod & KMOD_SHIFT ? XK_asciitilde : XK_grave;
	case SDLK_1: return e.keysym.mod & KMOD_SHIFT ? XK_exclam : XK_1;
	case SDLK_2: return e.keysym.mod & KMOD_SHIFT ? XK_at : XK_2;
	case SDLK_3: return e.keysym.mod & KMOD_SHIFT ? XK_numbersign : XK_3;
	case SDLK_4: return e.keysym.mod & KMOD_SHIFT ? XK_dollar : XK_4;
	case SDLK_5: return e.keysym.mod & KMOD_SHIFT ? XK_percent : XK_5;
	case SDLK_6: return e.keysym.mod & KMOD_SHIFT ? XK_asciicircum : XK_6;
	case SDLK_7: return e.keysym.mod & KMOD_SHIFT ? XK_ampersand : XK_7;
	case SDLK_8: return e.keysym.mod & KMOD_SHIFT ? XK_asterisk : XK_8;
	case SDLK_9: return e.keysym.mod & KMOD_SHIFT ? XK_parenleft : XK_9;
	case SDLK_0: return e.keysym.mod & KMOD_SHIFT ? XK_parenright : XK_0;
	case SDLK_MINUS: return e.keysym.mod & KMOD_SHIFT ? XK_underscore : XK_minus;
	case SDLK_EQUALS: return e.keysym.mod & KMOD_SHIFT ? XK_plus : XK_equal;
	case SDLK_TAB: return XK_Tab;
	case SDLK_q: return e.keysym.mod & KMOD_SHIFT ? XK_Q : XK_q;
	case SDLK_w: return e.keysym.mod & KMOD_SHIFT ? XK_W : XK_w;
	case SDLK_e: return e.keysym.mod & KMOD_SHIFT ? XK_E : XK_e;
	case SDLK_r: return e.keysym.mod & KMOD_SHIFT ? XK_R : XK_r;
	case SDLK_t: return e.keysym.mod & KMOD_SHIFT ? XK_T : XK_t;
	case SDLK_y: return e.keysym.mod & KMOD_SHIFT ? XK_Y : XK_y;
	case SDLK_u: return e.keysym.mod & KMOD_SHIFT ? XK_U : XK_u;
	case SDLK_i: return e.keysym.mod & KMOD_SHIFT ? XK_I : XK_i;
	case SDLK_o: return e.keysym.mod & KMOD_SHIFT ? XK_O : XK_o;
	case SDLK_p: return e.keysym.mod & KMOD_SHIFT ? XK_P : XK_p;
	case SDLK_LEFTBRACKET: return e.keysym.mod & KMOD_SHIFT ? XK_braceleft : XK_bracketleft;
	case SDLK_RIGHTBRACKET: return e.keysym.mod & KMOD_SHIFT ? XK_braceright : XK_bracketright;
	case SDLK_BACKSLASH: return e.keysym.mod & KMOD_SHIFT ? XK_bar : XK_backslash;
	case SDLK_BACKSPACE: return XK_BackSpace;
	case SDLK_a: return e.keysym.mod & KMOD_SHIFT ? XK_A : XK_a;
	case SDLK_s: return e.keysym.mod & KMOD_SHIFT ? XK_S : XK_s;
	case SDLK_d: return e.keysym.mod & KMOD_SHIFT ? XK_D : XK_d;
	case SDLK_f: return e.keysym.mod & KMOD_SHIFT ? XK_F : XK_f;
	case SDLK_g: return e.keysym.mod & KMOD_SHIFT ? XK_G : XK_g;
	case SDLK_h: return e.keysym.mod & KMOD_SHIFT ? XK_H : XK_h;
	case SDLK_j: return e.keysym.mod & KMOD_SHIFT ? XK_J : XK_j;
	case SDLK_k: return e.keysym.mod & KMOD_SHIFT ? XK_K : XK_k;
	case SDLK_l: return e.keysym.mod & KMOD_SHIFT ? XK_L : XK_l;
	case SDLK_SEMICOLON: return e.keysym.mod & KMOD_SHIFT ? XK_colon : XK_semicolon;
	case SDLK_QUOTE: return e.keysym.mod & KMOD_SHIFT ? XK_quotedbl : XK_apostrophe;
	case SDLK_RETURN: return XK_Return;
	case SDLK_z: return e.keysym.mod & KMOD_SHIFT ? XK_Z : XK_z;
	case SDLK_x: return e.keysym.mod & KMOD_SHIFT ? XK_X : XK_x;
	case SDLK_c: return e.keysym.mod & KMOD_SHIFT ? XK_C : XK_c;
	case SDLK_v: return e.keysym.mod & KMOD_SHIFT ? XK_V : XK_v;
	case SDLK_b: return e.keysym.mod & KMOD_SHIFT ? XK_B : XK_b;
	case SDLK_n: return e.keysym.mod & KMOD_SHIFT ? XK_N : XK_n;
	case SDLK_m: return e.keysym.mod & KMOD_SHIFT ? XK_M : XK_m;
	case SDLK_COMMA: return e.keysym.mod & KMOD_SHIFT ? XK_less : XK_comma;
	case SDLK_PERIOD: return e.keysym.mod & KMOD_SHIFT ? XK_greater : XK_period;
	case SDLK_SLASH: return e.keysym.mod & KMOD_SHIFT ? XK_question : XK_slash;
	case SDLK_SPACE: return XK_space;
	case SDLK_INSERT: return XK_Insert;
	case SDLK_DELETE: return XK_Delete;

// case SDLK_MODE: return ???;
	case SDLK_LSHIFT: return XK_Shift_L;
	case SDLK_RSHIFT: return XK_Shift_R;
	case SDLK_LCTRL: return XK_Control_L;
	case SDLK_RCTRL: return XK_Control_R;
	case SDLK_CAPSLOCK: return XK_Caps_Lock;
// case ???: return XK_Shift_Lock;

	case SDLK_LGUI: return XK_Meta_L;
	case SDLK_RGUI: return XK_Meta_R;
	case SDLK_LALT: return XK_Alt_L;
	case SDLK_RALT: return XK_Alt_R;
//      case SDLK_LSUPER: return XK_Super_L;
//      case SDLK_RSUPER: return XK_Super_R;
//      case SDLK_LHYPER: return XK_Hyper_L;
//      case SDLK_RHYPER: return XK_Hyper_R;

	default: return XK_VoidSymbol;
	}
	/* *INDENT-ON* */
}

bool
cadet_allup_key(void)
{
	bool allup;
	int mods;
	int shifts;

	int statesize;
	const Uint8 *state;

	allup = true;
	mods = 0;
	shifts = 0;

	state = SDL_GetKeyboardState(&statesize);
	for (int i = 0; allup && i < statesize; i++) {
		int bucky;

		if (state[i] != 1)
			continue;	/* bucky not pressed */
		bucky = -1;
		// This needs to be cleaned up -- translates scancode to bucky xk/index
		switch (SDL_GetKeyFromScancode(i)) {
		case SDLK_LSHIFT: case SDLK_RSHIFT: bucky = kbd_modifier_map[ShiftMapIndex];   break;
		case SDLK_LCTRL:  case SDLK_RCTRL:  bucky = kbd_modifier_map[ControlMapIndex]; break;
		case SDLK_LALT:   case SDLK_RALT:   bucky = kbd_modifier_map[Mod1MapIndex];    break;
		case SDLK_LGUI:   case SDLK_RGUI:   bucky = kbd_modifier_map[Mod4MapIndex];    break;
//              case SDLK_LSUPER: case SDLK_RSUPER: 
//              case SDLK_MODE: bucky = kbd_modifier_map[Mod5MapIndex]; break;
//              case SDLK_MENU: bucky = kbd_modifier_map[KBD_NoSymbol]; break;
//              case SDLK_COMPOSE: bucky = kbd_modifier_map[]; break;
		case SDLK_CAPSLOCK: bucky = kbd_modifier_map[LockMapIndex]; break;
		}
		DEBUG(TRACE_KBD, "cadet_allup_key() - bucky pressed (%d), i = %d\n", bucky, i);
		cadet_press_bucky(bucky, &mods, &shifts);
	}
	if (allup == true) {
		DEBUG(TRACE_KBD, "cadet_allup_key() - all-up event; mods = 0%o, shifts = 0%o\n", mods, shifts);
		cadet_shifts = shifts;
		cadet_allup_event(mods);
	}
	return allup;
}

static void
process_key(SDL_KeyboardEvent e, int keydown)
{
//      KeySym keysym;
//      SDLKey keycode;

	int bi;
	int keysym;

#ifndef DISABLE_IDLE
	idle_keyboard_activity();
#endif

	keysym = sdl2_keysym_to_xk(e);
	if (keysym == XK_VoidSymbol || keysym > (int)NELEM(kbd_map)) {
		NOTICE(TRACE_USIM, "cadet(sdl2): unable to translate to keysym %#o\n", e.keysym.sym);
		return;
	}
	if (kbd_type == 0) {
		if (!keydown)
			return;
		bi = 0;
		int mod = SDL_GetModState();
		if (mod & KMOD_SHIFT)
			knight_process_bucky(ShiftMapIndex, &bi);
		if (mod & KMOD_CAPS)
			knight_process_bucky(LockMapIndex, &bi);
		if (mod & KMOD_CTRL)
			knight_process_bucky(ControlMapIndex, &bi);
//              if (mod & Mod1Mask)
//                      knight_process_bucky(Mod1MapIndex, &bi);
//              if (mod & Mod2Mask)
//                      knight_process_bucky(Mod2MapIndex, &bi);
//              if (mod & Mod3Mask)
//                      knight_process_bucky(Mod3MapIndex, &bi);
//              if (mod & Mod4Mask)
//                      knight_process_bucky(Mod4MapIndex, &bi);
//              if (mod & Mod5Mask)
//                      knight_process_bucky(Mod5MapIndex, &bi);
		knight_process_key(e.keysym.sym, bi, keydown);
	} else {
		bi = sdl2_bucky(e);
		cadet_process_key(keysym /* xk_keysym */ , bi, keydown, &cadet_allup_key);
	}
}

void
update(int u_minh, int u_minv, int hs, int vs)
{
	(void)u_minh;
	(void)u_minv;
	(void)hs;
	(void)vs;
	SDL_UpdateTexture(texture, NULL, tv_bitmap, tv_width * sizeof(Uint32));
	// Flush
	SDL_RenderClear(renderer);
	SDL_RenderCopy(renderer, texture, NULL, NULL);
	SDL_RenderPresent(renderer);
}

void
sdl2_event(void)
{
	SDL_Event ev;

    if (apply_new_window_title)
    {
        sdl2_update_window_title(window_title);
        apply_new_window_title = false;
    }

	tv_update_screen(&update);
	kbd_dequeue_key_event();
	if (is_mouse_warp) {
		is_mouse_warp = 0;
		SDL_WarpMouseInWindow(window, mouse_warp_x, mouse_warp_y);
	}
	while (SDL_PollEvent(&ev)) {
		switch (ev.type) {
		case SDL_WINDOWEVENT:
			if (ev.window.event == SDL_WINDOWEVENT_RESIZED) {
				Sint32 width = ev.window.data1;
				Sint32 height = ev.window.data2;
				float tv_aspect = (float) tv_width / tv_height;
				float aspect = (float) width / height;

				if (tv_aspect < aspect)
					width = height * tv_aspect;
				else
					height = width / tv_aspect;

				SDL_SetWindowSize(window, width, height);
				break;
			}

			SDL_UpdateTexture(texture, NULL, tv_bitmap, tv_width * sizeof(Uint32));
			// flush
			SDL_RenderClear(renderer);
			SDL_RenderCopy(renderer, texture, NULL, NULL);
			SDL_RenderPresent(renderer);
			break;
		case SDL_KEYDOWN:
			process_key(ev.key, 1);
			break;
		case SDL_KEYUP:
			process_key(ev.key, 0);
			break;
		case SDL_MOUSEMOTION:
		case SDL_MOUSEBUTTONDOWN:
		case SDL_MOUSEBUTTONUP:
			mouse_event(ev.button.x, ev.button.y, ev.button.button);
			break;
		case SDL_QUIT:
			exit(0);
			break;
		}
	}
}

float xbeep_volume = 1.0f;
int AMPLITUDE = 28000;
const int SAMPLE_RATE = 44100;

int xbeep_sample_nr = 0;
float xbeep_freq = 0.0f;

void
xbeep_audio_callback(void *user_data, Uint8 *raw_buffer, int bytes)
{
	Sint16 *buffer = (Sint16 *) raw_buffer;
	int length = bytes / 2;	// 2 bytes per sample for AUDIO_S16SYS
	float *freq = (float *) (user_data);
	int amp = AMPLITUDE * xbeep_volume;

	for (int i = 0; i < length; i++, xbeep_sample_nr++) {
		double time = (double) xbeep_sample_nr / (double) SAMPLE_RATE;
		buffer[i] = (Sint16) (amp * sin(2.0f * M_PI * (*freq) * time));	// render sine wave
	}
}

void
xbeep_audio_init(void)
{
	SDL_AudioSpec want;
	SDL_AudioSpec have;

	want.freq = SAMPLE_RATE;
	want.format = AUDIO_S16SYS;
	want.channels = 1;
	want.samples = 2048;
	want.callback = xbeep_audio_callback;
	want.userdata = &xbeep_freq;
	if (SDL_OpenAudio(&want, &have) != 0)
		SDL_LogError(SDL_LOG_CATEGORY_AUDIO, "Failed to open audio: %s", SDL_GetError());
	if (want.format != have.format)
		SDL_LogError(SDL_LOG_CATEGORY_AUDIO, "Failed to get the desired AudioSpec");
	SDL_PauseAudio(1);
}

void
xbeep_audio_close(void)
{
	SDL_CloseAudio();
}

void
xbeep(int halfwavelength, int duration)
{
	xbeep_freq = 1000000.0f / (float) (halfwavelength * 2);
	xbeep_sample_nr = 0;

	SDL_PauseAudio(0);
	SDL_Delay(duration / 1000);
	SDL_PauseAudio(1);
}

void
sdl2_beep(int v)
{
	(void)v;
///XBEEP (MISC-INST-ENTRY %BEEP)
///;;; First argument is half-wavelength, second is duration.  Both are in microseconds.
///;;; M-1 has 2nd argument (duration) which is added to initial time-check
///;;; M-2 contains most recent time check
///;;;     to compute quitting time
///;;; M-C contains 1st argument, the wavelength
///;;; M-4 contains the time at which the next click must be done.
///;;; Note that the 32-bit clock wraps around once an hour, we have to be careful
///;;; to compare clock values in the correct way, namely without overflow checking.
	extern uint32_t mmem[32];
#define DTP_FIX_VAL(x) ((x) & ((1 << 24) - 1))
#define DTP_FIX(x) (DTP_FIX_VAL(x) | 5 << 25)
	int wavelength = DTP_FIX_VAL(mmem[7]);	//M-C M-MEM 7
	int duration = DTP_FIX_VAL(mmem[22]);	//M-1 M-MEM 22
	xbeep(wavelength, duration);
}

void
sdl2_init(void)
{
	NOTICE(TRACE_USIM, "tv: using SDL2 backend for monitor and keyboard\n");

	xbeep_audio_init();
	SDL_CreateWindowAndRenderer(tv_width * sdl2_scale, tv_height * sdl2_scale,
	    		SDL_WINDOW_OPENGL | SDL_WINDOW_ALLOW_HIGHDPI
				| (sdl2_allow_resize
					? SDL_WINDOW_RESIZABLE
					: 0),
			&window, &renderer);

	NOTICE(TRACE_USIM, "tv: SDL2 video driver: %s\n", SDL_GetCurrentVideoDriver());
	SDL_RendererInfo renderer_info;
	if (SDL_GetRendererInfo(renderer, &renderer_info) == 0)
	{
		NOTICE(TRACE_USIM, "tv: SDL2 renderer: %s\n", renderer_info.name);
	}

	SDL_ShowCursor(0);	/* Invisible cursor. */

	if ((window_position_x >= 0) && (window_position_y >= 0)) {
		SDL_SetWindowPosition(window, window_position_x, window_position_y);
		NOTICE(TRACE_USIM, 
				"window position is explicitly set to %d,%d\n",
				window_position_x,
				window_position_y);
	}

	SDL_EnableScreenSaver();

	// Obtain icon. It must be a 32x32 pixel 256-color BMP image. RGB 255,0,255 is used for transparency.
    // use xxd -i to create icon.h
    SDL_RWops* icon_rwops = SDL_RWFromMem(icon_bmp, icon_bmp_len);
    if (icon_rwops != NULL)
    {
        SDL_Surface* icon = SDL_LoadBMP_RW(icon_rwops, 1); 
        if(icon != NULL){
            SDL_SetColorKey(icon, SDL_TRUE, SDL_MapRGB(icon->format, 255, 0, 255));
            SDL_SetWindowIcon(window, icon);
            SDL_FreeSurface(icon);
        }else{
            WARNING(TRACE_USIM, "Failed to load icon\n");
        }
    }
    else
    {
        WARNING(TRACE_USIM, "Failed to create icon rwops\n");
    }
	SDL_SetWindowTitle(window, "usim");

//      SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
//      SDL_RenderClear(renderer);
//      SDL_RenderPresent(renderer);

	char *filter;
	if (sdl2_scale_filter == SDL2_SCALE_LINEAR)
		filter = "linear";
	else if (sdl2_scale_filter == SDL2_SCALE_NEAREST)
		filter = "nearest";
	else
		errx(1, "sdl2_scale_filter must be either SDL2_SCALE_LINEAR or SDL2_SCALE_NEAREST");

	SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, filter);
	SDL_RenderSetLogicalSize(renderer, tv_width, tv_height);

	texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_ARGB8888,
			SDL_TEXTUREACCESS_STREAMING, tv_width, tv_height);
}

void
sdl2_update_window_title(char *window_title)
{
	SDL_SetWindowTitle(window, window_title);
}
