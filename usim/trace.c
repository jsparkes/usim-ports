/* trace.c --- simple trace framework
 */

#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <syslog.h>
#include <unistd.h>

#include <sys/stat.h>
#include <sys/types.h>

#include "trace.h"

// these are not default values but just some initial values
int trace_level = LOG_NOTICE;
int trace_facilities = 0; // TRACE_NONE

void
trace(int facility, int prio, const char *fmt, ...)
{
	va_list ap;

	if (trace_level < prio)
		return;

	if (!(trace_facilities & facility))
		return;

	va_start(ap, fmt);

    // this can be parametrized if needed
    vfprintf(stdout, fmt, ap);
    fflush(stdout);

	va_end(ap);
}
