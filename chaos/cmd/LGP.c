#include <sys/chaos.h>

#define FILE_NAME 0
#define PRINTER_NAME 1
#define USER_NAME 2
#define PRETTY_NAME 3
#define CREATE_DATE 4
#define BANNER_STRING 5
#define NPROTOC_ITEMS 6

char LGPSPOOL[512];

main(argc, argv)
	int argc;
	char **argv;
{
	sprintf(LGPSPOOL, "%s/lpr", DESTUSERS);
	argc--;
	argv++;
	if (argc >= USER_NAME) {
#ifdef BSD42
		execl("/usr/ucb/lpr", "LGP", "-l", "-Plp-fast", "-T", argv[FILE_NAME], "-U", argv[USER_NAME], 0);
#else
		execl(LGPSPOOL, "LGP", "-t", argv[FILE_NAME], "-l", "LGP-SERVER", 0);
#endif
	} else
		execl(LGPSPOOL, "LGP", 0);
	chreject(0, "LGP spooling program doesn't exist");
}
