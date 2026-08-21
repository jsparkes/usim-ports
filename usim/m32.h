#pragma once

/*
 * For 32-bit integers, (A + B) & (1 << 32) will always be
 * zero.  Without resorting to 64-bit arithmetic, you can find the
 * carry by B > ~A.  How does it work? ~A (the complement of A) is the
 * largest possible number you can add to A without a carry: A + ~A =
 * (1 << 32) - 1.  If B is any larger, then a carry will be generated
 * from the top bit.
 */
#define add32(a, b, ci, out, co)					\
	out = ((uint32_t) a) + (b) + ((ci) ? 1 : 0);			\
	co = (ci) ? (((b) >= ~(a)) ? 0:1) : (((b) > ~(a)) ? 0:1) ;
#define sub32(a, b, ci, out, co)			\
	out = (a) - (b) - ((ci) ? 0 : 1);		\
	co = (unsigned)(out) < (unsigned)(a) ? 1 : 0;
#define abs32(a)				\
	(a) < 0 ? ~(a) + 1 : (a)

extern uint32_t rol32(uint32_t, int);
