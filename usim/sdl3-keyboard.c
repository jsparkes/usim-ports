/* sdl3_keyboard.c --- SDL3 keyboard
 */

#include <stdbool.h>
#include <stdint.h>
#include <time.h>

#include <SDL3/SDL.h>

#include "diagnostic-interface.h"
#include "dump.h"
#include "iob.h"
#include "machine-control.h"
#include "sched.h"
#include "sdl3-keyboard.h"
#include "sdl3-video.h"
#include "ucode.h"
#include "utrace.h"

const uint32_t cold_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 < 10) | 046;
const uint32_t warm_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 < 10) | 062;

SDL_Scancode sdl3_special_key_scancode = SDL_SCANCODE_UNKNOWN;

// keyboard queue is with enqueue and dequeue methods
// if queue size is too small, capacity can be increased
// but it is probably never needed
#define QUEUE_CAPACITY 100
static uint32_t queue[QUEUE_CAPACITY];
// points to element to read when queue_not_empty > 0
static size_t queue_read = 0;
// points to element to write when queue_not_full > 0
static size_t queue_write = 0;
// number of elements in queue
static size_t queue_nelem = 0;
// mutex for en/de-queue ops
static SDL_Mutex *queue_mutex = NULL;
// semaphore for queue processing thread
static SDL_Semaphore *thread_sem = NULL;

inline static void 
sdl3_keyboard_enqueue(uint32_t scancode)
{
	SDL_LockMutex(queue_mutex);
	if (queue_nelem == QUEUE_CAPACITY)
	{
		WARNING(TRACE_KBD, "sdl3: keyboard scancode: %u is missed, queue exhausted\n", scancode);
	}
	else
	{
		queue[queue_write++] = scancode;
		if (queue_write == QUEUE_CAPACITY) queue_write = 0;
		queue_nelem++;
        SDL_SignalSemaphore(thread_sem);
	}
	SDL_UnlockMutex(queue_mutex);
}

inline static uint32_t 
sdl3_keyboard_dequeue()
{
	uint32_t scancode = cadet_scancode_null;
	SDL_LockMutex(queue_mutex);
	if (queue_nelem > 0)
	{
		scancode = queue[queue_read++];
		if (queue_read == QUEUE_CAPACITY) queue_read = 0;
		queue_nelem--;
	} 
	SDL_UnlockMutex(queue_mutex);
	return scancode;
}
// ---

// scancodes are 7 bit, so scancode count cannot be more than 128
// there is no need to change this for anything
#define CADET_SCANCODE_COUNT 128

// cadet_scancode_X = <scan_code_of_X>
// cadet_scancode_a = 0123;
const Cadet_Scancode cadet_scancode_null = 0000;
#define X(K, S) static const Cadet_Scancode cadet_scancode_##K = S;
#include "sdl3-keyboard-cadet-scancodes.defs"
#undef X

// cadet_key_names[cadet_scancode_a] = "a"
// this is initialied in sdl3_keyboard_static_init early initialization method
static const char* cadet_key_names[CADET_SCANCODE_COUNT] = {NULL};

// key mapping is a fixed length array from SDL scancode to cadet scancodes
// initialized to cadet_scancode_null (meaning no mapping)
static Cadet_Scancode key_map[SDL_SCANCODE_COUNT] = {cadet_scancode_null};

// return a cadet_scancode from its name
// or return cadet_scancode_null
Cadet_Scancode
sdl3_keyboard_get_cadet_scancode_from_name(const char* name)
{
	for (size_t i = 0; i < CADET_SCANCODE_COUNT; i++) 
	{
		const char* cadet_key_name = cadet_key_names[i];
		if (cadet_key_name != NULL) {
			if (strcmp(cadet_key_name, name) == 0) return i;
		}
	}

	return cadet_scancode_null;
}

bool
sdl3_keyboard_map_add(const char *sdl_keycode_name, Cadet_Scancode scancode)
{
	const SDL_Keycode sdl_keycode = SDL_GetKeyFromName(sdl_keycode_name);
	if (sdl_keycode == SDLK_UNKNOWN) return false;
	const SDL_Scancode sdl_scancode = SDL_GetScancodeFromKey(sdl_keycode, NULL);
	if (sdl_scancode == SDL_SCANCODE_UNKNOWN) return false;
	key_map[sdl_scancode] = scancode;
	return true;
}

