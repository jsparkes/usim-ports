/* ucfg.c --- configuration handling
 */

#include <assert.h>
#include <err.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <limits.h>

#include "disk-controller.h"
#include "disk-unit.h"
#include "idle.h"
#include "lashup-debugger.h"
#include "lmch.h"
#include "main-memory.h"
#include "misc.h"
#include "tv.h"
#include "ucfg.h"
#include "ucode.h"
#include "uch11.h"
#include "utrace.h"
#include "usim.h"

#if defined(WITH_X11)

#include <X11/keysym.h>
#include <X11/X.h>
#include "kbd.h"
#include "x11.h"

#elif defined(WITH_SDL2)

#include <X11/keysym.h>
#include <X11/X.h>
#include "kbd.h"
#include "sdl2.h"

#elif defined(WITH_SDL3)

#include <SDL3/SDL.h>
#include "sdl3-audio.h"
#include "sdl3-keyboard.h"
#include "sdl3-video.h"

#endif

#define INIHEQ(s, n) (streq(s, section) && streq(n, name))

ucfg_t ucfg = {0};

#ifdef WITH_SDL3
#else
int
lmbucky(const char *s)
{
	bool cadetp;
	int mod;

	cadetp = false;
	/* *INDENT-OFF* */
	if (streq(s, "Shift"))          mod = KBD_SHIFT;
	else if (streq(s, "Top"))       mod = KBD_TOP;
	else if (streq(s, "Control"))   mod = KBD_CONTROL;
	else if (streq(s, "Meta"))      mod = KBD_META;
	else if (streq(s, "ShiftLock") || streq(s, "CapsLock"))
		mod = KBD_SHIFT_LOCK;
	else if (streq(s, "ModeLock")) { cadetp = true; mod = KBD_MODE_LOCK; }
	else if (streq(s, "Greek"))    { cadetp = true; mod = KBD_GREEK; }
	else if (streq(s, "Repeat"))   { cadetp = true; mod = KBD_REPEAT; }
	else if (streq(s, "AltLock"))  { cadetp = true; mod = KBD_ALT_LOCK; }
	else if (streq(s, "Hyper"))    { cadetp = true; mod = KBD_HYPER; }
	else if (streq(s, "Super"))    { cadetp = true; mod = KBD_SUPER; }
	else {
		warnx("unknown lisp machine bucky: %s", s);
		mod = KBD_NoSymbol;
	}
	/* *INDENT-ON* */
	if (cadetp == true && kbd_type != 1)
		warn("this key doesn't exist on the Knight (old) keyboard: %s", s);
	return mod;
}
#endif

void
read_integer_value(const char *name, const char *value, size_t *target)
{
	char *ep;
	long lval;

	errno = 0;
	lval = strtol(value, &ep, 10);
	if ((value[0] == '\0' || *ep != '\0')
			|| (errno == ERANGE && (lval == LONG_MAX || lval == LONG_MIN))) {
		warnx("invalid integer value for %s: %s", name, value);
		return;
	}
	*target = lval;
	return;
}

void
read_double_value(const char *name, const char *value, double *target)
{
	char *ep;
	double lval;

	errno = 0;
	lval = strtod(value, &ep);
	if ((value[0] == '\0' || (ep && *ep != '\0')) || (errno == ERANGE)) {
		warnx("invalid double value for %s: %s", name, value);
		return;
	}
	*target = lval;
	return;
}

static struct ucfg_kbd_pair_s*
new_ucfg_kbd_pair(const char* name, const char* value)
{
    assert (name != NULL);
    assert (value != NULL);
    struct ucfg_kbd_pair_s* entry = malloc(sizeof(struct ucfg_kbd_pair_s));
    entry->name = strdup(name);
    entry->value = strdup(value);
    return entry;
}

static void 
free_ucfg_kbd_pair(struct ucfg_kbd_pair_s* entry)
{
    assert (entry != NULL);
    free(entry->name);
    free(entry->value);
    free(entry);
}


