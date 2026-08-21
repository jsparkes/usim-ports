#pragma once

#include <stdbool.h>
#include <stdint.h>

#define BIT(R, POS) ((((uint32_t) (R >> POS)) & 1) != 0)
#define BUS(R, START, END) (((uint32_t) (R >> END)) & (((uint32_t)1 << (START - END + 1)) - 1))
#define BIT64(R, POS) (((uint64_t) (R >> POS)) & 1)
#define BUS64(R, START, END) (((uint64_t) (R >> END)) & (((uint64_t)1 << (START - END + 1)) - 1))

struct machine_state_s
{
    bool halted;

    // read from flag register 2
    bool vmaok;

    // mode register
    bool promdisabled;
};

extern struct machine_state_s machine_state;

void machine_control_performance_report_start();
void machine_control_performance_report_stop();

// call _start_X methods to power on CADR
// as name suggest, start_blocking blocks and returns only when CADR is powered off
void machine_control_start_blocking(void);
// start_nonblocking returns immediately, starting CADR in different thread
// call shutdown to terminate this this and power off CADR 
bool machine_control_start_nonblocking(void);

void machine_control_set_run(bool);
void machine_control_shutdown(void);

void machine_control_reboot(void);
void machine_control_boot(void);
