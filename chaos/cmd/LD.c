#include <sys/chaos.h>

main(argc, argv)
{
	int i, n, fd, other_fd;
	char buf[512];

	if ((other_fd = chopen(chaos_addr("cupid", 0), "NETLOAD", 0, 0, 0, 0, 0)) < 0) {
		printf("Unknown NETLOAD server on cupid\n");
		exit();
	}
	for (;;) {
		if ((fd = chlisten("LD", 0, 1, 0)) < 0) {
			printf("Cannot listen on 'LD' contact name\n");
			exit();
		}
		close(other_fd);
		ioctl(fd, CHIOCSWAIT, CSLISTEN);
		ioctl(fd, CHIOCANSWER, 0);
		ioctl(fd, CHIOCSMODE, CHRECORD);
		n = read(fd, buf, 512);
		write(1, buf, n);
		write(1, "Rec", 3);
		other_fd = fd;
	}
}