// this is to initialize cadet key names and default mapping
// does not have an SDL3 dependency so it can be called before initializing SDL
// this has to be called before processing kbd mapping in usim.ini
void
sdl3_keyboard_early_init(void)
{
	// initialize key names
	// cadet_key_names[cadet_scancode_a] = "a";
	cadet_key_names[cadet_scancode_null] = "null";
#define X(K, S) cadet_key_names[cadet_scancode_##K] = #K;
#include "sdl3-keyboard-cadet-scancodes.defs"
#undef X

	// initialize default mapping
	// sdl_keyboard_map_add(SDLK_A, "a");
#define X(K, S) if (!sdl3_keyboard_map_add(#K, cadet_scancode_##S)) printf("%s not an SDL key name\n", #K); 
#include "sdl3-keyboard-default-mapping.defs"
#undef X
}

static SDL_Thread* thread = NULL;
static bool quit = false;

static bool sdl3_keyboard_disable_up_codes_for_boot = false;

// caps, alt and mode_lock can be any key
// so their state should be tracked manually
static bool caps_lock_down = false;
static bool alt_lock_down = false;
static bool mode_lock_down = false;

static int
sdl3_keyboard_thread_run(__attribute__((unused)) void *data)
{
	while (!quit)
	{
        // this can be WaitSemaphore without Timeout
        // using Timeout just in case it stucks and prevents quitting
        if (SDL_WaitSemaphoreTimeout(thread_sem, 1000))
        {
            const uint32_t scancode = sdl3_keyboard_dequeue();
            if (scancode != cadet_scancode_null)
            {
                // wait up to 2+4+...+256 ms for iob keyboard to be not busy
                for (int i = 1; i <= 8 && !quit; i++)
                {
                    if (!iob_is_keyboard_ready_set()) break;
                    SDL_Delay(1<<i);
                }
                if (quit) break;
                if (iob_is_keyboard_ready_set())
                {
                    WARNING(TRACE_KBD, "sdl3: keyboard scancode: %u is missed, keyboard is busy exhausted\n", scancode);
                } 
                else
                {
                    iob_set_keyboard_ready(scancode);
                }
            }
        }
	}

	DEBUG(TRACE_USIM, "usim: keyboard thread quitting\n");

	return 0;
}

bool
sdl3_keyboard_init(void)
{
	DEBUG(TRACE_KBD, "sdl3: listing SDL key names\n");
	for (int scancode = 0; scancode < SDL_SCANCODE_COUNT; scancode++) {
		const SDL_Keycode keycode = SDL_GetKeyFromScancode(scancode, SDL_KMOD_NONE, false);
		if (keycode == SDLK_UNKNOWN) continue;
		DEBUG(TRACE_KBD, 
				"sdl3: '%s'\n", 
				SDL_GetScancodeName(scancode),
				SDL_GetKeyName(keycode));
	}
	DEBUG(TRACE_KBD, "sdl3: end of listing SDL key names\n");

	queue_mutex = SDL_CreateMutex();
    thread_sem = SDL_CreateSemaphore(0);

	quit = false;
	thread = SDL_CreateThread(sdl3_keyboard_thread_run, "usim:keyboard", NULL);
	if (thread == NULL) return false;
    SDL_DetachThread(thread);

	return true;
}

void
sdl3_keyboard_quit(void)
{
	quit = true;
    // signal to unblock
    SDL_SignalSemaphore(thread_sem);
	SDL_DestroyMutex(queue_mutex);
}

inline static uint32_t
sdl3_keyboard_new_scancode()
{
	// 24-bit scancode
	uint32_t cadr_scancode = 0;
	// NKB<23:19>		= all 1, reserved
	cadr_scancode |= (0b11111 << 19);
	// NKB<18:16>		= b001 for new keyboard
	cadr_scancode |= (0b001 << 16);

	return cadr_scancode;
}

