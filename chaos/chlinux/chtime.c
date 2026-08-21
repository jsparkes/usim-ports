#include "chaos.h"

/*
 * Return the time according to the chaos TIME protocol, in a long.
 * No byte shuffling need be done here, just time conversion.
 */
void
ch_time(tp)
	register long *tp;
{
	*tp += 60L * 60 * 24 * ((1970 - 1900) * 365L + 1970 / 4 - 1900 / 4);
}

void
ch_uptime(tp)
	register long *tp;
{
	*tp = 0 /* (time - boottime) * 60L */ ;
}
