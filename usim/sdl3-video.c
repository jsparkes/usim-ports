/* sdl3_video.c --- SDL3 video
 */

#include <assert.h>
#include <err.h>
#include <math.h>
#include <stdlib.h>
#include <time.h>

#include <SDL3/SDL.h>
#include <SDL3/SDL_render.h>

#include "colortv.h"
#include "icon.h"
#include "tv.h"
#include "ucfg.h"
#include "usim.h"
#include "utrace.h"

#include "sdl3-video.h"

double sdl3_video_scale = 0.0;
SDL_ScaleMode sdl3_video_scale_mode = SDL_SCALEMODE_LINEAR;
bool sdl3_video_allow_resize = false;

static SDL_Surface *icon_surface = NULL;

// main (monochrome) window
SDL_Window *main_window = NULL;
SDL_WindowID main_window_id;
static SDL_Renderer *main_renderer = NULL;
static SDL_Texture *main_texture = NULL;
static SDL_GLContext main_gl_context;

// color (4bpp) window
static SDL_Window *color_window = NULL;
static SDL_Renderer *color_renderer = NULL;
static SDL_Texture *color_texture = NULL;
static SDL_GLContext color_gl_context;

#ifndef NDEBUG
static void
sdl3_video_print_sdl_info(void)
{
	INFO(TRACE_USIM, "sdl3: available video drivers: ");
	for (int i = 0; i < SDL_GetNumVideoDrivers(); i++)
	{
		const char* driver = SDL_GetVideoDriver(i);
		INFO(TRACE_USIM, "%s ", driver);
	}
	INFO(TRACE_USIM, "\n");

	INFO(TRACE_USIM, "sdl3: available render drivers: ");
	for (int i = 0; i < SDL_GetNumRenderDrivers(); i++) 
	{
		INFO(TRACE_USIM, "%s ", SDL_GetRenderDriver(i));
	}
	INFO(TRACE_USIM, "\n");

	SDL_DisplayID primary_displayid = SDL_GetPrimaryDisplay();

	int ndisplays = 0;
	SDL_DisplayID *display_ids = SDL_GetDisplays(&ndisplays);
	for (int i = 0; i < ndisplays; i++) 
	{
		SDL_DisplayID display_id = display_ids[i];
		const char *display_name = SDL_GetDisplayName(display_id);
		const SDL_DisplayMode *display_mode = SDL_GetCurrentDisplayMode(display_id);
		INFO(TRACE_USIM, 
				"sdl3: display %d [%d/%s] %dx%d %.2fHz, pixel_density=%.2f%s\n", 
                i,
				display_id,
				display_name,
				display_mode->w,
				display_mode->h,
				display_mode->refresh_rate,
				display_mode->pixel_density,
				display_id == primary_displayid ? " [primary]" : "");
	}
    SDL_free(display_ids);
}
#endif

