#pragma once

/*
 * USAGE OF TRACE LEVELS AND FACILITIES
 *
 * Each subsystem (a source file) should have its own trace facility.
 * The default facility is TRACE_USIM, and trace level is
 * TRACE_NOTICE.  So it is a good idea to put messages that are
 * directly useful to the user in TRACE_USIM, e.g. disk_init()
 * mentioning which disk is being opened, or uch11_init() reporting
 * what address usim is using.  The TRACE_MISC facility can be used as
 * a catch all for things that don't fit anywhere.  One can also
 * combine trace levels, e.g. TRACE_IOB | TRACE_UNIBUS for some IOB
 * devices that live on the IOB, but are connected through the Unibus.
 *
 * The <err.h> functions should be used for cases where system
 * functions fail, e.g. if open(2) cannot open a file, or if malloc(3)
 * cannot allocate memory.
 *
 * It is generally OK to continue executing after hitting a ERR or
 * higher trace level.  It is also OK to use a facility in a different
 * subsystem (e.g., TRACE_TV in ucode.c) if it makes sense.
 *
 * How levels are used:
 *
 *	EMERG	-- Not currently used.
 *	ALERT	-- Not currently used.
 *	CRIT	-- Not currently used.
 *	ERR	-- Plain old error; normally used as reporting from other
 *		   functions if they caused an error.
 *	WARNING -- Warning message used for altering the user isn't
 *		   entierly right, but we can still continue execution
 *		   without any lossage.
 *	NOTICE	-- Useful messages to inform the user (e.g., our
 *		   address, what symbol file was loaded, which disk
 *		   images have been opened, etc.).
 *	INFO	-- Information about what the particular sub system is
 *		   doing (e.g., CH11 getting/transmitting packets,
 *		   commands being issued to the disk controller, etc).
 *	DEBUG	-- Everything else that might or not be useful (e.g,
 *		   dump packet content when RX/TX in uch11.c, what
 *		   block was read or written to in the disk
 *		   controller, etc.).
 */

#define TRACE_NONE              0000000000
#define TRACE_USIM              0000000001
#define TRACE_UCODE             0000000002
#define TRACE_MICROCODE         0000000004
#define TRACE_MACROCODE         0000000010
#define TRACE_INT               0000000020
#define TRACE_VM                0000000040
#define TRACE_UNIBUS            0000000100
#define TRACE_UNIBUS_MAPPING    0000000200
#define TRACE_XBUS              0000000400
#define TRACE_BUS_INTERFACE     0000001000
#define TRACE_IOB               0000002000
#define TRACE_KBD               0000004000
#define TRACE_TV                0000010000
#define TRACE_COLOR             0000020000
#define TRACE_MOUSE             0000040000
#define TRACE_DISK              0000100000
#define TRACE_CHAOS             0000200000
#define TRACE_X11               0000400000
#define TRACE_SPY               0001000000
#define TRACE_LASHUP            0002000000
#define TRACE_MISC              0004000000
#define TRACE_TAPE	        0010000000
#define TRACE_IDLE              0020000000
#define TRACE_unused0           0040000000
#define TRACE_ALL               0777777777

#include "trace.h"
