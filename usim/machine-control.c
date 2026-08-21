
#include <assert.h>
#include <err.h>
#include <errno.h>
#include <pthread.h>
#include <semaphore.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <ucfg.h>
#include <usim.h>

#include "bus-adaptor.h"
#include "bus-interface.h"
#include "config.h"
#include "diagnostic-interface.h"
#include "idle.h"
#include "machine-control.h"
#include "ucode.h"
#include "utrace.h"

struct machine_state_s machine_state;

// performance measurement
static struct timespec machine_control_run_ts;
static struct timespec machine_control_halt_ts;
static uint64_t machine_control_run_started_at_machine_cycles;
static bool machine_control_measuring_perf;

// main/cadr thread
static pthread_t machine_control_thread;

void
machine_control_performance_report_start()
{
    if (machine_control_measuring_perf) return;
    machine_control_measuring_perf = true;

    machine_control_run_started_at_machine_cycles = machine_cycles;
    clock_gettime(CLOCK_MONOTONIC, &machine_control_run_ts);
}

void
machine_control_performance_report_stop()
{
    if (!machine_control_measuring_perf) return;
    machine_control_measuring_perf = false;

    clock_gettime(CLOCK_MONOTONIC, &machine_control_halt_ts);
    const double elapsed_in_us = (machine_control_halt_ts.tv_sec - machine_control_run_ts.tv_sec) * 1000000.0 +
		(machine_control_halt_ts.tv_nsec - machine_control_run_ts.tv_nsec) / 1000.0;
	const double clock_rate_MHz = ((machine_cycles - machine_control_run_started_at_machine_cycles) / elapsed_in_us);
	const double clock_period_us = 1.0 / clock_rate_MHz;

	NOTICE(TRACE_USIM, "usim: T:%.1lf ns, f:%.1lf MHz, speedup:%.1lfx, total cycles:%.2lfG\n",
			clock_period_us * 1000, 
			clock_rate_MHz,
			0.180 / clock_period_us,
			machine_cycles / 1.0e9);

#ifdef WITH_SDL3
    const double total_time = elapsed_in_us / 1.0e6;
    const double idle_time = ((double)idle_count * (double)idle_timeout) / 1.0e6;
    const double cpu_time = total_time - idle_time;
    const double idle_time_percent = (idle_time * 100.0) / total_time;
    const double cpu_time_percent = 100.0 - idle_time_percent;

	NOTICE(TRACE_USIM, "usim: cpu:%.2lfs (%.1lf%%), idle:%.2lfs (%.1lf%%), total:%.2lfs, idle count:%llu\n",
            cpu_time, cpu_time_percent, idle_time, idle_time_percent, total_time, idle_count);
#endif
}

// Reset stops the machine by clearing RUN, 
// forces the clock to stop until the RESET operation is over, 
// clears the pipeline flags which cause things to happen in the next instruction, 
// and clears the Clock, Mode, and OPC registers of the diagnostic interface.
void
machine_control_reset(void)
{
	DEBUG(TRACE_USIM, "usim: CADR reset\n");

    machine_state.halted = false;
    machine_state.vmaok = false;
    machine_state.promdisabled = false;
}

// "When the machine is powered on it resets itself and the Unibus
// but does not automatically start up."
static void
machine_control_power_on(void)
{
	DEBUG(TRACE_USIM, "usim: CADR powering on...\n");

	machine_control_reset();

    // "The machine also resets the busses when it is powered up."
    bus_interface_bus_reset();

	NOTICE(TRACE_USIM, "usim: CADR powered on\n");
}

static void
machine_control_power_off(void)
{
	DEBUG(TRACE_USIM, "usim: CADR powering off...\n");

	NOTICE(TRACE_USIM, "usim: CADR powered off\n");
}

// --- CALLED by WRITING TO MODE REGISTER ---

// "A bootstrap sequence can be initiated in any of several ways:
// - The diagnostic interface
// - The diagnostic display panel (by grounding a wire/a push button)
// - The bus interface (by grounding a wire)
// - The chaos network interface (receiving a certain sequence of messages)
// - The I/O board recognizes a special set of keyboard commands as a boot signal"

void
machine_control_boot(void)
{
    NOTICE(TRACE_USIM, "usim: CADR booting\n");

	// "The bootstrap sequence starts by resetting the machine,
	// which will halt it if it is running."
	machine_control_reset();

	// "It turns on RUN, which will not do anything yet since the clock is 
	// stopped. It sets the machine to its slowest speed, disables parity traps,
	// errors halts, and the statistics counter, and enables the PROM. The 
	// trailing edge of the boot signal allows the clock to start, causing a
	// trap to microcode location 0, just like the memory parity
	// error trap."
	
	machine_state.promdisabled = false;

    // BOOT sets BOOT.TRAP
    // BOOT.TRAP sets TRAP
    // TRAP (as in memory parity errors) disables NPC selector
    // which causes the NPC output to be 0
    // TRAP also sets N to NOP/inhibit the execution of 
    // unfetched instruction in p0
    
    // TRAP sets NPC ouput to be 0
    npc = 0;
    // TRAP also sets N/inhibit
    inhibit = true;
}

static void*
machine_control_thread_run(__attribute__((unused)) void* arg)
{
    machine_control_start_blocking();
    return NULL;
}

void
machine_control_start_blocking(void)
{
    machine_control_power_on();
    ucode_run();
    machine_control_power_off();
}

bool
machine_control_start_nonblocking(void)
{
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);

    if (pthread_create(&machine_control_thread, &attr, machine_control_thread_run, NULL) != 0) 
    {
        ERR(TRACE_USIM, "usim: cannot create machine thread\n");
        return false;
    }
    else
    {
        return true;
    }
}

void
machine_control_shutdown(void)
{
    DEBUG(TRACE_USIM, "usim: shutdown\n");
    machine_state.halted = true;
}
