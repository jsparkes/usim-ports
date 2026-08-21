/*
 * Chaosnet RFC handler for contact name NETLOAD
 */

#include <stdio.h>
#include <sys/chaos.h>

main(argc, argv)
	register int argc;
	register char *argv[];
{
	register FILE *f, *errfile;
	char buf[256];
	int i;
	char *load_file;
	struct ld_hdr {
		short addr;
		short len;
	}     *pkt_hdr;

	errfile = fopen("/tmp/NETerr", "w");
	fprintf(errfile, "Started\n");
	fflush(errfile);
	pkt_hdr = (struct ld_hdr *)buf;
	pkt_hdr->addr = 0;
	if (argc == 1)
		load_file = "/prod/sned/netbox/netcode.b";
	else
		load_file = argv[1];
	fprintf(errfile, "Got file name %s\n", load_file);
	fflush(errfile);
	if ((f = fopen(load_file, "r")) == NULL) {
		chreject(1, "Cannot find load-file");
		exit();
	}
	fprintf(errfile, "Opened file\n");
	fflush(errfile);
	ioctl(1, CHIOCANSWER, 0);
	write(1, "OK", 2);
	close(0);
	close(1);
	fprintf(errfile, "Answered\n");
	fflush(errfile);
	while ((i = fread(&buf[4], 250, 1, f)) > 0) {
		pkt_hdr->len = i;
		close(chopen(chaos_addr("cupid", 0), "LD  ", 0, 0, buf, i + 4, 0));
		pkt_hdr->addr += i;
		fprintf(errfile, "Worked !!\n");
	}
}
