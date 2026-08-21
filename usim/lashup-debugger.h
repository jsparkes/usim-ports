#pragma once

#include <stdint.h>

extern int lashup_read_timeout;

extern char* lashup_debugger_addr;
extern int lashup_debugger_port;

extern char* lashup_target_addr;
extern int lashup_target_port;

// triggered by a read from 766100
bool lashup_debugger_read(uint32_t uaddr, uint16_t *pv);

// triggered by a write to 766100
bool lashup_debugger_write(uint32_t uaddr, uint16_t v);

// triggered by nxm inhibit bit<2> of a write to 766110
bool lashup_debugger_inhibit_nxm(bool);

// triggered by a reset bit<1> of a write to 766110
bool lashup_debugger_reset_unibus_and_bus_interface(void);

// the commands and fields below are not standardized by CADR or LISPM

// triggered by bit<3> of a write to 766110
// symbol is in bits<15..8>
bool lashup_debugger_mark_debuggee(uint8_t symbol);

// triggered by bit<4> of a write to 766110
// symbol is in bits<15..8>
bool lashup_debugger_mark_debugger(uint8_t symbol);

// triggered by bit<5> of a write to 766110
bool lashup_debugger_ping();

// triggered by writing 0766102, the data written is cmd|param
bool lashup_debugger_usim(uint8_t cmd, uint8_t param);
