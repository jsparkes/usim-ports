/* sdl3_audio.c --- SDL3 audio
 */

#include <errno.h>
#include <math.h>
#include <time.h>
#include <sys/time.h>

#include <SDL3/SDL.h>

#include "sdl3-audio.h"
#include "ucode.h"
#include "utrace.h"

#define WANT_SAMPLE_RATE 8000

double sdl3_audio_beep_amplitude = 0.8;
bool sdl3_audio_use_ascii_beep = false;

static SDL_AudioStream *sdl_audiostream = NULL;
static SDL_AudioSpec spec;

// these are set only at init
static double max_amplitude = 0.0;
static double dt = 0.0;

// these change depending on the beep calls
static double f = 0.0;
static double t = 0.0;

#define BUFFER_SIZE (16 * 1024)
static int16_t buffer[BUFFER_SIZE];

static void
sdl3_audio_fill_stream(double duration)
{
	const double t_stop = t + duration;

	int idx = 0;

	while (t < t_stop) 
	{
		buffer[idx++] = (int16_t) (max_amplitude * sin(2.0 * M_PI * f * t));
		t += dt;
		if (idx == BUFFER_SIZE) 
		{
			SDL_PutAudioStreamData(sdl_audiostream, buffer, idx * 2);
			idx = 0;
		}
	}
	if (idx > 0) 
	{
		SDL_PutAudioStreamData(sdl_audiostream, buffer, idx * 2);
	}
}

static int last_wavelength_in_us = 0;

static void
sdl3_audio_internal_beep(int wavelength_in_us, int duration_in_us)
{
	DEBUG(TRACE_KBD,
			"sdl3: beep(wavelength=%d us, duration=%d us)\n", 
			wavelength_in_us, 
			duration_in_us);

	// this magical contants indicate a standard beep from CADR
	// determined experimentally
	// (beep) calls this function with these params
	if (sdl3_audio_use_ascii_beep && 
			(wavelength_in_us == 1488) && 
			(duration_in_us == 1058)) 
	{
		printf("\a\n");
		return;
	}

	// might be disabled by setting beep_amplitude to zero
	if (sdl_audiostream == NULL) return;

	// if wavelength thus f changes, flush stream to device
	// this may insert a small silent padding
	// so this should not be done for consecutive calls of the same wavelength
	if (wavelength_in_us != last_wavelength_in_us) 
	{
		SDL_FlushAudioStream(sdl_audiostream);
		f = 1000000.0 / (double) wavelength_in_us;
		// should we reset t here or not
		// if we reset, the next sample will jump
		// if not, the playback of new freq will start not from 0
	}
	last_wavelength_in_us = wavelength_in_us;

	const double duration = (double)duration_in_us / 1000000.0;

	sdl3_audio_fill_stream(duration);

	// do not wait for duration less than 100ms
	// this is a dirty hack
	// CADR calls beep with short durations, which breaks the audio buffer
	// this is to prevent it
	if (duration < 0.1) return;

	SDL_Delay(duration_in_us / 1000);
}

bool
sdl3_audio_init(void)
{
	if (sdl3_audio_beep_amplitude == 0.0) 
	{
		NOTICE(TRACE_USIM, "sdl3: audio disabled (beep_amplitude is zero%s)\n",
			sdl3_audio_use_ascii_beep ? ", ascii beep is available" : "");
		return true;
	}

#ifndef NDEBUG
    INFO(TRACE_USIM, "sdl3: available audio drivers: ");
    for (int i = 0; i < SDL_GetNumAudioDrivers() - 1; i++)
    {
        INFO(TRACE_USIM, "%s ", SDL_GetAudioDriver(i));
    }
    INFO(TRACE_USIM, "\n");
#endif    

    NOTICE(TRACE_USIM, "sdl3: current audio driver: %s\n", SDL_GetCurrentAudioDriver());

#ifndef NDEBUG
    INFO(TRACE_USIM, "sdl3: available audio devices: ");
    int count;

    SDL_AudioDeviceID *ids = SDL_GetAudioPlaybackDevices(&count);
    for (int i = 0; i < count; i++)
    {
        INFO(TRACE_USIM, "\"%s\" ", SDL_GetAudioDeviceName(ids[i]));
    }

    INFO(TRACE_USIM, "\n");
#endif    

    SDL_AudioSpec want;
    want.format = SDL_AUDIO_S16;
    want.channels = 1;
    want.freq = WANT_SAMPLE_RATE;

    sdl_audiostream = SDL_OpenAudioDeviceStream(
            SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, 
            &want,
            NULL, // sdl3_audio_callback,
            NULL);

    if (sdl_audiostream == NULL) {

        // this is a warning because it is not so much critical for usim to function
        WARNING(TRACE_KBD, "failed to open audio device stream: %s", SDL_GetError());
        return false;

    } else {

        NOTICE(TRACE_USIM, "sdl3: current audio device: \"%s\"\n", 
                SDL_GetAudioDeviceName(SDL_GetAudioStreamDevice(sdl_audiostream)));

        if (!SDL_GetAudioStreamFormat(sdl_audiostream, &spec, NULL)) {
            WARNING(TRACE_KBD, "failed to get audio stream format: %s", SDL_GetError());
            return false;
        }

        // different spec.freq does not matter
        // the audio functions above use spec.freq not WANT_SAMPLE_RATE
        if ((spec.channels != want.channels) || (spec.format != want.format)) {
            WARNING(TRACE_KBD, "failed to use required audio specs");
            return false;
        }

        NOTICE(TRACE_KBD, "sdl3: current audio sample rate: %d\n", spec.freq);

    }

    max_amplitude = 32767.0 * sdl3_audio_beep_amplitude;
    dt = 1.0 / (double) spec.freq;

    if (!SDL_ResumeAudioStreamDevice(sdl_audiostream)) 
    {
        WARNING(TRACE_KBD, "failed to resume audio stream device\n");
    }

    return true;
}

void
sdl3_audio_quit(void)
{
	if (sdl_audiostream != NULL) {
		SDL_PauseAudioStreamDevice(sdl_audiostream);
		SDL_DestroyAudioStream(sdl_audiostream);
		sdl_audiostream = NULL;
	}
}

void
sdl3_audio_beep(__attribute__((unused)) int v)
{
	DEBUG(TRACE_KBD, "sdl3: beep\n");	

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
#define DTP_FIX_VAL(x) ((x) & ((1 << 24) - 1))
#define DTP_FIX(x) (DTP_FIX_VAL(x) | 5 << 25)
	int half_wavelength_in_us = DTP_FIX_VAL(mmem[7]);	//M-C M-MEM 7
	int duration_in_us = DTP_FIX_VAL(mmem[22]);	//M-1 M-MEM 22
#undef DTP_FIX																						
#undef DTP_FIX_VAL
	sdl3_audio_internal_beep(half_wavelength_in_us * 2, duration_in_us);
}
