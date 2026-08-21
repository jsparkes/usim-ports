#pragma once

/**
 * DEBUGGER-DEBUGGEE bus to bus (BUSINT) connection protocol
 *
 * requests are always sent from DEBUGGER
 *
 * commands: command(uaddr, data)
 *
 * uaddr is 18 bits, hence it should be uint32_t
 * data is 16 bits, corresponds to DBD lines in schematic
 * 
 * - READ           : r(uaddr,  X)          ; reads data from uaddr
 * - WRITE          : w(uaddr,  data)       ; writes data to uaddr
 * - NXM INHIBIT    : t(X,      inhibit)    ; inhibit=1 means inhibit, =0 means dont inhibit
 * - RESET          : s(X,      X)          ; resets unibus and bus interface
 * - MARK           : e(X,      symbol)     ; prints MARK symbol
 * - PING/PONG      : p(X,      X)          ; prints PONG
 * - USIM COMMAND   : u(X,      sub|param)  ; sub is subcommand defined below
 *
 * reply:
 * uint16_t bus_error_status, uint16_t data
 * bus_error_status: is used as bus-interface:debuggee_bus_error_status
 * data: only valid for READ command
 */

struct __attribute__((packed)) lashup_request_s
{
    char command;
    uint32_t uaddr;
    uint16_t data;
};

struct __attribute__((packed)) lashup_reply_s
{
    uint16_t bus_error_status;
    uint16_t data;
};
