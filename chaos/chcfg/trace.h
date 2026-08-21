#ifndef TRACE_H
#define TRACE_H

#include <stdio.h>
#include <syslog.h>

#define TRACE_NONE		0000000
#define TRACE_CHAOSD		0000001
#define TRACE_SERVER		0000002
#define TRACE_unused0		0000004
#define TRACE_unused1		0000010
#define TRACE_unused2		0000020
#define TRACE_unused3		0000040
#define TRACE_unused4		0000100
#define TRACE_unused5		0000200
#define TRACE_unused6		0000400
#define TRACE_ALL		0000777

#ifndef TRACE_ALL
#define TRACE_ALL 0
#endif

extern int trace_level;
extern int trace_facilities;
extern FILE *trace_stream;
extern int trace_fd;

extern void trace(int, int, const char *, ...);

#define EMERG(facility, args...)	trace(facility, LOG_EMERG, args)
#define ALERT(facility, args...)	trace(facility, LOG_ALERT, args)
#define CRIT(facility, args...)		trace(facility, LOG_CRIT, args)
#define ERR(facility, args...)		trace(facility, LOG_ERR, args)
#define WARNING(facility, args...)	trace(facility, LOG_WARNING, args)
#define NOTICE(facility, args...)	trace(facility, LOG_NOTICE, args)
#define INFO(facility, args...)		trace(facility, LOG_INFO, args)
#define DEBUG(facility, args...)	trace(facility, LOG_DEBUG, args)

#endif
