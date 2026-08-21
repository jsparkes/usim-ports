/*
 * Definitions which are shared between the system dependent storage
 * allocator and other system dependent modules.
 */

#ifdef BSD42
/*
 * Macro to convert a chaos allocation into the corresponding mbuf pointer.
 */
#define chdtom(d) \
		((int)(d) & (MSIZE - 1) ? dtom(d) : (struct mbuf *)((int)(d)&~CLOFSET))
#endif
