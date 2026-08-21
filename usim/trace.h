#pragma once

#include <stdio.h>
#include <syslog.h>

extern int trace_level;
extern int trace_facilities;

extern void trace(int, int, const char *, ...);

#define ERR(facility, args...)		trace(facility, LOG_ERR, args)
#define WARNING(facility, args...)	trace(facility, LOG_WARNING, args)
#define NOTICE(facility, args...)	trace(facility, LOG_NOTICE, args)

#ifdef NDEBUG
#define INFO(facility, args...)
#define DEBUG(facility, args...)
#else
#define INFO(facility, args...)		trace(facility, LOG_INFO, args)
#define DEBUG(facility, args...)	trace(facility, LOG_DEBUG, args)
#endif
