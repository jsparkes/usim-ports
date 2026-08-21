#include <sys/time.h>
#include <sys/resource.h>
#include <sys/chaos.h>
#include <stdio.h>
char buf[512];

main(argc, argv)
	char **argv;
{
	int pid;
	int tcp;

	/*
	 * unfortunate, but I see no reason to propagate the chaos (well...) of
	 * passing the arpa socket number in octal down into the open_tcp routine 
	 */
	int port;

	if (sscanf(argv[2], "%o", &port))
		sprintf(argv[2], "%d", port);

	tcp = open_tcp(argv[1], argv[2]);
	switch (tcp) {
	case -1:
		perror("open tcp socket");
		break;
	case -2:
		fprintf(stderr, "%s: no such host\n", argv[1]);
		break;
	case -3:
		fprintf(stderr, "%s: no such service\n", argv[2]);
		break;
	}
	if (tcp < 0)
		exit(-tcp);
	pid = getpid();
	setpgrp(0, getpid());
	setpriority(PRIO_PGRP, 0, -10);

	if (fork())
		for (;;) {
			if (write(1, buf, read(tcp, buf, sizeof buf)) <= 0)
				killpg(pid, 9), exit(0);
			else
				ioctl(1, CHIOCFLUSH, 0);
	} else
		for (;;) {
			if (write(tcp, buf, read(0, buf, sizeof buf)) <= 0)
				killpg(pid, 9), exit(0);
		}
}