bool
sdl3_video_init(void)
{
#ifndef NDEBUG
	sdl3_video_print_sdl_info();
#endif    

	NOTICE(TRACE_USIM, "sdl3: current video driver: %s\n", SDL_GetCurrentVideoDriver());

    int display_x = 0;
    int display_y = 0;
    float display_pixel_density = 0.0f;

    size_t display_idx = 0;
    if (window_display > 0) display_idx = window_display;

    int ndisplays = 0;
    SDL_DisplayID *display_ids = SDL_GetDisplays(&ndisplays);
    assert (display_ids != NULL);
    if (window_display >= ndisplays)
    {
        SDL_free(display_ids);
        ERR(TRACE_USIM, "sdl3: there are %d displays (< %d given in usim.ini)", 
                ndisplays, window_display);
        return false;
    }
    else
    {
        SDL_Rect display_bounds;
        SDL_GetDisplayBounds(display_ids[display_idx], &display_bounds);
        const SDL_DisplayMode *display_mode = SDL_GetCurrentDisplayMode(display_ids[display_idx]);
        SDL_free(display_ids);

        display_x = display_bounds.x;
        display_y = display_bounds.y;
        display_pixel_density = display_mode->pixel_density;
    }

	INFO(TRACE_USIM, "sdl3: display_pixel_density=%f\n", display_pixel_density);

	if (sdl3_video_scale == 0)
	{
		// set sdl3_video_scale to 1 because it is used to unscale mouse coordinates
		sdl3_video_scale = 1.0; 
		NOTICE(TRACE_USIM, 
			"sdl3: creating main window %dx%d with native scaling, display_pixel_density=%f\n",
			tv_width,
			tv_height,
			display_pixel_density);

        if (colortv_enabled)
        {
            NOTICE(TRACE_USIM, 
                "sdl3: creating color window %dx%d with native scaling, display_pixel_density=%f\n",
                colortv_width,
                colortv_height,
                display_pixel_density);
        }
	}
	else
	{
		NOTICE(TRACE_USIM, 
			"sdl3: creating main window %dx%d using usim:scale=%lf\n", 
			tv_width,
			tv_height,
			sdl3_video_scale);

        if (colortv_enabled)
        {
            NOTICE(TRACE_USIM, 
                "sdl3: creating color window %dx%d using usim:scale=%lf\n", 
                colortv_width,
                colortv_height,
                sdl3_video_scale);
        }
	}

    // --- SET HINTS AND OpenGL ATTRIBUTES ---

    // vsync on
	SDL_SetHint(SDL_HINT_RENDER_VSYNC, "1");
    // use streaming texture
	SDL_SetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION, "1");
    // dont install SIGINT and SIGTERM handlers
	SDL_SetHint(SDL_HINT_NO_SIGNAL_HANDLERS, "1");
    // prefer opengl 
    SDL_SetHint(SDL_HINT_RENDER_DRIVER, "opengl");

    // --- CREATE WINDOW AND RENDERER ---
	
	if (!SDL_CreateWindowAndRenderer
			(
			 "usim",
			 tv_width * sdl3_video_scale, 
			 tv_height * sdl3_video_scale,
			 (sdl3_video_allow_resize ? SDL_WINDOW_RESIZABLE : 0) | SDL_WINDOW_OPENGL,
			 &main_window,
			 &main_renderer
			)) 
    {
		ERR(TRACE_USIM, "sdl3: failed to create main window and renderer: %s\n", SDL_GetError());
		return false;
	}

        SDL_SetRenderScale(main_renderer, sdl3_video_scale, sdl3_video_scale);

    main_gl_context = SDL_GL_CreateContext(main_window);

    if (main_gl_context == NULL) 
    {
		ERR(TRACE_USIM, "sdl3: failed to create GL context for main window: %s\n", SDL_GetError());
		return false;
    }

	NOTICE(TRACE_USIM, "sdl3: current render driver: %s\n", SDL_GetRendererName(main_renderer));

    main_window_id = SDL_GetWindowID(main_window);

    if (main_window_id == 0)
    {
		ERR(TRACE_USIM, "sdl3: failed to get main window ID: %s\n", SDL_GetError());
		return false;
    }

    if (colortv_enabled)
    {
        if (!SDL_CreateWindowAndRenderer
                (
                 "usim - Color TV",
                 colortv_width * sdl3_video_scale, 
                 colortv_height * sdl3_video_scale,
                 (sdl3_video_allow_resize ? SDL_WINDOW_RESIZABLE : 0) | SDL_WINDOW_OPENGL,
                 &color_window,
                 &color_renderer
                )) 
        {
            ERR(TRACE_USIM, "sdl3: failed to create color window and renderer: %s\n", SDL_GetError());
            return false;
        }

        SDL_SetRenderScale(color_renderer, sdl3_video_scale, sdl3_video_scale);

        color_gl_context = SDL_GL_CreateContext(color_window);

        if (color_gl_context == NULL) 
        {
            ERR(TRACE_USIM, "sdl3: failed to create GL context for color window: %s\n", SDL_GetError());
            return false;
        }
    }

    // --- SET WINDOW POSITION ---

    if (SDL_SetWindowPosition(
                main_window, 
                window_position_x + display_x, 
                window_position_y + display_y)) 
    {
        DEBUG(TRACE_USIM, 
                "sdl3: window position is explicitly set to %d,%d\n",
                window_position_x,
                window_position_y);

        if (colortv_enabled)
        {
            if (!SDL_SetWindowPosition(
                        color_window, 
                        window_position_x + display_x + tv_width * sdl3_video_scale + 64,
                        window_position_y + display_y + (tv_height * sdl3_video_scale - colortv_height) / 2))
            {
                WARNING(TRACE_USIM, "sdl3: failed to set color window position: %s\n", SDL_GetError());
            }
        }
    } 
    else
    {
        WARNING(TRACE_USIM, "sdl3: failed to set main window position: %s\n", SDL_GetError());
    }

    SDL_SetWindowAlwaysOnTop(main_window, window_always_on_top);

    if (color_window != NULL) 
    {
        SDL_SetWindowAlwaysOnTop(color_window, window_always_on_top);
    }

	// cursor is drawn by CADR, so hide the system cursor
	SDL_HideCursor();
	
	// SDL disables screen saver, so expliticly enable it
	SDL_EnableScreenSaver();

    // --- LOAD ICON ---

	// icon. 32x32 is a must. 64x64 can be alternative for hidpi displays.
    // use xxd -i to create icon.h
    SDL_IOStream* icon_io = SDL_IOFromMem(icon_bmp, icon_bmp_len);
    if (icon_io != NULL)
    {
        icon_surface = SDL_LoadBMP_IO(icon_io, true); 
        if (icon_surface != NULL)
        {
            const SDL_PixelFormatDetails *pfd = SDL_GetPixelFormatDetails(icon_surface->format);
            if (pfd != NULL)
            {
                // icon.bmp is indexed, so the palette parameter is required here
                const Uint32 transparent = SDL_MapRGB(
                        pfd, 
                        SDL_GetSurfacePalette(icon_surface), 
                        255, 0, 255);
                SDL_SetSurfaceColorKey(icon_surface, true, transparent);
                SDL_SetWindowIcon(main_window, icon_surface);
            }
            else
            {
                WARNING(TRACE_USIM, "sdl3: failed to get pixel format details of icon: %s", 
                        SDL_GetError());
            }
        }
        else
        {
            WARNING(TRACE_USIM, "sdl3: failed to load icon from io: error: %s", 
                    SDL_GetError());
        }
    }
    else
    {
        WARNING(TRACE_USIM, "sdl3: failed to create icon io: error: %s", 
                SDL_GetError());
    }

    // --- CHECK IF VSYNC IS REALLY ON ---

	// check if vsync hint worked
	int vsync;
	if (SDL_GetRenderVSync(main_renderer, &vsync)) {
		if (vsync == 1) {
            INFO(TRACE_USIM, "sdl3: vsync is on\n");
		} else {
            WARNING(TRACE_USIM, "sdl3: vsync is off, 60Hz updates will have unknown timings.\n");
		}
	} else {
		ERR(TRACE_USIM, "sdl3: failed to get render vsync: %s\n", SDL_GetError());
		return false;
	}

    // --- SET RENDER LOGICAL PRESENTATION ---

	if (!SDL_SetRenderLogicalPresentation
			(
				main_renderer, 
				tv_width, 
				tv_height, 
				SDL_LOGICAL_PRESENTATION_LETTERBOX
			)) 
    {
		ERR(TRACE_USIM, "sdl3: failed to set main renderer scale: %s\n", SDL_GetError());
		return false;
	}

    if (color_renderer != NULL)
    {
        if (!SDL_SetRenderLogicalPresentation
			(
				color_renderer, 
				colortv_width, 
				colortv_height, 
				SDL_LOGICAL_PRESENTATION_LETTERBOX
			)) 
        {
            ERR(TRACE_USIM, "sdl3: failed to set color renderer scale: %s\n", SDL_GetError());
            return false;
        }

    }

    // --- CREATE TEXTURE AND SET SCALE MODE ---

	main_texture = SDL_CreateTexture
		(
		 main_renderer, 
		 SDL_PIXELFORMAT_ARGB8888,
		 SDL_TEXTUREACCESS_STREAMING,
		 tv_width,
		 tv_height
		);

	if (main_texture == NULL) {

		ERR(TRACE_USIM, "sdl3: failed to create main texture: %s\n", SDL_GetError());
		return false;

	}

    if (color_renderer != NULL)
    {
        color_texture = SDL_CreateTexture
            (
             color_renderer, 
             SDL_PIXELFORMAT_ARGB8888,
             SDL_TEXTUREACCESS_STREAMING,
             colortv_width,
             colortv_height
            );

        if (color_texture == NULL) {

            ERR(TRACE_USIM, "sdl3: failed to create color texture: %s\n", SDL_GetError());
            return false;

        }
    }

    sdl3_video_set_scalemode(sdl3_video_scale_mode);

    // otherwise color_window has the focus
    SDL_RaiseWindow(main_window);
	
	return true;
}

