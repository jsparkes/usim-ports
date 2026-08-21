#include <stdio.h>
#include <sys/ioctl.h>
#include <sys/chaos.h>
#include <strings.h>
main(argc, argv)
	char **argv;
{
	char *progname = rindex(argv[0], '/');

	freopen("/dev/console", "w", stderr);
	if (progname == 0)
		progname = argv[0];	/* no slashes in argv[0] */
	else
		progname++;	/* skip the slash in the name */
	switch (progname[2]) {
	case 'a':
	case 'A':
		if (ioctl(0, CHIOCACCEPT, 0) < 0)
			perror("accept");
		break;
	case 'r':
	case 'R':
		if (argc > 1) {
			int i;
			char buf[512];

			strcpy(buf, argv[1]);
			for (i = 2; i < argc; i++) {
				strcat(buf, " ");
				strcat(buf, argv[i]);
			}
			if (strlen(buf) >= sizeof(buf - 1))
				fprintf(stderr, "reject arg string too long %d\n\"%s\"\n", strlen(buf), buf);
			if (chreject(1, buf))
				perror("reject");
		} else if (chreject(1, 0))
			perror("reject");
	default:
		fprintf(stderr, "illegal name %s\n", progname);
	}
}
