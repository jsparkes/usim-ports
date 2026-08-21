#include <sys/chaos.h>
#include <hosttab.h>
#include <stdio.h>

main(argc, argv)
	char **argv;
{
	if (argc <= 1) {
		fprintf(stderr, "host_name host\n");
		exit(1);
	}
	printf("%o\n", ho_addr(argv[1]));
	exit(0);
}
