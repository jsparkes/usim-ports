/* tv.c --- TV interface
 */

#include <err.h>
#include <string.h>

#include "bus-interface.h"
#include "tv.h"
#include "ucode.h"
#include "utrace.h"

#if WITH_X11
#include "x11.h"
#elif WITH_SDL2
#include "sdl2.h"
#endif

// usim.ini option
int tv_monitor;

// sch: nxbctl, synmod
// the most important register is at offset 0 called tv_mode below
// window/shwarm.lisp:BLACK-ON-WHITE and WHITE-ON-BLOCK
// read write
// MODE<0> = CLOCK MODE 0, used in sync prom addressing
// MODE<1> = CLOCK MODE 1, used in sync prom addressing
// MODE<2> = MODE BOW, if 1, draw black on white, one as black and zero as white, otherwise reverse
// see window/cold.lisp
// MODE<3> = interrupt enable (MODE INTR ENB)
// MODE<4> = VERT FLAG, if MODE INTR ENB=1 and VERT FLAG=1, send interrupt, VERT probably means VERTICAL SYNC (VSYNC)
// read only
// MODE<5> = VSYNC
// MODE<6> = HSYNC
// MODE<7> = SYNC PROM ENB
//
// CLOCK MODE = 00, 64 MHz, CPT
// CLOCK MODE = 01, 32 MHz (or 16 MHz color), M4408
// CLOCK MODE = 10, 12 MHz, 525 LINE
// CLOCK MODE = 11, 12 MHz, COLOR

// register at offset 1 is for setting data
// register at offset 2 is for setting a pointer
// register at offset 3 is for enabling so called sync ram
// it seems like these are used to write hardware parameters to a ram in tv
// not relevant for usim

// register at offset 4 seems to be used by/for color, see window/color
// see also sch: nxbctl, maybe that is this extra register

// window/shwarm.lisp:MAIN-SCREEN-WIDTH and MAIN-SCREEN-HEIGHT
// cadr			width x height : 768 x 963
// lambda		width x height : 1024 x 796 (landscape)
// lambda		width x height : 800 x 1020 (portrait) (this is commented out)
// explorer width x height : 1024 x 804

// window/shwarm.lisp:MAIN-SCREEN-LOCATIONS-PER-LINE
// cadr:			24
// lambda:		32
// explorer:	32

// window/shwarm.lisp:INITIALIZE
// MAIN-SCREEN-WIDTH
uint32_t tv_width;
// MAIN-SCREEN-HEIGHT - (SHEET-HEIGHT WHO-LINE-SCREEN)
uint32_t tv_height;

// window/shwarm.lisp:MAIN-SCREEN-BUFFER-LENGTH 100000
uint32_t tv_screen_buffer[0100000 /* 32K */]; 

#define BLACK 0xff000000
#define WHITE 0xffffffff

// this is the reset condition with tv_mode = 0 
uint32_t tv_background = BLACK;
uint32_t tv_foreground = WHITE;

#ifdef WITH_SDL3
#else

// this is the buffer for display
// actually there is no need for this but do not want to change all display
// codes at the moment
uint32_t tv_bitmap[1024 * 1024];

#endif

static uint32_t tv_mode = 0;
 
// -1 means do not set the position explicitly
int window_position_x = -1;
int window_position_y = -1;
int window_display = -1;
bool window_always_on_top = false;

static int u_minh = 0x7fffffff;
static int u_maxh;
static int u_minv = 0x7fffffff;
static int u_maxv;

static uint32_t tv_vert_spacing;
static uint32_t tv_sync_ptr;
static uint8_t tv_sync_ram[4096]; // 4Kx8, 12-bit address lines

// sch: ntvinc
static bool
tv_is_sync_prom_enabled(void)
{
    return !((tv_vert_spacing & 0200) != 0);
}

bool
tv_is_black_on_white(void)
{
	return ((tv_mode & 04) != 0);
}

bool
tv_is_interrupt_enabled(void)
{
	return ((tv_mode & 010) != 0);
}

#ifdef WITH_SDL3
#else

static void
accumulate_update(int h, int v, int hs, int vs)
{
	if (h < u_minh)
		u_minh = h;
	if (h + hs > u_maxh)
		u_maxh = h + hs;
	if (v < u_minv)
		u_minv = v;
	if (v + vs > u_maxv)
		u_maxv = v + vs;
}

#endif

void
tv_update_screen(void (*fn)(int, int, int, int))
{
	int hs;
	int vs;

	hs = u_maxh - u_minh;
	vs = u_maxv - u_minv;
	if (u_minh != 0x7fffffff && u_minv != 0x7fffffff && u_maxh && u_maxv) {
		(*fn)(u_minh, u_minv, hs, vs);
	}
	u_minh = 0x7fffffff;
	u_maxh = 0;
	u_minv = 0x7fffffff;
	u_maxv = 0;
}

void
tv_assert_interrupt(void)
{
    if (tv_is_interrupt_enabled())
    {
        // bit<4> is interrupt request
        tv_mode |= (1 << 4);
        assert_xbus_interrupt();
    }
}