void
sdl3_video_set_scalemode(SDL_ScaleMode scalemode)
{
    if (SDL_SetTextureScaleMode(main_texture, scalemode))
	{
		DEBUG(TRACE_USIM, "sdl3: texture scale mode is set to: ");

        switch (scalemode)
        {
            case SDL_SCALEMODE_LINEAR: DEBUG(TRACE_USIM, "linear"); break;
            case SDL_SCALEMODE_NEAREST: DEBUG(TRACE_USIM, "nearest"); break;
            case SDL_SCALEMODE_INVALID: DEBUG(TRACE_USIM, "invalid"); break;
        }

        DEBUG(TRACE_USIM, "\n");

        if (color_texture != NULL)
        {
            if (!SDL_SetTextureScaleMode(color_texture, scalemode))
            {
                WARNING(TRACE_USIM, "sdl3: failed to set color texture scale mode: %s\n", SDL_GetError());
            }
        }
	}
	else
	{
		WARNING(TRACE_USIM, "sdl3: failed to set main texture scale mode: %s\n", SDL_GetError());
	} 
}

void
sdl3_video_update_window_title(char *window_title)
{
	SDL_SetWindowTitle(main_window, window_title);
}

void
sdl3_video_quit(void) 
{
	if (main_texture != NULL) SDL_DestroyTexture(main_texture);
	if (main_renderer != NULL) SDL_DestroyRenderer(main_renderer);
	if (icon_surface != NULL) SDL_DestroySurface(icon_surface);
	if (main_window != NULL) SDL_DestroyWindow(main_window);

	if (color_texture != NULL) SDL_DestroyTexture(color_texture);
	if (color_renderer != NULL) SDL_DestroyRenderer(color_renderer);
	if (color_window != NULL) SDL_DestroyWindow(color_window);
}

