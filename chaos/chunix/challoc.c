#include <stdlib.h>
#include <unistd.h>

#ifdef __linux__
#include <malloc.h>

#include <sys/types.h>
#include <sys/uio.h>
#endif

void *
ch_alloc(int size, int cantwait)
{
	(void)cantwait;
	return calloc(size, sizeof(void *));
}

void
ch_free(void *p)
{
	free((void *)p);
}

int
ch_size(register char *p)
{
#ifdef __linux__
	return malloc_usable_size(p);
#else
	return -1;
#endif
}

int
ch_badaddr(char *p)
{
	(void)p;
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
