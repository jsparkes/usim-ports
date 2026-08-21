#include <stdio.h>
#include <sys/chaos.h>

struct node {
	char *dev;
	int maj;
	int min;
} nodes[] = {
/*	dev		maj		min	*/
	{CHURFCDEV, CHRMAJOR, CHURFCMIN,},
	{CHAOSDEV, CHRMAJOR, CHAOSMIN,},
	{0,},
};

main()
{
	register struct node *np;

	printf(": Make sure the major devices are right!\n");
	for (np = nodes; np->dev; np++)
		printf("/etc/mknod %s c %d %d\n", np->dev, np->maj, np->min);
	fprintf(stderr, "!!! Make sure the major devices are correct\n");
}
