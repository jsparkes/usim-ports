#include <stdio.h>
#include <sys/chaos.h>

main()
{
	register int c;

	if (freopen("/dev/ramtek", "w", stdout) == NULL)
		chreject(0, "Can't open ramtek printer device");
	else {
		while ((c = getchar()) != EOF)
			putchar(c);
	}
}