void
tv_save_screenshot(char *fn)
{
    char buff[128];
	FILE *f;
	f = fopen(fn, "wb");
	if (f == NULL) {
		warnx("failed to open: %s", fn);
		return;
	}

	fputs("P1\n", f);
	snprintf(buff, 128, "%i", tv_width);
	fputs(buff, f);
	fputc(' ', f);
	snprintf(buff, 128, "%i", tv_height);
	fputs(buff, f);
	fputs("\n", f);
	for (size_t i = 0; i < tv_width * tv_height; i++) 
	{
		const uint32_t offset = i / 32;
		const uint32_t bitpos = i % 32;
		const bool bit = ((tv_screen_buffer[offset] & (1<<bitpos)) != 0);
		fputc(bit ? '1' : '0', f);
		if (i % 70 == 0 && i > 0)
			fputc('\n', f);
	}
	fclose(f);

    warnx("screenshot saved to %s", fn);
}

void
tv_reset(void)
{
	tv_mode = 0;
	memset(tv_screen_buffer, 0, sizeof(tv_screen_buffer));
#ifdef WITH_SDL3
#else
	tv_background = 0x000000;
	tv_foreground = 0xffffff;
	memset(tv_bitmap, 0, sizeof(tv_bitmap));
#endif
}

void
tv_screen_read(uint32_t offset, uint32_t *pv)
{		
	*pv = tv_screen_buffer[offset];
}

void
tv_screen_write(uint32_t offset, uint32_t v)
{
	tv_screen_buffer[offset] = v;

#ifdef WITH_SDL3

    // sdl3 uses tv_screen_buffer only
    
#else

	// this part can also removed when x11 and sdl-2 codes are updated like sdl3
	
	offset *= 32;

	for (size_t i = 0; i < 32; i++) 
	{
		tv_bitmap[offset + i] = (v & 1) ? tv_foreground : tv_background;
		v >>= 1;
	}

	const uint32_t w = offset / tv_width;
	const uint32_t h = offset % tv_width;
	accumulate_update(h, w, 32, 1);

#endif
}

// registers at offset 0 and 4 are basically the same
// white on black and video switch are read-only at 0
// but write-able at 4
void
tv_control_read(uint32_t offset, uint32_t *pv)
{
	switch (offset)
	{
		case 0:
			*pv = tv_mode;
            DEBUG(TRACE_TV, "tv: read mode: #o%o\n", *pv);
			break;

        case 1:
            if (!tv_is_sync_prom_enabled())
            {
                *pv = tv_sync_ram[tv_sync_ptr];
                DEBUG(TRACE_TV, "tv: read sync_ram[#o%o] = #o%o\n", tv_sync_ptr, *pv);
            }
            else
            {
                *pv = 0;
            }
            DEBUG(TRACE_TV, "tv: read sync data: #o%o\n", *pv);
            break;

        // case 2: sync ptr is write-only
        // case 3: vert spacing is write-only

		default:
			warnx("tv: write invalid offset:%o", offset);
			bus_interface_set_xbus_nxm();
	}
}

void
tv_control_write(uint32_t offset, uint32_t v)
{
	switch (offset)
	{
		case 0:
			{
                DEBUG(TRACE_TV, "tv: write mode: #o%o [old:#o%o]\n", v, tv_mode);
				const bool was_bow = tv_is_black_on_white();
				tv_mode = v;
                const bool is_bow = tv_is_black_on_white();
                tv_foreground = is_bow ? BLACK : WHITE;
                tv_background = is_bow ? WHITE : BLACK;
				// is color switched ?
				if (was_bow != is_bow)
				{
					// redraw all screen
					uint32_t bits = 0;
					for (uint32_t i = 0; i < tv_width * tv_height / 32; i++) {
						tv_screen_read(i, &bits);
						tv_screen_write(i, bits);
					}
				}
			}
			break;

        case 1:
            DEBUG(TRACE_TV, "tv: write sync data: #o%o\n", v);
            if (!tv_is_sync_prom_enabled())
            {
                tv_sync_ram[tv_sync_ptr] = v & 0xFF;
                DEBUG(TRACE_TV, "tv: write sync_ram[#o%o] = #o%o\n", 
                        tv_sync_ptr, tv_sync_ram[tv_sync_ptr]);
            }
            break;

        case 2:
            DEBUG(TRACE_TV, "tv: write sync pointer: #o%o\n", v);
            // sch: nsyadr
            // SYNC ADR <11-0> = XDI <11-0>
            tv_sync_ptr = v & 0x0FFF;
            break;

        case 3:
            DEBUG(TRACE_TV, "tv: write vert spacing: #o%o\n", v);
            tv_vert_spacing = v;
            break;

		default:
			warnx("tv: write invalid offset:%o", offset);
			bus_interface_set_xbus_nxm();
	}
}

void
tv_poll(void)
{
#if defined(WITH_X11)
	x11_event();
#elif defined(WITH_SDL2)
	sdl2_event();
#endif
}

void
tv_init(void)
{
    switch (tv_monitor) 
    {
        case 0:		/* CPT */
            NOTICE(TRACE_USIM, "tv: using cpt monitor\n");
            tv_width = 768;
            tv_height = 896;
            break;
        case 1:		/* That other thing ... */
            NOTICE(TRACE_USIM, "tv: using other monitor\n");
            tv_width = 768;
            tv_height = 963;
            break;
        default:
            errx(1, "unknown monitor type: %d", tv_monitor);
            break;
    }
}

void 
tv_bus_reset(void)
{
}
