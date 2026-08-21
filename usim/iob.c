/* iob.c --- CADR I/O board
 */

#include <err.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <sys/time.h>

#include "iob.h"
#include "machine-control.h"
#include "uch11.h"
#include "ucode.h"
#include "utrace.h"

#if WITH_X11

#include "kbd.h"
#include "mouse.h"
#include "x11.h"

#elif WITH_SDL2

#include "kbd.h"
#include "mouse.h"
#include "sdl2.h"

#elif WITH_SDL3

#include "sdl3-audio.h"
#include "sdl3-keyboard.h"
#include "sdl3-mouse.h"

#endif

uint32_t iob_csr;
static uint32_t iob_usec;

uint16_t the_60_cycle_clock = 0;

#ifdef WITH_SDL3
struct itimerval interval_timer;
#endif

static uint32_t
get_us_clock(void)
{
	static struct timeval tv;
	struct timeval tv2;
	uint32_t ds;
	uint32_t du;

	if (tv.tv_sec == 0) {
		gettimeofday(&tv, 0);
		return 0;
	}
	gettimeofday(&tv2, 0);
	if (tv2.tv_usec < tv.tv_usec) {
		tv2.tv_sec--;
		tv2.tv_usec += 1000 * 1000;
	}
	ds = tv2.tv_sec - tv.tv_sec;
	du = tv2.tv_usec - tv.tv_usec;
	return (ds * 1000 * 1000) + du;
}

#if WITH_SDL3

// SDL3 uses a push rather than poll from keyboard and mouse perspective
// iob does not read scancode or mouse state from keyboard and mouse
// but keyboard and mouse sets the scancode and mouse_x, mouse_y iob registers 
// and sets corresponding CSR bit ready
// mouse has no queue, so it always overrides (does not check if CSR<4> is set)
// keyboard has a queue, so it does not override if CSR<5> is set
// if interrupt is enabled for mouse and/or keyboard
// set_ready functions below also asserts the interrupt

static uint32_t iob_scancode;
static uint16_t iob_mouse_x;
static uint16_t iob_mouse_y;

// command status register (CSR) get/set
// if interrupt is enabled, it also generates the interrupt
// https://tumbleweed.nu/r/lm-3/uv/cadr.html#Command_002fStatus-register-_0028CSR_0029

// keyboard ready CSR<5>
bool iob_is_keyboard_ready_set() { return ((iob_csr & (1 << 5)) != 0); }
void iob_set_keyboard_ready(uint32_t scancode) 
{ 
	if (scancode == cold_boot_scancode) 
	{
		NOTICE(TRACE_IOB, "iob: triggering cold reboot\n");
		machine_control_boot();
	}
	else if (scancode == warm_boot_scancode)
	{
		NOTICE(TRACE_IOB, "iob: triggering warm reboot\n");
		machine_control_boot();
	}
	iob_scancode = scancode;
	iob_csr |= (1 << 5); 
	// keyboard interrupt enabled ?
	if ((iob_csr & (1 << 2)) != 0) 
	{
		assert_unibus_interrupt(0260);
	}
}
void iob_clear_keyboard_ready() { iob_csr &= ~(1 << 5); }

// mouse ready CSR<4>
bool iob_is_mouse_ready_set() { return ((iob_csr & (1 << 4)) != 0); }
void 
iob_set_mouse_ready
(
 int mouse_x, 
 int mouse_rawx,
 int mouse_y,
 int mouse_rawy,
 int mouse_head,
 int mouse_middle,
 int mouse_tail
) 
{ 
	iob_mouse_x = (mouse_rawx << 12) | (mouse_rawy << 14) | (mouse_x & 0x7777);
	iob_mouse_y = (mouse_tail << 12) | (mouse_middle << 13) | (mouse_head << 14) | (mouse_y & 0x7777);
	iob_csr |= (1 << 4); 
	// mouse interrupt enabled ?
	if ((iob_csr & (1 << 1)) != 0) 
	{
		assert_unibus_interrupt(0264);
	}
}
void iob_clear_mouse_ready() { iob_csr &= ~(1 << 4); }

#endif

