#include <err.h>
#include <stdio.h>
#include <stdlib.h>

#include "hosttab.h"

int
main(int argc, char **argv)
{
	if (argc != 2)
		errx(1, "Usage is: chaosnames this-host-in-upper-case");

	readhosts(argv[1], NULL);
	chaosnames(stdout);
	exit(0);
}
