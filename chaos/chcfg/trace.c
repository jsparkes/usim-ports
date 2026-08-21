/* trace.c --- simple trace framework
 */

#include <sys/stat.h>
#include <sys/types.h>

#include <fcntl.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <syslog.h>
#include <unistd.h>

#include "trace.h"

int trace_level = LOG_NOTICE;
int trace_facilities = TRACE_NONE;
FILE *trace_stream;
int trace_fd = -1;

void
trace(int facility, int prio, const char *fmt, ...)
{
	va_list ap;

	if (trace_level < prio)
		return;
	if (!(trace_facilities & facility))
		return;
	va_start(ap, fmt);
	if (prio < LOG_ERR && trace_stream != stderr) {
		vfprintf(stderr, fmt, ap);
		fflush(stderr);
	}
	if (trace_stream != NULL) {
		vfprintf(trace_stream, fmt, ap);
		fflush(trace_stream);
	}
	if (trace_fd != -1) {
		vdprintf(trace_fd, fmt, ap);
		fsync(trace_fd);
	}
	va_end(ap);
}
