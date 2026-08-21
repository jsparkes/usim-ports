#include <stdint.h>

#include "m32.h"

uint32_t
rol32(uint32_t value, int bitstorotate)
{
	uint32_t tmp;
	int mask;

	/*
	 * Determine which bits will be impacted by the rotate.
	 */
	if (bitstorotate == 0)
		mask = 0;
	else
		mask = (int) 0x80000000 >> bitstorotate;
	/*
	 * Save off the affected bits.
	 */
	tmp = (uint64_t) (value & mask) >> (32 - bitstorotate);
	/*
	 * Perform the actual rotate, and add the rotated bits back in
	 * (in the proper location).
	 */
	return (value << bitstorotate) | tmp;
}
