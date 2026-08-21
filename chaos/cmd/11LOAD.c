/* 11LOAD filename */

#define BSD42			/* Grrrrrrr. */
#include <sys/chaos.h>
#include <stdio.h>
#define min(a,b) ( ((a) < (b))?(a):(b))

struct {
	short d_addr;
	char d_buf[400];
} dpack;

int packets = 0;
int retries = 0;

main(argc, argv)
	char **argv;
{

	FILE *loadf;
	int n;
	struct chstatus st;
	char *name;
	char nambuf[40];
	struct {
		short l_magic;
#define				LDAMAGIC 1
		short l_count;
		unsigned short l_addr;
	} lda;

	freopen("/tmp/11load", "w", stderr);

	fprintf(stderr, "RFC: ");
	for (n = 0; n < argc; n++)
		fprintf(stderr, " %s", argv[n]);
	fprintf(stderr, "\n");

	if (ioctl(0, CHIOCGSTAT, &st) < 0) {
		perror("get status");
		exit(1);
	}
	fprintf(stderr, "from %o ", st.st_fhost);

	if (chdir("11LOAD.files") == -1) {
		perror("11LOAD.files/");
		exit(1);
	}

	if (argc == 2)
		name = argv[1];

	else if (argc > 2)
		name = argv[2];	/* crock for MC filenames */

	else
		name = chaos_name(st.st_fhost);

	fprintf(stderr, "(%s)\n", name);
	loadf = fopen(name, "r");
	if (loadf == NULL) {
		perror(name);
		sprintf(nambuf, "host%o", st.st_fhost);
		name = nambuf;
		loadf = fopen(name, "r");
		if (loadf == NULL) {
			perror(name);
			exit(1);
		}
	}
	while (fread(&lda, sizeof lda, 1, loadf)) {
		if (lda.l_magic != LDAMAGIC) {
			fprintf(stderr, "%s: not an lda file\n", name);
			exit(1);
		}
		lda.l_count -= sizeof lda;
		n = min((sizeof dpack.d_buf), lda.l_count);
		dpack.d_addr = lda.l_addr;
		do {
			int errs = 0;

			if (fread(&(dpack.d_buf[0]), 1, n, loadf) != n) {
				fprintf(stderr, "%s: unexpected EOF\n", name);
				exit(1);
			}
			if (n)
				fprintf(stderr, "send %d bytes to %o\n", n, dpack.d_addr);
			else
				fprintf(stderr, "blastoff at %o\n", dpack.d_addr);
		      again:if ((close	/* functional programming */
				(chrfc(st.st_fhost, "LD ", 1, 0, &dpack, n + 2, 0))) == -1) {
				if (n) {
					fprintf(stderr, "no response\n");

					if (errs++ >= 2)
						report(1);
					else {
						retries++;
						goto again;
					}

				} else
					report(0);	/* no ANS on GO */
			}
			dpack.d_addr += n;

			packets++;
		} while (lda.l_count -= n);
		getc(loadf);	/* eat that checkbyte */
		fflush(stderr);
	}
}

report(rval)
{
	fprintf(stderr, "%d packets, %d retries\n", packets, retries);
	exit(rval);
}


chrfc(address, contact, mode, async, data, dlength, rwsize)
	int address;
	char *contact;
	char *data;
{
	struct chopen rfc;
	static int f = -1;
	int connfd = -1;

	rfc.co_host = address;
	rfc.co_contact = contact;
	rfc.co_data = data;
	rfc.co_length = data ? (dlength ? dlength : strlen(data)) : 0;
	rfc.co_clength = strlen(contact);
	rfc.co_async = async;
	rfc.co_rwsize = rwsize;
	if (f < 0)
		f = open(CHAOSDEV, mode);
#ifdef BSD42
	if (f >= 0)
		connfd = ioctl(f, CHIOCOPEN, &rfc);
#else
	if (f >= 0 && ioctl(f, CHIOCOPEN, &rfc));
	else {
		connfd = f;
		f = -1;
	}
#endif
	return connfd;
}
