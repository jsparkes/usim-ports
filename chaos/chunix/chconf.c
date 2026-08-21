#include "chaos.h"
#include "chsys.h"
#include "chconf.h"
#include "../chncp/chncp.h"

#ifdef BSD42
#include "time.h"
#include "kernel.h"
#endif

/*
 * This file contains initializations of configuration dependent data
 * structures and device dependent initialization functions.
 */

/*
 * We must identify ourselves
 */
short Chmyaddr = -1;
char Chmyname[CHSTATNAME] = "Uninitialized";
short chhosts[] = { 0 };

/*
 * Reset the NCP and all devices.
 */
void
chreset(void)
{
	struct chroute *r;

	for (r = Chroutetab; r < &Chroutetab[CHNSUBNET]; r++)
		if (r->rt_cost == 0)
			r->rt_cost = CHHCOST;
#if NCHDR > 0
	chdrinit();
#endif				/* NCHDR */
#if NCHCH > 0
	chchinit();
#endif				/* NCHCH */
#if NCHIL > 0
	chilinit();
#endif				/* NCHIL */
#if NCHETHER > 0
	cheinit();
#endif
/* If we have an internet... allow UNC encapsulation. */
#ifdef INET
#if NCHIP > 0
	chipattach();
#endif
#endif
	/*
	 * This is necessary to preserve the modularity of the
	 * NCP.
	 */
#ifdef BSD42
	Chhz = hz;
#else
	Chhz = 60;		/* This is set correctly at auto-conf time but needs
				 * a non-zero initial value at boot time.
				 */
#endif
}

void
chdeinit(void)
{
#if NCHETHER > 0
	chedeinit();
#endif
}

/*
 * Check for interface timeouts
 */
void
chxtime(void)
{
#ifdef NDR11C
	chdrxtime();
#endif				/* NDR11C */
}