bool
sdl3_keyboard_are_all_ctrl_and_meta_down_for_boot(const bool *state, int statesize)
{
	int nctrl = 0;
	int nmeta = 0;
	for (int i = 0; i < statesize; i++) 
	{
		if (key_map[i] == cadet_scancode_ctrl) 
		{
			if (!state[i]) return false;
			nctrl++;
		}

		if (key_map[i] == cadet_scancode_meta)
		{
			if (!state[i]) return false;
			nmeta++;
		}
	}
	// do not allow CM, CCM, CMM combinations to work even thought they are the
	// all ctrl and meta keys mapped
	// boot requires at least two C and two M
	if ((nctrl >= 2) && (nmeta >= 2)) return true;
	return false;
}

void 
sdl3_keyboard_key_down(SDL_Event *event)
{
	// do not accept events anymore after quit is signalled
	if (quit) return;

	const SDL_Scancode sdl_scancode = event->key.scancode;

	DEBUG(TRACE_KBD, "kbd: key down: keycode=0x%x/%s/ scancode=0x%x/%s/\n", 
			event->key.key, 
			SDL_GetKeyName(event->key.key),
			sdl_scancode,
			SDL_GetScancodeName(sdl_scancode));

	// new keyboard (cadet) sends (not surprisingly) only one type of up code
	// - up-down code
	// it does not depend on the state of the other keys

	// is this key mapped to cadet ?
	// if there is no mapping, no further processing is needed, simply ignore it
	const Cadet_Scancode cadet_scancode = key_map[sdl_scancode];
	if (cadet_scancode == cadet_scancode_null) 
	{
		DEBUG(TRACE_KBD, "kbd: key not mapped to CADR, ignoring\n");
		return;
	}

	if (cadet_scancode == cadet_scancode_caps_lock) 
	{
		caps_lock_down = !caps_lock_down;
		DEBUG(TRACE_KBD, "kbd: caps-lock state changed to: %d\n", caps_lock_down);
	}
	if (cadet_scancode == cadet_scancode_alt_lock) 
	{
		alt_lock_down = !alt_lock_down;
		DEBUG(TRACE_KBD, "kbd: alt-lock state changed to: %d\n", alt_lock_down);
	}
	if (cadet_scancode == cadet_scancode_mode_lock) 
	{
		mode_lock_down = !mode_lock_down;
		DEBUG(TRACE_KBD, "kbd: mode-lock state changed to: %d\n", mode_lock_down);
	}

	bool send_cold_boot = false;
	bool send_warm_boot = false;

	int statesize;
	const bool *state = SDL_GetKeyboardState(&statesize);

	const bool cmcm = sdl3_keyboard_are_all_ctrl_and_meta_down_for_boot(state, statesize);
	const bool special_key_down = (sdl3_special_key_scancode == SDL_SCANCODE_UNKNOWN ? false : state[sdl3_special_key_scancode]);
	
	// check for boot combinations
	if (cmcm || special_key_down)
	{
		const bool is_rubout = (cadet_scancode == cadet_scancode_rubout);
		const bool is_return = (cadet_scancode == cadet_scancode_return);
		
		if (is_rubout)
		{
			NOTICE(TRACE_USIM, "kbd: cold boot requested\n");
			send_cold_boot = true;
		}
		else if (is_return)
		{
			NOTICE(TRACE_USIM, "kbd: warm boot requested\n");
			send_warm_boot = true;
		}
	}

	// check for machine control combinations
	if (special_key_down) 
	{
        sdl3_keyboard_disable_up_codes_for_boot = true;

		const bool is_1 = (cadet_scancode == cadet_scancode_1);
		const bool is_2 = (cadet_scancode == cadet_scancode_2);
		const bool is_d = (cadet_scancode == cadet_scancode_d);

        if (is_1)
        {
			NOTICE(TRACE_USIM, "kbd: set scalemode to linear\n");
            sdl3_video_set_scalemode(SDL_SCALEMODE_LINEAR);
        }
        else if (is_2)
        {
			NOTICE(TRACE_USIM, "kbd: set scalemode to nearest\n");
            sdl3_video_set_scalemode(SDL_SCALEMODE_NEAREST);
        }
        else if (is_d)
		{
			NOTICE(TRACE_USIM, "kbd: dump\n");
            save_state_to_numbered_file("kbd", 0);
        }

        return;
	}

	uint32_t cadr_scancode = sdl3_keyboard_new_scancode();

	DEBUG(TRACE_KBD, "kbd: sending up-down (down) code\n");

	DEBUG(TRACE_KBD, 
			"kbd: cadet scancode: 0%o %s\n", 
			cadet_scancode,
			cadet_key_names[cadet_scancode]);

	// NKB<15>		= 0 for up-down code
	// NKB<14>		= 0, reserved
	// NKB<13>		= 0 for not a boot
	// NKB<12:9>	= 0, reserved
	// NKB<8>			= 0 if down
	// NKB<7>			= 0, reserved
	// NKB<6:0>		= key code
	cadr_scancode |= (cadet_scancode & 0x7F);

	sdl3_keyboard_enqueue(cadr_scancode);

	if (send_cold_boot)
	{
		sdl3_keyboard_disable_up_codes_for_boot = true;
		sdl3_keyboard_enqueue(cold_boot_scancode);
		INFO(TRACE_USIM, "kbd: cold boot enqueued\n");
	}
	else if (send_warm_boot)
	{
		sdl3_keyboard_disable_up_codes_for_boot = true;
		sdl3_keyboard_enqueue(warm_boot_scancode);
		INFO(TRACE_USIM, "kbd: warm boot enqueued\n");
	}
	else
	{
		sdl3_keyboard_disable_up_codes_for_boot = false;
	}
}

