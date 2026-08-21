#include <sys/time.h>
#include <stdio.h>

#include "chaos.h"

#ifdef BSD42
#include "param.h"
#include "systm.h"
#include "time.h"
#include "kernel.h"
#endif

/*
 * Return the time according to the chaos TIME protocol, in a long.
 * No byte shuffling need be done here, just time conversion.
 */
void
ch_time(tp)
	register long *tp;
{
#if defined(BSD42)
	*tp = time.tv_sec;
#else
	struct timeval time;

	gettimeofday(&time, NULL);
	*tp = time.tv_sec;
#endif
	*tp += 60L * 60 * 24 * ((1970 - 1900) * 365L + 1970 / 4 - 1900 / 4);
}

void
ch_uptime(tp)
	register long *tp;
{
#ifdef BSD42
	*tp = (time.tv_sec - boottime.tv_sec) * 60L;
#else
	*tp = 0 /* (time - boottime) * 60L */ ;
#endif
}