bool
sdl3_video_present_main(void)
{
    SDL_GL_MakeCurrent(main_window, main_gl_context);

	uint32_t *pixels;
	int pitch;

	// optimal way to update texture is Lock, copy pixel data, Unlock

	if (!SDL_LockTexture(main_texture, NULL, (void**)&pixels, &pitch)) {
		ERR(TRACE_TV, "sdl3: failed to lock texture: %s\n", SDL_GetError());
		return false;
	}

    // 4 = BYTES_PER_PIXEL of ARGB8888
    const uint32_t diff = tv_width - (pitch / 4);

    uint32_t *screen_buffer = tv_screen_buffer;

	for (uint32_t y = 0; y < tv_height; y++)
	{
        uint32_t bit_counter = 0;
        uint32_t screen_word = *screen_buffer;

		for (uint32_t x = 0; x < tv_width; x++)
		{
			// pixel 0 set by lispm is at bit position 0 also
			// not at bit position 31
			const bool bit = ((screen_word & 1) != 0);
			*pixels = bit ? tv_foreground : tv_background;
            screen_word = screen_word >> 1;
            pixels++;
            bit_counter++;
            if (bit_counter == 32)
            {
                bit_counter = 0;
                screen_buffer++;
                screen_word = *screen_buffer;
            }
		}

        // in case pitch is not zero
        pixels += diff;
	}

	SDL_UnlockTexture(main_texture);

	if (!SDL_RenderClear(main_renderer)) {
		ERR(TRACE_TV, "sdl3: failed to render clear main: %s\n", SDL_GetError());
		return false;
	}

	if (!SDL_RenderTexture(main_renderer, main_texture, NULL, NULL)) {
		ERR(TRACE_TV, "sdl3: failed to render main texture: %s\n", SDL_GetError());
		return false;
	}

	if (!SDL_RenderPresent(main_renderer)) {
		ERR(TRACE_TV, "sdl3: failed to render present main: %s\n", SDL_GetError());
		return false;
	}

	return true;
}

bool
sdl3_video_present_color(void)
{
    if (color_renderer == NULL) return true;

    colortv_vsync = true;

    SDL_GL_MakeCurrent(color_window, color_gl_context);

    uint32_t *pixels;
	int pitch;

	if (!SDL_LockTexture(color_texture, NULL, (void**)&pixels, &pitch)) {
		ERR(TRACE_TV, "sdl3: failed to lock color texture: %s\n", SDL_GetError());
		return false;
	}

    struct timespec hsync_ts = {0, 1000};

    // 4 = BYTES_PER_PIXEL of ARGB8888
    const uint32_t diff = colortv_width - (pitch / 4);

    uint32_t *screen_buffer = colortv_screen_buffer;

	for (uint32_t y = 0; y < colortv_height; y++)
	{
        colortv_hsync = true;

        uint32_t bit_counter = 0;
        uint32_t screen_word = *screen_buffer;

		for (uint32_t x = 0; x < colortv_width; x++)
		{
            // 4 bits per pixel
			const uint32_t lispm_pixel = (screen_word & 0xF);
            const uint32_t pixel_color = colortv_color_map[lispm_pixel];
			*pixels = pixel_color;
            pixels++;
            bit_counter += 4;
            if (bit_counter == 32)
            {
                bit_counter = 0;
                screen_buffer++;
                screen_word = *screen_buffer;
            }
            else
            {
                screen_word = screen_word >> 4;
            }
		}

        nanosleep(&hsync_ts, NULL);

        colortv_hsync = false;

        // in case pitch is not zero
        pixels += diff;
	}

	SDL_UnlockTexture(color_texture);

	if (!SDL_RenderClear(color_renderer)) {
		ERR(TRACE_TV, "sdl3: failed to render clear color: %s\n", SDL_GetError());
		return false;
	}

	if (!SDL_RenderTexture(color_renderer, color_texture, NULL, NULL)) {
		ERR(TRACE_TV, "sdl3: failed to render texture color: %s\n", SDL_GetError());
		return false;
	}

	if (!SDL_RenderPresent(color_renderer)) {
		ERR(TRACE_TV, "sdl3: failed to render present color: %s\n", SDL_GetError());
		return false;
	}

    colortv_vsync = false;

	return true;

}

bool
sdl3_video_present(void)
{
    if (!sdl3_video_present_main()) return false;
    if (!sdl3_video_present_color()) return false;
    return true;
}
