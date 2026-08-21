#include <err.h>
#include <stdio.h>

#include "hosttab.h"

int
main(int argc, char **argv)
{
	unsigned short addr;

	if (argc != 3)
		errx(1, "Usage is: chaosaddr this-host-in-upper-case host");

	readhosts(argv[1], NULL);

	addr = chaos_addr(argv[2], 0);
	if (addr == 0)
		errx(1, "could not find (or on different subnet) %s", argv[2]);
	printf("%s has address %o\n", argv[2], addr);
}
