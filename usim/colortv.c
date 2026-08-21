/* tv.c --- TV interface
 */

#include <err.h>
#include <string.h>

#include "bus-interface.h"
#include "colortv.h"
#include "dump.h"
#include "tv.h"
#include "ucode.h"
#include "utrace.h"

// Color TV is probed by LISPM by writing 1 to 17'200'000 and reading it back
// if it is not 1, Color TV is disabled

// Color TV is 576x454 4 bits per pixel with a 24-bit RGB palette
const uint32_t colortv_width = 576;
const uint32_t colortv_height = 454;

// 17,,200000 to 17,,277777	Color TV
uint32_t colortv_screen_buffer[0100000 /* 32K */]; 

// 4 bit display color map has 16 entries
// but there are 6 bits available when setting this by XDI<5-0>
uint32_t colortv_color_map[64];

static uint32_t colortv_mode = 0;

// the critical part of Color TV implementation is hsync
// because when writing color map, it waits for hsync to not
// change color map in the middle of the line -I think-
// so both vsync and hsync is changed in sdl3-video render code
bool colortv_vsync;
bool colortv_hsync;

static bool sync_prom_enb;

static uint32_t colortv_vert_spacing;
static uint32_t colortv_sync_ptr;
static uint8_t colortv_sync_ram[4096]; // 4Kx8, 12-bit address lines
 
bool
colortv_is_interrupt_enabled(void)
{
	return ((colortv_mode & 010) != 0);
}

void
colortv_assert_interrupt(void)
{
    if (colortv_is_interrupt_enabled())
    {
        // bit<4> is interrupt request
        colortv_mode |= (1 << 4);
        assert_xbus_interrupt();
    }
}

// sch: ntvinc
bool
colortv_is_sync_prom_enabled(void)
{
    return !((colortv_vert_spacing & 0200) != 0);
}

void
colortv_screen_read(uint32_t offset, uint32_t *pv)
{		
	*pv = colortv_screen_buffer[offset];
    DEBUG(TRACE_COLOR, "color: screen read: offset:#o%o *pv:#o%o\n", offset, *pv);
}

void
colortv_screen_write(uint32_t offset, uint32_t v)
{
    DEBUG(TRACE_COLOR, "color: screen write: offset:#o%o *pv:#o%o\n", offset, v);
	colortv_screen_buffer[offset] = v;
}

void
colortv_control_read(uint32_t offset, uint32_t *pv)
{
    switch (offset)
    {
        case 0:
            *pv = colortv_mode | (sync_prom_enb ? 0200 : 0) | (colortv_hsync ? 0100 : 0) | (colortv_vsync ? 040 : 0);
            // disabled because of too much output
            DEBUG(TRACE_COLOR, "color: read mode: #o%o\n", *pv);
            break;

        case 1:
            if (!sync_prom_enb)
            {
                *pv = colortv_sync_ram[colortv_sync_ptr];
                DEBUG(TRACE_COLOR, "color: read sync_ram[#o%o] = #o%o\n", colortv_sync_ptr, *pv);
            }
            else
            {
                *pv = 0;
            }
            DEBUG(TRACE_COLOR, "color: read sync data: #o%o\n", *pv);
            break;

        // case 2: sync ptr is write-only
        // case 3: vert spacing is write-only
        // case 4: color map is write-only
        
        default: 
			warnx("color: read invalid offset:%o", offset);
			bus_interface_set_xbus_nxm();
    }
}

void
colortv_control_write(uint32_t offset, uint32_t v)
{
    switch (offset)
    {
        case 0:
            // disabled because of too much output
            DEBUG(TRACE_COLOR, "color: write mode: #o%o\n", v);
            colortv_mode = v & 037;
            break;

        case 1:
            DEBUG(TRACE_COLOR, "color: write sync data: #o%o\n", v);
            if (!sync_prom_enb)
            {
                colortv_sync_ram[colortv_sync_ptr] = v & 0xFF;
                DEBUG(TRACE_COLOR, "color: write sync_ram[#o%o] = #o%o\n", 
                        colortv_sync_ptr, colortv_sync_ram[colortv_sync_ptr]);
            }
            break;

        case 2:
            DEBUG(TRACE_COLOR, "color: write sync pointer: #o%o\n", v);
            // sch: nsyadr
            // SYNC ADR <11-0> = XDI <11-0>
            colortv_sync_ptr = v & 0x0FFF;
            break;

        case 3:
            DEBUG(TRACE_COLOR, "color: write vert spacing: #o%o\n", v);
            sync_prom_enb = ((v & 0200) == 0);
            colortv_vert_spacing = v & 0177;
            break;

        case 4:
            {
                DEBUG(TRACE_COLOR, "color: write color map: #o%o\n", v);
                // sch: ramcol, synmod
                // COLOR VALUE<7-0> = XDI<15-8>
                // color values are reversed 
                // (0: max intensity, 255: min intensity)
                const uint32_t color_channel_value = 255 - ((v >> 8) & 0xFF);
                // LOAD COLOR 3,2,1,0 = SELECTED WITH XDI<7-6>
                // 00 is R, 01 is G, 10 is B, 11 is not defined
                const uint32_t color_channel = (v >> 6) & 0x03;
                // location = XDI<5-0>
                const uint32_t location = v & 0x3F;
                const uint32_t current_value = colortv_color_map[location];
                // mask old value or with new value
                uint32_t new_value;
                switch (color_channel)
                {
                    case 0:
                        new_value = (current_value & 0x0000FFFF) | (color_channel_value << 16);
                        break;

                    case 1:
                        new_value = (current_value & 0x00FF00FF) | (color_channel_value << 8);
                        break;

                    case 2:
                        new_value = (current_value & 0x00FFFF00) | (color_channel_value << 0);
                        break;

                    default:
                        errx(1, "color_channel is %u\n", color_channel);
                }

                // set alpha to opaque
                colortv_color_map[location] = 0xFF000000 | new_value;

                DEBUG(TRACE_COLOR, "color: write loc:#o%o channel:#o%o value:#o$%o final:0x%08x\n",
                        location, color_channel, 
                        color_channel_value, colortv_color_map[location]);

            }
            break;

        default: 
			warnx("color: write invalid offset:%o", offset);
			bus_interface_set_xbus_nxm();
    }
}

void
colortv_init(void)
{
    NOTICE(TRACE_USIM, "color: Color TV is enabled.\n");
}

void 
colortv_bus_reset(void)
{
}