void
iob_unibus_read(uint32_t uaddr, uint16_t *pv)
{
	*pv = 0;		/* For now default to zero. */
	switch (uaddr) 
	{
		// --- KEYBOARD, MOUSE and BEEP ---

#ifdef WITH_SDL3

		case 0764100:
			{
				*pv = iob_scancode & 0177777;
				DEBUG(TRACE_IOB, "iob: kbd low %011o\n", *pv);
				iob_clear_keyboard_ready();
			}
			break;
		case 0764102:
			{
				*pv = (iob_scancode >> 16) & 0177777;
				DEBUG(TRACE_IOB, "iob: kbd high %011o\n", *pv);
				// CSR cleared above when reading 0100
			}
			break;
		case 0764104:
			{
				*pv = iob_mouse_y;
				// this is logged very often, hence DEBUG level
				DEBUG(TRACE_IOB, "iob: mouse y %011o\n", *pv);
				iob_clear_mouse_ready();
			}
			break;
		case 0764106:
			{
				*pv = iob_mouse_x;
				// this is logged very often, hence DEBUG level
				DEBUG(TRACE_IOB, "iob: mouse x %011o\n", *pv);
				// CSR cleared above when reading 0104
			}
			break;

#else

		case 0764100:
			*pv = kbd_scancode & 0177777;
			DEBUG(TRACE_IOB, "iob: kbd low %011o\n", *pv);
			iob_csr &= ~(1 << 5);	/* Clear CSR<5>. */
			break;
		case 0764102:
			*pv = (kbd_scancode >> 16) & 0177777;
			DEBUG(TRACE_IOB, "iob: kbd high %011o\n", *pv);
			iob_csr &= ~(1 << 5);	/* Clear CSR<5>. */
			break;
		case 0764104:
			*pv = (mouse_tail << 12) | (mouse_middle << 13) | (mouse_head << 14) | (mouse_y & 07777);
			// this is logged very often, hence DEBUG level
			DEBUG(TRACE_IOB, "iob: mouse y %011o\n", *pv);
			iob_csr &= ~(1 << 4);	/* Clear CSR<4>. */
			break;
		case 0764106:
			*pv = (mouse_rawx << 12) | (mouse_rawy << 14) | (mouse_x & 07777);
			// this is logged very often, hence DEBUG level
			DEBUG(TRACE_IOB, "iob: mouse x %011o\n", *pv);
			break;

#endif

		case 0764110:
			DEBUG(TRACE_IOB, "iob: beep\n");
			/* 
			 * This is triggered by older code that does a %UNIBUS-READ. 
			 */
			// MMcM: It's the number of microseconds between triggers of the
			//   flip-flop.  That is, half the wavelength.  So the frequency is, I
			//   think, (/ 1e6 (* #o1350 2)).  So I guess 672Hz. And duration is
			//   only .13sec.
#if defined(WITH_X11)
			x11_beep();
#elif defined(WITH_SDL2)
			sdl2_beep(-1);
#elif defined(WITH_SDL3)
			sdl3_audio_beep(-1);
#endif
			break;

			// --- IOB STATUS REGISTER ---

		case 0764112:
			*pv = iob_csr;
			// this is logged very often, hence DEBUG level
			DEBUG(TRACE_IOB, "iob: csr %011o\n", *pv);
			break;

			// --- TIME OF DAY ---

		case 0764120:
			iob_usec = get_us_clock();
			*pv = iob_usec & 0xffff;
			// this is logged very often, hence DEBUG level
			DEBUG(TRACE_IOB, "iob: usec clock low\n");
			break;
		case 0764122:
			*pv = (uint32_t) (iob_usec >> 16);
			// this is logged very often, hence DEBUG level
			DEBUG(TRACE_IOB, "iob: usec clock high\n");
			break;

            // when read, 60 cycle clock is returned
		case 0764124:
			*pv = the_60_cycle_clock;
			DEBUG(TRACE_IOB, "iob: 60 cycle clock\n");
			break;

			// --- GPIO ---
		case 0764126:
			*pv = 0;
			warnx("iob: read from gpio");
			break;

			// --- CHAOS INTERFACE ---
		case 0764140:		/* ch_csr -- command and status register */
			*pv = uch11_get_csr();
			break;
		case 0764142:		/* ch_myaddr -- interface address */
			*pv = uch11_myaddr;
			DEBUG(TRACE_IOB, "iob: chaos read my-number: %o\n", *pv);
			break;
		case 0764144:		/* ch_rbf -- read buffer */
			*pv = uch11_get_rcv_buffer();
			DEBUG(TRACE_IOB, "iob: chaos read rcv buffer %06o\n", *pv);
			break;
		case 0764146:		/* ch_rbc -- read bit counter */
			*pv = uch11_get_bit_count();
			DEBUG(TRACE_IOB, "iob: chaos read bit-count 0%o\n", *pv);
			break;
		case 0764150:		/* ch_nop -- unused */
			warnx("iob: chaos read obsolete unibus address: %06o\n", uaddr);
			break;
		case 0764152:		/* ch_xmt -- initiate transmission */
			*pv = uch11_myaddr;
			DEBUG(TRACE_IOB, "iob: chaos read xmt => %o\n", *pv);
			uch11_xmit_pkt();
			break;
		case 0764154:
		case 0764156:
			warnx("iob: chaos read obsolete unibus address: %06o\n", uaddr);
			break;

			// --- SERIAL INTERFACE ---
		case 0764160:
		case 0764162:
		case 0764164:
		case 0764166:
			warnx("iob: read from serial interface: %06o", uaddr);
			break;

			// --- NOT DOCUMENTED ---
		case 0764170:
		case 0764172:
		case 0764174:
		case 0764176:
			warnx("iob: read from unknown device: %06o", uaddr);
			break;

		default:
			errx(1, "iob: read from unknown uaddr: %06o", uaddr);
	}
}

