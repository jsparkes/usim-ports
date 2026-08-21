/* idle.c --- sleep when system is not running
 */

#include <err.h>
#include <errno.h>
#include <pthread.h>
#include <signal.h>
#include <stdio.h>
#include <time.h>
#include <unistd.h>

#include <sys/select.h>

#include "idle.h"
#include "machine-control.h"
#include "tv.h"
#include "ucode.h"
#include "usim.h"
#include "utrace.h"

// usim.ini:idle section config options
bool idle_enabled;
size_t idle_cycles;
size_t idle_quantum;
size_t idle_timeout;

static bool is_idle;
static uint64_t last_work;
static uint64_t last_cycle;

#ifdef WITH_SDL3

uint64_t idle_count;
static struct timespec timeout_ts;
static sigset_t timeout_sigset;
static pthread_t idle_thread;

#define SIG_WAKEUP SIGUSR2

#else

static int working;
static int reported_idle;	/*debug */
static int drl;
static int disk_run_light;

#ifdef WITH_X11
static int maxfd;
static int registered_fds[FD_SETSIZE];
#endif

#endif

void
idle_init(void)
{
    is_idle = false;
    last_work = 0;
    last_cycle = 0;

#ifdef WITH_SDL3

    idle_count = 0;
    timeout_ts.tv_sec = idle_timeout / 1000000;
    timeout_ts.tv_nsec = (idle_timeout % 1000000) * 1000;
    sigemptyset(&timeout_sigset);
    sigaddset(&timeout_sigset, SIG_WAKEUP);

#else

    int val;
    sym_find(&sym_mcr, "A-DISK-RUN-LIGHT", &val);
    disk_run_light = val;
    working = 1;
    reported_idle = 1;

#endif

    if (idle_enabled)
    {
        DEBUG(TRACE_USIM, "idle: init cycles:%zu quantum:%zu timeout:%zu\n",
                idle_cycles, idle_quantum, idle_timeout);
    }
    else
    {
        NOTICE(TRACE_USIM, "idle: is disabled\n");
    }

}

void
idle_quit(void)
{
}

bool
idle_is_idle(void)
{
    return is_idle;
}

#ifdef WITH_SDL3

void
idle_activity(void)
{
	last_work = machine_cycles;
}

// sdl3 specific idle_check does not look at the disk run light
// because last_work is set with idle_activity_with_source from
// keyboard, mouse, disk and chaos activity
void
idle_check(uint64_t cycles)
{
	if ((cycles - last_work) > idle_cycles) 
    {
        is_idle = true;
		if ((cycles & idle_quantum) == 0) 
        {
            idle_count++;
            idle_thread = pthread_self();
            int ret = pselect(0, NULL, NULL, NULL, &timeout_ts, &timeout_sigset);
            if ((ret == -1) && (errno != EINTR))
            {
                err(1, "idle/pselect error");
            }
		}
	}
    else
    {
        is_idle = false;
    }
}

#else

void
idle_activity(void)
{
	last_work = last_cycle;
}

void
idle_check(uint64_t cycles)
{
	last_cycle = cycles;
	if ((cycles & 0x0ffff) == 0) {
		if (!drl) {
			drl = amem[disk_run_light];
		} else {
			uint32_t p;
			tv_screen_read((drl + 2) & 077777, &p);
			working = !!p;
		}
	}
	if (working) {
		last_work = cycles;
		if (reported_idle) 
        {
			reported_idle = 0;
            // working
		}
	} else if ((cycles - last_work) > idle_cycles) {
		if (!reported_idle) 
        {
			reported_idle = 1;
            // idle
		}
		if ((cycles & idle_quantum) == 0) {
#ifdef WITH_X11
			int fd;
			fd_set readset;
			struct timeval timeout;
			timeout.tv_sec = idle_timeout / 1000000;
			timeout.tv_usec = idle_timeout % 1000000;
			FD_ZERO(&readset);
			for (int i = 0; i < FD_SETSIZE; i++) {
				fd = registered_fds[i];
				if (fd == 0)
					break;
				FD_SET(fd, &readset);
			}
			select(maxfd + 1, &readset, NULL, NULL, &timeout);
#endif
		}

	}
}

#ifdef WITH_X11
void
idle_register_fd(int fd)
{
	for (int i = 0; i < FD_SETSIZE; i++) {
		if (registered_fds[i] == 0) {
			registered_fds[i] = fd;
			break;
		}
	}
	if (fd > maxfd)
		maxfd = fd;
}
#endif

#endif
