#include "chaos.h"
#include "chunix/chconf.h"
#include "chlinux/chsys.h"
#include "../chncp/chncp.h"

#include <linux/slab.h>

unsigned long alloc_count;
unsigned long free_count;
unsigned long alloc_bytes;
unsigned long free_bytes;

void *
ch_alloc(int size, int cantwait)
{
	char *p;

	p = kmalloc(size + 4, cantwait ? GFP_ATOMIC : GFP_KERNEL);
	if (p) {
		*(int *)p = size;
		p += 4;
	}

	if (p) {
		printk("ch_alloc(%d) = %p\n", size, p - 4);
		alloc_count++;
		alloc_bytes += size;
	}

	return p;
}

void
ch_free(void *p)
{
	p -= 4;
	printk("ch_free(%p)\n", p);

	free_count++;
	free_bytes += *(int *)p;

	kfree(p);
}

int
ch_size(register char *p)
{
	p -= 4;
	return *(int *)p;
}

int
ch_badaddr(char *p)
{
	return 0;
}

void
ch_bufalloc(void)
{
}

void
ch_buffree(void)
{
}