void 
sdl3_keyboard_key_up(SDL_Event *event)
{
	// do not accept events anymore after quit is signalled
	if (quit) return;

	// after boot is signalled from the keyboard, no up code should be sent
	if (sdl3_keyboard_disable_up_codes_for_boot) return;

	const SDL_Scancode sdl_scancode = event->key.scancode;

	DEBUG(TRACE_KBD, "kbd: key up: keycode=0x%x/%s/ scancode=0x%x/%s/\n", 
			event->key.key, 
			SDL_GetKeyName(event->key.key),
			sdl_scancode,
			SDL_GetScancodeName(sdl_scancode));

	// new keyboard (cadet) sends two different up codes which complicates things
	// - up-down code (similar to down)
	// - all-keys-up code
	// all-keys-up code is sent when no non-modifier key is down
	// in other words, when the last non-modifier key is up, all-keys-up is sent
	//                                                       not up-down up code
	// in order to decide which code to send
	// it is required to know which keys are down

	// is this key mapped to cadet ?
	// if there is no mapping, no further processing is needed, simply ignore it
	const Cadet_Scancode cadet_scancode = key_map[sdl_scancode];
	if (cadet_scancode == cadet_scancode_null) {
		DEBUG(TRACE_KBD, "kbd: key not mapped to CADR, ignoring\n");
		return;
	}

	uint32_t cadr_scancode = sdl3_keyboard_new_scancode();
		
	// these are required to decide which code to send
	bool a_non_modifier_key_is_down = false;
	bool a_modifier_key_is_down = false;

	// these are required to set flags in all-keys-up code
	bool repeat = false;
	bool mode_lock = mode_lock_down;
	bool alt_lock = alt_lock_down;
	bool hyper = false;
	bool super = false;
	bool meta = false;
	bool control = false;
	bool caps_lock = caps_lock_down;
	bool top = false;
	bool greek = false;
	bool shift = false;

	// for lock keys, the key up is not important but its actual state
	if (mode_lock || alt_lock || caps_lock) 
	{
		a_modifier_key_is_down = true;
	}

	// lets find which keys are down
	// this has to be done with an exhaustive search
	int statesize;
	const bool *state = SDL_GetKeyboardState(&statesize);
	for (int i = 0; i < statesize; i++) 
	{
		// when a non-modifier key is down, it is for sure an up-down code will be sent
		// so there is no need to continue this search, simply break
		if (a_non_modifier_key_is_down) break;
		// if the key is not down, continue on next one
		if (!state[i]) continue;
		const Cadet_Scancode cadet_scancode = key_map[i];
		// if this key is not mapped to cadet, ignore it 
		if (cadet_scancode != cadet_scancode_null) 
		{
			// if this key is a modifier, we need to specifically know which one
			// if it is a non-modifier, a generic flag is enough
			if (cadet_scancode == cadet_scancode_repeat) 
			{
				a_modifier_key_is_down = true;
				repeat = true; 
			} 
			else if (cadet_scancode == cadet_scancode_mode_lock) 
			{
				// the state of lock keys are tracked manually
			} 
			else if (cadet_scancode == cadet_scancode_alt_lock) 
			{
				// the state of lock keys are tracked manually
			} 
			else if (cadet_scancode == cadet_scancode_hyper) 
			{
				a_modifier_key_is_down = true;
				hyper = true; 
			} 
			else if (cadet_scancode == cadet_scancode_super) 
			{
				a_modifier_key_is_down = true;
				super = true; 
			} 
			else if (cadet_scancode == cadet_scancode_meta) 
			{
				a_modifier_key_is_down = true;
				meta = true; 
			} 
			else if (cadet_scancode == cadet_scancode_ctrl) 
			{
				a_modifier_key_is_down = true;
				control = true; 
			} 
			else if (cadet_scancode == cadet_scancode_caps_lock) 
			{
				// the state of lock keys are tracked manually
			} 
			else if (cadet_scancode == cadet_scancode_top) 
			{
				a_modifier_key_is_down = true;
				top = true; 
			} 
			else if (cadet_scancode == cadet_scancode_greek) 
			{
				a_modifier_key_is_down = true;
				greek = true; 
			} 
			else if (cadet_scancode == cadet_scancode_shift) 
			{
				a_modifier_key_is_down = true;
				shift = true; 
			} 
			else 
			{
				a_non_modifier_key_is_down = true;
			}
		}
	}

	bool send_up_down_code = false;

	if (a_non_modifier_key_is_down) 
	{
		send_up_down_code = true;
	} 
	else if (a_modifier_key_is_down) 
	{
		send_up_down_code = false;
	} 
	else 
	{
		send_up_down_code = true;
	}

	if (send_up_down_code) 
	{
		DEBUG(TRACE_KBD, "kbd: sending up-down (up) code\n");

		DEBUG(TRACE_KBD, 
				"kbd: cadet scancode: 0%o %s\n", 
				cadet_scancode,
				cadet_key_names[cadet_scancode]);

		// NKB<15>		= 0 when up-down code
		// NKB<14>		= 0, reserved
		// NKB<13>		= 0 when not a boot
		// NKB<12:9>	= 0, reserved
		// NKB<8>			= 1 if up
		cadr_scancode |= (1 << 8);
		// NKB<7>			= 0, reserved
		// NKB<6:0>		= key code
		cadr_scancode |= (cadet_scancode & 0x7F);
	} 
	else 
	{
		DEBUG(TRACE_KBD, "kbd: sending all-keys-up code\n");
		
		// NKB<15>		= 1 when not an up-down code
		cadr_scancode |= (1 << 15);
		// NKB<14>		= 0, reserved
		// NKB<13>		= 0 when not a boot
		// NKB<12:11>	= 0, reserved
		// NKB<10>	  = repeat
		if (repeat)			cadr_scancode |= (1 << 10);
		// NKB<9>			= mode_lock
		if (mode_lock)	cadr_scancode |= (1 << 9);
		// NKB<8>			= alt_lock
		if (alt_lock)		cadr_scancode |= (1 << 8);
		// NKB<7>			= hyper (either one)
		if (hyper)			cadr_scancode |= (1 << 7);
		// NKB<6>			= super (either one)
		if (super)			cadr_scancode |= (1 << 6);
		// NKB<5>			= meta (either one)
		if (meta)				cadr_scancode |= (1 << 5);
		// NKB<4>			= control (either one)
		if (control)		cadr_scancode |= (1 << 4);
		// NKB<3>			= caps_lock
		if (caps_lock)	cadr_scancode |= (1 << 3);
		// NKB<2>			= top (either one)
		if (top)				cadr_scancode |= (1 << 2);
		// NKB<1>			= greek (either one)
		if (greek)			cadr_scancode |= (1 << 1);
		// NKB<0>			= shift (either one)
		if (shift)			cadr_scancode |= (1 << 0);
	}

	sdl3_keyboard_enqueue(cadr_scancode);
}