// return non-zero on success, zero on error
static int
ucfg_handler(
        __attribute__((unused)) void *user, 
        const char *section, 
        const char *name, 
        const char *value)
{
    // duplicate the value to ucfg
    if (false);
#define X(s, n, default) \
	else if (INIHEQ(#s, #n)) \
    { \
        if (value != NULL) ucfg.s##_##n = strdup(value); \
    }
#include "ucfg.defs"
#undef X

    // value == NULL only happens when ucfg_handler is run explicitly
    // for default values
    // not when usim.ini is being parsed
    if (value == NULL) return 1;

    // update the kbd_modifiers q manually
    if (streq(section, "kbd.modifiers")) 
    {
        printf("inserting kbd.modifiers:%s = %s\n", name, value);
        struct ucfg_kbd_pair_s* item = new_ucfg_kbd_pair(name, value);
        STAILQ_INSERT_TAIL(&ucfg.kbd_modifiers, item, entries);
    }

    // update the kbd q manually
    if (streq(section, "kbd")) 
    {
        printf("inserting kbd:%s = %s\n", name, value);
        struct ucfg_kbd_pair_s* item = new_ucfg_kbd_pair(name, value);
        STAILQ_INSERT_TAIL(&ucfg.kbd, item, entries);
    }
    
    // --- USIM ---

    if (streq(section, "usim"))
    {
        // ucfg.usim_sys_directory is used directly
        // ucfg.usim_fs_root_directory is used directly
        if (streq(name, "fs_root_directory")) strcpy(usim_fs_root_directory, value);
        
        if (streq(name, "monitor")) 
        {
            if (streq(value, "cpt")) tv_monitor = 0;
            else if (streq(value, "other")) tv_monitor = 1;
            else
            {
                warnx("unknown monitor type: %s", value);
                return 1;
            }
        }

        if (streq(name, "geometry"))
        {
            int x, y, display;
            int nc = sscanf(value, "%d %d %d", &x, &y, &display);
            if (nc >= 2) 
            {
                // do not override -g by checking the values first
                if (window_position_x < 0) window_position_x = x;
                if (window_position_y < 0) window_position_y = y;
                if (nc == 3) window_display = display;
            } 
            else 
            {
                warnx("illegal value for geometry: %s", value);
                return 1;
            }
        }

        if (streq(name, "always_on_top"))
        {
            window_always_on_top = streq(value, "true");
        }

        if (streq(name, "headless")) 
        {
            headless = streq(value, "true");
        }

#ifdef WITH_X11
        if (streq(name, "grab_keyboard")) 
        {
            x11_grab_keyboard = streq(value, "true");;
        }
#endif

#if defined(WITH_X11) || defined(WITH_SDL2)
        if (streq(name, "kbd")) 
        {
            if (streq(value, "knight")) kbd_type = 0;
            else if (streq(value, "cadet"))  kbd_type = 1;
            else 
            {
                warnx("unknown keyboard type: %s", value);
                return 1;
            }
        }
#endif

#ifdef WITH_SDL2
        if (streq(name, "scale")) 
        {
            read_double_value(name, value, &sdl2_scale);
        }

        if (streq(name, "allow_resize")) 
        {
            sdl2_allow_resize = streq(value, "true");
        }

        if (streq(name, "scale_filter")) 
        {
            if (streq(value, "nearest"))
                sdl2_scale_filter = SDL2_SCALE_NEAREST;
            else if (streq(value, "linear"))
                sdl2_scale_filter = SDL2_SCALE_LINEAR;
            else 
            {
                warnx("unknown value for scale_filter: %s", value);
                return 1;
            }
        }
#endif


#ifdef WITH_SDL3
        if (streq(name, "scale"))
        {
            read_double_value(name, value, &sdl3_video_scale);
            return 1;
        }

        if (streq(name, "allow_resize")) 
        {
            sdl3_video_allow_resize = streq(value, "true");
        }

        if (streq(name, "scale_filter")) 
        {
            if (streq(value, "nearest")) sdl3_video_scale_mode = SDL_SCALEMODE_NEAREST;
            else if (streq(value, "linear")) sdl3_video_scale_mode = SDL_SCALEMODE_LINEAR;
            else 
            {
                warnx("unknown value for scale_filter: %s", value);
                return 1;
            }
        }
        
        if (streq(name, "beep_amplitude")) 
        {
            read_double_value(name, value, &sdl3_audio_beep_amplitude);

            if ((sdl3_audio_beep_amplitude > 1.0) || (sdl3_audio_beep_amplitude < 0.0)) 
            {
                warnx("invalid value (%lf) for beep_amplitude (should be between 0 and 1), using 0", sdl3_audio_beep_amplitude);
                sdl3_audio_beep_amplitude = 0.0;
            }

            return 1;
        }

        if (streq(name, "use_ascii_beep")) 
        {
            sdl3_audio_use_ascii_beep = streq(value, "true");
        }

        if (streq(name, "special_key")) 
        {
            if (strlen(value) > 0)
            {
                SDL_Scancode scancode = SDL_GetScancodeFromName(value);

                if (scancode == SDL_SCANCODE_UNKNOWN) 
                {
                    warnx("SDL scancode '%s' not found for special_key", value);
                    return 1;
                }

                sdl3_special_key_scancode = scancode;
            }
        }
#endif

    }
	
    // --- UCODE ---
    
    // ucfg.ucode_ vars are used directly
    
    // --- MEMORY ---
    if (streq(section, "memory"))
    {
        if (streq(name, "size")) 
        {
            const int memory_size_in_KW = strtol(value, NULL, 0);
            const int max_memory_size_in_KW = NUMBER_OF_MAX_MAIN_MEMORY_PAGES / 4;
            if (memory_size_in_KW <= 0)
            {
                errx(1, "memory.size (%d) shall be > 0", 
                        memory_size_in_KW);
            }
            else if (memory_size_in_KW > max_memory_size_in_KW)
            {
                errx(1, "memory.size (%d) shall be <= %d",
                        memory_size_in_KW, max_memory_size_in_KW);
            }
            else if ((memory_size_in_KW & 0xF) != 0)
            {
                errx(1, "memory.size (%d) shall be a multiple of 16",
                        memory_size_in_KW);
            }
            else
            {
                // 1 page = 256 words, 1KW = 4 pages
                main_memory_npages = memory_size_in_KW * 4;
            }
        }
    }

    // --- DISK ---

    // disk configuration has changed
    // warn and exit if there is old configuration in usim.ini
    if (INIHEQ("disk", "disk0_filename") ||
        INIHEQ("disk", "disk1_filename") ||
        INIHEQ("disk", "disk2_filename") ||
        INIHEQ("disk", "disk3_filename") ||
        INIHEQ("disk", "disk4_filename") ||
        INIHEQ("disk", "disk5_filename") ||
        INIHEQ("disk", "disk6_filename") ||
        INIHEQ("disk", "disk7_filename"))
    {
        warnx("[disk] configuration in usim.ini has changed.");
        warnx("Instead of using diskN_filename=, use diskN=");
        warnx("Full format is diskN = [DISK_UNIT_TYPE,]DISK_PACK_FILENAME");
        warnx("DISK_UNIT_TYPE can be T-80 or T-300. If it is omitted, T-300 is assumed.");
        exit(1);
    }

    // --- IDLE ---

    if (streq(section, "idle")) 
    {
		if (streq(name, "cycles")) 
        {
            int cycles = strtol(value, NULL, 0);
            if (cycles < 0) idle_enabled = false;
            else idle_cycles = (size_t) cycles;
        } 

		if (streq(name, "quantum")) read_integer_value(name, value, &idle_quantum);

		if (streq(name, "timeout")) read_integer_value(name, value, &idle_timeout);
	}

    // --- CHAOS ---
    
    if (streq(section, "chaos"))
    {
        if (streq(name, "backend")) 
        {
            if (streq(value, "daemon")) uch11_backend = UCH11_BACKEND_DAEMON;
            else if (streq(value, "local")) uch11_backend = UCH11_BACKEND_LOCAL;
            else if (streq(value, "udp")) uch11_backend = UCH11_BACKEND_UDP;
            else if (streq(value, "hybrid")) 
            {
                uch11_backend = UCH11_BACKEND_UDP;
                hybrid_udp_and_local = true;
            }
            else 
            {
                warnx("unknown chaos backend: %s", value);
                return 1;
            }
        }

        if (streq(name, "udp_local_hybrid"))
        {
            hybrid_udp_and_local = streq(value, "true");
        }

    }

    // --- TRACE ---
    
    if (streq(section, "trace"))
    {
        if (streq(name, "level"))
        {
            if (streq(value, "emerg"))          trace_level = LOG_EMERG;
            else if (streq(value, "alert"))     trace_level = LOG_ALERT;
            else if (streq(value, "crit"))      trace_level = LOG_CRIT;
            else if (streq(value, "err"))       trace_level = LOG_ERR;
            else if (streq(value, "warning"))   trace_level = LOG_WARNING;
            else if (streq(value, "notice"))    trace_level = LOG_NOTICE;
            else if (streq(value, "info"))       
            {
#ifdef NDEBUG
                warnx("trace level debug and info is not visible in release builds.");
#endif
                trace_level = LOG_INFO;
            }
            else if (streq(value, "debug"))      
            {
#ifdef NDEBUG
                warnx("trace level debug and info is not visible in release builds.");
#endif
                trace_level = LOG_DEBUG;
            }
            else {
                warnx("unknown trace level: %s", value);
                return 1;
            }
        }

        if (streq(name, "facilities"))
        {
            char *s;
            char *sp;
            s = strdup(value);
            sp = strtok(s, " ");
            while (sp != NULL) 
            {
                if (streq(sp, "all"))               trace_facilities = TRACE_ALL;
                else if (streq(sp, "none"))         trace_facilities = TRACE_NONE;
                else if (streq(sp, "usim"))         trace_facilities |= TRACE_USIM;
                else if (streq(sp, "ucode"))        trace_facilities |= TRACE_UCODE;
                else if (streq(sp, "microcode"))    trace_facilities |= TRACE_MICROCODE;
                else if (streq(sp, "macrocode"))    trace_facilities |= TRACE_MACROCODE;
                else if (streq(sp, "int"))          trace_facilities |= TRACE_INT;
                else if (streq(sp, "vm"))           trace_facilities |= TRACE_VM;
                else if (streq(sp, "unibus"))           trace_facilities |= TRACE_UNIBUS;
                else if (streq(sp, "unibus-mapping"))   trace_facilities |= TRACE_UNIBUS_MAPPING;
                else if (streq(sp, "xbus"))             trace_facilities |= TRACE_XBUS;
                else if (streq(sp, "bus-interface"))    trace_facilities |= TRACE_BUS_INTERFACE;
                else if (streq(sp, "iob"))          trace_facilities |= TRACE_IOB;
                else if (streq(sp, "kbd"))          trace_facilities |= TRACE_KBD;
                else if (streq(sp, "tv"))           trace_facilities |= TRACE_TV;
                else if (streq(sp, "color"))        trace_facilities |= TRACE_COLOR;
                else if (streq(sp, "mouse"))        trace_facilities |= TRACE_MOUSE;
                else if (streq(sp, "disk"))         trace_facilities |= TRACE_DISK;
                else if (streq(sp, "chaos"))        trace_facilities |= TRACE_CHAOS;
                else if (streq(sp, "x11"))          trace_facilities |= TRACE_X11;
                else if (streq(sp, "spy"))          trace_facilities |= TRACE_SPY;
                else if (streq(sp, "lashup"))       trace_facilities |= TRACE_LASHUP;
                else if (streq(sp, "misc"))         trace_facilities |= TRACE_MISC;
                else if (streq(sp, "tape"))         trace_facilities |= TRACE_TAPE;
                else if (streq(sp, "idle"))         trace_facilities |= TRACE_IDLE;
                else 
                {
                    warnx("unknown trace facility: %s", sp);
                    return 1;
                }
                sp = strtok(NULL, " ");
            }
            free(s);
        }
    }

    // --- KBD.MODIFIERS ---

#ifdef WITH_SDL3

	// all mapping for SDL3 is done under kbd including modifiers
			
#else

	if (streq(section, "kbd.modifiers")) 
    {
		/*
		 * ---!!! We don't differentiate between left/right on
		 * ---!!!   the Lisp Machine side.
		 */
		if (streq(value, "")) {
			warnx("value for %s is empty", name);
			return 1;
		}

		if (streq(name, "Shift"))        kbd_modifier_map[ShiftMapIndex]   = lmbucky(value);
		else if (streq(name, "Lock"))    kbd_modifier_map[LockMapIndex]    = lmbucky(value);
		else if (streq(name, "Control")) kbd_modifier_map[ControlMapIndex] = lmbucky(value);
		// should do something better/more intuitive when using SDL?
		else if (streq(name, "Mod1"))    kbd_modifier_map[Mod1MapIndex]    = lmbucky(value);
		else if (streq(name, "Mod2"))    kbd_modifier_map[Mod2MapIndex]    = lmbucky(value);
		else if (streq(name, "Mod3"))    kbd_modifier_map[Mod3MapIndex]    = lmbucky(value);
		else if (streq(name, "Mod4"))    kbd_modifier_map[Mod4MapIndex]    = lmbucky(value);
		else if (streq(name, "Mod5"))    kbd_modifier_map[Mod5MapIndex]    = lmbucky(value);
		else {
			warnx("unknown modifier: %s", name);
			return 1;
		}

	}

#endif

    // --- KBD ---

#ifdef WITH_X11

	if (streq(section, "kbd")) {
		int xk;
		int lmchar;

		xk = XStringToKeysym(name);
		if (xk == NoSymbol) {
			warnx("unknown X11 key name: %s", name);
			return 1;
		}
		lmchar = kbd_lmchar(value);
		if (lmchar == LMCH_NoSymbol) {
			warnx("unknown lisp machine character name: %s", value);
			return 1;
		}
		kbd_map[xk] = lmchar;
	}
#endif

#ifdef WITH_SDL3

	if (streq(section, "kbd")) {
		SDL_Keycode keycode = SDL_GetKeyFromName(name);
		if (keycode == SDLK_UNKNOWN) {
			warnx("SDL key '%s' not found", name);
			return 1;
		}

		const Cadet_Scancode scancode = sdl3_keyboard_get_cadet_scancode_from_name(value);

		if (scancode == cadet_scancode_null) {
			warnx("Cadet key %s not found", value);
			return 0;
		}

		if (sdl3_keyboard_map_add(name, scancode)) 
		{
			warnx("SDL key '%s' mapped to cadet: %s", name, value);
			return 1;
		}
		else
		{
			warnx("SDL keycode for '%s' or its scancode not found", name);
			return 0;
		}
	}
#endif

#ifdef WITH_SDL2
	if (streq(section, "kbd")) {
		SDL_Scancode scode = SDL_GetScancodeFromName(name);
		if (scode == SDL_SCANCODE_UNKNOWN) {
			warnx("unknown SDL key name: %s", name);
			return 1;
		}
        if (scode == SDL_SCANCODE_F12)
        {
            warnx("F12 cannot be used in key bindings");
            return 1;
        }
		SDL_Keycode skey = SDL_GetKeyFromScancode(scode);
		if (skey == 0) {
			// "always fails"? Do we need scancode?
			//warnx("key %s: can't get SDL keycode from scancode %d, trying SDL_GetKeyFromName()", name, scode);
			skey = SDL_GetKeyFromName(name);
			if (skey == SDLK_UNKNOWN) {
				warnx("key %s: can't get SDL keycode from scancode %d, trying %#x", name, scode,
				    SDL_SCANCODE_TO_KEYCODE(scode));
				skey = SDL_SCANCODE_TO_KEYCODE(scode);
			}
		}
		SDL_KeyboardEvent e;
		memset(&e,0,sizeof(e));
		e.keysym.sym = skey;
		e.keysym.scancode = scode;
		int keysym = sdl2_keysym_to_xk(e);
		if (keysym == XK_VoidSymbol) {
			warnx("key %s: can't map SDL scancode %d (keycode %#x) to X11 keysym", name,
			    scode, skey);
			return 1;
		}
		int lmchar = kbd_lmchar(value);
		if (lmchar == LMCH_NoSymbol) {
			warnx("unknown lisp machine character name: %s (when attempting to map %s)", value, name);
			return 1;
		}
		kbd_map[keysym] = lmchar;
#if 1
		warnx("key %s mapped to lispm key %s", name, value);
#else // debug
		warnx("key %s (scancode %d (%s), keycode %#x (%s)) mapped to X11 keysym %#x, lmchar %#o (%s)",
		    name, scode, SDL_GetScancodeName(scode), skey, SDL_GetKeyName(skey), keysym, lmchar, value);
#endif
	}

#endif
	
	return 1;
}

#define BOOL2STR(X) (X ? "true" : "false")

void
ucfg_dump_running_config(FILE* fp)
{
    fprintf(fp, "[usim]\n");

    fprintf(fp, "state_filename = %s\n", usim_state_filename);
    if (strlen(usim_sys_directory) > 0) 
    {
        fprintf(fp, "sys_directory = %s\n", usim_sys_directory);
    }
    if (strlen(usim_fs_root_directory) > 0) 
    {
        fprintf(fp, "fs_root_directory = %s\n", usim_fs_root_directory);
    }
    fprintf(fp, "monitor = %s\n", tv_monitor == 0 ? "cpt" : "other");
    fprintf(fp, "geometry = %d %d %d\n", 
            window_position_x, window_position_y, window_display);
    fprintf(fp, "always_on_top = %s\n", BOOL2STR(window_always_on_top));
    fprintf(fp, "headless = %s\n", BOOL2STR(headless));

#ifdef WITH_X11
    fprintf(fp, "grab_keyboard = %s\n", BOOL2STR(x11_grab_keyboard));
    fprintf(fp, "kbd = %s\n", kbd_type == 0 ? "knight" : "cadet");
    fprintf(fp, "scale = %s\n", ucfg.usim_scale);
    fprintf(fp, "allow_resize = %s\n", ucfg.usim_allow_resize);
    fprintf(fp, "scale_filter = %s\n", ucfg.usim_scale_filter);
    fprintf(fp, "beep_amplitude = %s\n", ucfg.usim_beep_amplitude);
    fprintf(fp, "use_ascii_beep = %s\n", ucfg.usim_use_ascii_beep);
    fprintf(fp, "special_key = %s\n", ucfg.usim_special_key);
#endif

#ifdef WITH_SDL2
    fprintf(fp, "grab_keyboard = %s\n", ucfg.usim_grab_keyboard);
    fprintf(fp, "kbd = %s\n", kbd_type == 0 ? "knight" : "cadet");
    fprintf(fp, "scale = %g\n", sdl2_scale);
    fprintf(fp, "allow_resize = %s\n", BOOL2STR(sdl2_allow_resize));
    fprintf(fp, "scale_filter = %s\n", 
            sdl2_scale_filter == SDL2_SCALE_NEAREST ? "nearest" : "linear");
    fprintf(fp, "beep_amplitude = %s\n", ucfg.usim_beep_amplitude);
    fprintf(fp, "use_ascii_beep = %s\n", ucfg.usim_use_ascii_beep);
    fprintf(fp, "special_key = %s\n", ucfg.usim_special_key);
#endif

#ifdef WITH_SDL3
    fprintf(fp, "grab_keyboard = %s\n", ucfg.usim_grab_keyboard);
    fprintf(fp, "kbd = %s\n", ucfg.usim_kbd);
    fprintf(fp, "scale = %g\n", sdl3_video_scale);
    fprintf(fp, "allow_resize = %s\n", BOOL2STR(sdl3_video_allow_resize));
    fprintf(fp, "scale_filter = %s\n", 
            sdl3_video_scale_mode == SDL_SCALEMODE_NEAREST ? "nearest" : "linear");
    fprintf(fp, "beep_amplitude = %g\n", sdl3_audio_beep_amplitude);
    fprintf(fp, "use_ascii_beep = %s\n", BOOL2STR(sdl3_audio_use_ascii_beep));
    fprintf(fp, "special_key = %s\n", SDL_GetScancodeName(sdl3_special_key_scancode));
#endif

    fprintf(fp, "\n");
    fprintf(fp, "[ucode]\n");

    fprintf(fp, "prommcr_filename = %s\n", ucfg.ucode_prommcr_filename);
    fprintf(fp, "promsym_filename = %s\n", ucfg.ucode_promsym_filename);
    fprintf(fp, "mcrsym_filename = %s\n", ucfg.ucode_mcrsym_filename);

    fprintf(fp, "\n");
    fprintf(fp, "[chaos]\n");

    fprintf(fp, "backend = ");
    switch (uch11_backend)
    {
        case UCH11_BACKEND_DAEMON: 
            fprintf(fp, "daemon\n");
            break;

        case UCH11_BACKEND_LOCAL: 
            fprintf(fp, "local\n");
            break;

        case UCH11_BACKEND_UDP: 
            {
                if (hybrid_udp_and_local)
                {
                    fprintf(fp, "hybrid\n");
                }
                else
                {
                    fprintf(fp, "udp\n");
                }
            }

            break;
        default: 
            errx(1, "unknown chaos backend");
    }
    fprintf(fp, "myname = %s\n", ucfg.chaos_myname);
    fprintf(fp, "servername = %s\n", ucfg.chaos_servername);
    fprintf(fp, "bridgeip = %s\n", ucfg.chaos_bridgeip);
    fprintf(fp, "bridgeport = %s\n", ucfg.chaos_bridgeport);
    fprintf(fp, "bridgeport_local = %s\n", ucfg.chaos_bridgeport_local);
    fprintf(fp, "bridgechaos = %s\n", ucfg.chaos_bridgechaos);
    fprintf(fp, "udp_local_hybrid = %s\n", BOOL2STR(hybrid_udp_and_local));
    fprintf(fp, "hosts = %s\n", ucfg.chaos_hosts);

    fprintf(fp, "\n");
    fprintf(fp, "[disk]\n");
    for (size_t i = 0; i < NUMBER_OF_DISK_UNITS; i++)
    {
        struct disk_unit_s* disk = disk_units + i;
        if (disk->configured)
        {
            fprintf(fp, "disk%zu = %s,%s\n", 
                    i, disk->type->short_name, disk->filename);
        }
    }

    fprintf(fp, "\n");
    fprintf(fp, "[trace]\n");

    fprintf(fp, "level = ");
    switch (trace_level)
    {
        case LOG_ERR: fprintf(fp, "err"); break;
        case LOG_WARNING: fprintf(fp, "warning"); break;
        case LOG_NOTICE: fprintf(fp, "notice"); break;
        case LOG_INFO: fprintf(fp, "info"); break;
        case LOG_DEBUG: fprintf(fp, "debug"); break;
        default: errx(1, "unknown trace level: %d", trace_level);
    }

    fprintf(fp, "\n");
    fprintf(fp, "facilities =");
    if (trace_facilities == TRACE_ALL) fprintf(fp, " all");
    else if (trace_facilities == TRACE_NONE) fprintf(fp, " none");
    else
    {
        if (trace_facilities & TRACE_USIM) fprintf(fp, " usim");
        if (trace_facilities & TRACE_UCODE) fprintf(fp, " ucode");
        if (trace_facilities & TRACE_MICROCODE) fprintf(fp, " microcode");
        if (trace_facilities & TRACE_MACROCODE) fprintf(fp, " macrocode");
        if (trace_facilities & TRACE_INT) fprintf(fp, " int");
        if (trace_facilities & TRACE_VM) fprintf(fp, " vm");
        if (trace_facilities & TRACE_UNIBUS) fprintf(fp, " unibus");
        if (trace_facilities & TRACE_UNIBUS_MAPPING) fprintf(fp, " unibus-mapping");
        if (trace_facilities & TRACE_XBUS) fprintf(fp, " xbus");
        if (trace_facilities & TRACE_BUS_INTERFACE) fprintf(fp, " bus-interface");
        if (trace_facilities & TRACE_IOB) fprintf(fp, " iob");
        if (trace_facilities & TRACE_KBD) fprintf(fp, " kbd");
        if (trace_facilities & TRACE_TV) fprintf(fp, " tv");
        if (trace_facilities & TRACE_COLOR) fprintf(fp, " color");
        if (trace_facilities & TRACE_MOUSE) fprintf(fp, " mouse");
        if (trace_facilities & TRACE_DISK) fprintf(fp, " disk");
        if (trace_facilities & TRACE_CHAOS) fprintf(fp, " chaos");
        if (trace_facilities & TRACE_X11) fprintf(fp, " x11");
        if (trace_facilities & TRACE_SPY) fprintf(fp, " spy");
        if (trace_facilities & TRACE_LASHUP) fprintf(fp, " lashup");
        if (trace_facilities & TRACE_MISC) fprintf(fp, " misc");
        if (trace_facilities & TRACE_IDLE) fprintf(fp, " idle");
    }
    fprintf(fp, "\n");

    fprintf(fp, "\n");
    fprintf(fp, "[idle]\n");
    fprintf(fp, "cycles = %zu\n", idle_cycles);
    fprintf(fp, "quantum = %zu\n", idle_quantum);
    fprintf(fp, "timeout = %zu\n", idle_timeout);

    struct ucfg_kbd_pair_s* item;

    fprintf(fp, "\n");
    fprintf(fp, "[kbd.modifiers]\n");
    STAILQ_FOREACH(item, &ucfg.kbd_modifiers, entries)
    {
        fprintf(fp, "%s = %s\n", item->name, item->value);
    }

    fprintf(fp, "\n");
    fprintf(fp, "[kbd]\n");
    STAILQ_FOREACH(item, &ucfg.kbd, entries)
    {
        fprintf(fp, "%s = %s\n", item->name, item->value);
    }

}

void
ucfg_init(void)
{
    STAILQ_INIT(&ucfg.kbd_modifiers);
    STAILQ_INIT(&ucfg.kbd);

#ifdef WITH_SDL3
	sdl3_keyboard_early_init();
#else
	kbd_default_map();
#endif

    // call ucfg_handler for each possible option 
    // so everything is initialized to defaults
    // ignore deprecated diskX_filename options
#define X(s, n, default) \
    if ((strcmp(#n, "disk0_filename") != 0) && \
        (strcmp(#n, "disk1_filename") != 0) && \
        (strcmp(#n, "disk2_filename") != 0) && \
        (strcmp(#n, "disk3_filename") != 0) && \
        (strcmp(#n, "disk4_filename") != 0) && \
        (strcmp(#n, "disk5_filename") != 0) && \
        (strcmp(#n, "disk6_filename") != 0) && \
        (strcmp(#n, "disk7_filename") != 0)) \
    { \
        ucfg_handler(&ucfg, #s, #n, default); \
    }
#include "ucfg.defs"
#undef X
}

bool
ucfg_load_config_file(char* config_filename)
{
    // now parse config file and call ucfg_handler only for 
    // the options specified in the config file
	int error = ini_parse(config_filename, ucfg_handler, &ucfg);
    if (error > 0)
    {
		warnx("error while loading config file: %s, line: %d\n", 
                config_filename, error);
        return false;
    }
    else if (error < 0)
	{
		warnx("cannot load config file: %s", config_filename);
        return false;
	}

    return true;
}

void
ucfg_quit(void)
{
    // free strduped things in ucfg_t
#define X(s, n, default) free(ucfg.s##_##n);
#include "ucfg.defs"
#undef X

    struct ucfg_kbd_pair_s* entry;

    // free all in kbd q
    entry = STAILQ_FIRST(&ucfg.kbd);
    while (entry != NULL)
    {
        struct ucfg_kbd_pair_s* next = STAILQ_NEXT(entry, entries);
        free_ucfg_kbd_pair(entry);
        entry = next;
    }

    // free all in kbd_modifiers q
    entry = STAILQ_FIRST(&ucfg.kbd_modifiers);
    while (entry != NULL)
    {
        struct ucfg_kbd_pair_s* next = STAILQ_NEXT(entry, entries);
        free_ucfg_kbd_pair(entry);
        entry = next;
    }
}