void
iob_unibus_write(uint32_t uaddr, uint16_t v)
{
	switch (uaddr) 
	{
		case 0764100:
			DEBUG(TRACE_IOB, "iob: kbd low\n");
			break;
		case 0764102:
			DEBUG(TRACE_IOB, "iob: kbd high\n");
			break;
		case 0764104:
			DEBUG(TRACE_IOB, "iob: mouse y\n");
			break;
		case 0764106:
			DEBUG(TRACE_IOB, "iob: mouse x\n");
			break;
		case 0764110:
			DEBUG(TRACE_IOB, "iob: beep\n");
			/* 
			 * Triggered via %BEEP.
			 */
#if WITH_X11
			x11_beep();
#elif WITH_SDL2
			sdl2_beep(v);
#elif WITH_SDL3
			sdl3_audio_beep(v);
#else
			fprintf(stderr, "\a");	/* Beep! */
#endif
			break;
		case 0764112:
			DEBUG(TRACE_IOB, "iob: kbd csr\n");
			iob_csr = (iob_csr & ~017) | (v & 017);
			break;
		case 0764120:
			DEBUG(TRACE_IOB, "iob: usec clock\n");
			break;
		case 0764122:
			DEBUG(TRACE_IOB, "iob: usec clock\n");
			break;
        // when written, interval timer is set
		case 0764124:
            {
#if WITH_SDL3
                INFO(TRACE_IOB, "iob: start interval timer: #o%o\n", v);
                // interval timer is in units of 16 microseconds
                interval_timer.it_value.tv_usec = (16 * v) % 1000000;
                interval_timer.it_value.tv_sec = (16 * v) / 1000000;
                setitimer(ITIMER_REAL, &interval_timer, NULL);
#else
                warnx("interval timer is not supported");
#endif
            }
			break;
		case 0764140:		/* ch_csr -- command and status register */
			DEBUG(TRACE_IOB, "iob: chaos write %011o\n", v);
			uch11_set_csr(v);
			break;
		case 0764142:		/* write buffer */
			DEBUG(TRACE_IOB, "iob: chaos write-buffer write %011o\n", v);
			uch11_put_xmit_buffer(v);
			break;
		case 0764144:		/* ch_rbf -- read buffer */
		case 0764146:		/* ch_rbc -- read bit counter */
		case 0764150:		/* ch_nop -- unused */
		case 0764152:		/* ch_xmt -- initiate transmission */
			break;
		case 0764160:
		case 0764162:
		case 0764164:
		case 0764166:
			DEBUG(TRACE_IOB, "iob: uart write ---!!! %o\n", v);
			break;
		default:
			errx(1, "iob: write from unknown uaddr: %06o", uaddr);
	}
}

#ifdef WITH_SDL3
static void
interval_timer_over(__attribute__((unused)) int signum)
{
    INFO(TRACE_IOB, "iob: interval timer is over\n");
    assert_unibus_interrupt(0274);
}
#endif

void
iob_poll(void)
{
#ifdef WITH_SDL3
#else
	mouse_poll();
#endif
	// there is no kbd_poll; handled by events
	uch11_poll();
}

void
iob_init(void)
{
#ifdef WITH_SDL3
    signal(SIGALRM, &interval_timer_over);
    interval_timer.it_interval.tv_sec = 0;
    interval_timer.it_interval.tv_usec = 0;
#endif

#ifdef WITH_SDL3
#else
	kbd_init();
	mouse_init();
#endif
	uch11_init();
}

void
iob_quit(void)
{
}

void
iob_bus_reset(void)
{
}

void 
iob_prepare_for_warm_boot(void)
{
    // actually anything other than 046 means warm boot
#ifdef WITH_SDL3
    iob_scancode = 062;
#else
    kbd_scancode = 062;
#endif
	iob_csr |= (1 << 5); 
}
