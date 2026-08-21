#include <err.h>
#include <stdio.h>
#include <stdlib.h>

#include "hosttab.h"

int
main(int argc, char **argv)
{
	unsigned short addr;
	char *name;

	if (argc != 3)
		errx(1, "Usage is: chaosname this-host-in-upper-case addr");

	readhosts(argv[1], NULL);

	addr = strtol(argv[2], NULL, 8);
	name = chaos_name(addr);
	if (name == NULL)
		errx(1, "could not find (or on different subnet) %s", argv[2]);
	printf("%s has address %o\n", name, addr);
}
