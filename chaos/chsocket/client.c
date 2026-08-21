/* client --- simple chaosnet test client for chaosd server
 */

#include <sys/uio.h>

#include <arpa/inet.h>

#include <err.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "chaos.h"
#include "chaosd.h"
#include "misc.h"

int chaosd_fd;

void
sndpkt(int n)
{
	uint16_t p[64];
	uint8_t lenbytes[4];
	struct iovec iov[3];
	char data[64];
	int wsize;
	int plen;
	int ret;

	wsize = 0;
	memset(p, 0, sizeof(p));
	printf("sending packet...\n");
	p[wsize++] = htons(ANSOP);	/* op */
	p[wsize++] = 20;	/* count */
	p[wsize++] = 0401;
	p[wsize++] = 0;
	p[wsize++] = 0402;
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = n;
	strcpy(data, "TIME");
	plen = 20;
	lenbytes[0] = plen >> 8;
	lenbytes[1] = plen;
	lenbytes[2] = 0;
	lenbytes[3] = 0;
	iov[0].iov_base = lenbytes;
	iov[0].iov_len = 4;
	iov[1].iov_base = p;
	iov[1].iov_len = 16;
	iov[2].iov_base = data;
	iov[2].iov_len = 4;
	ret = writev(chaosd_fd, iov, 3);
	if (ret == -1)
		errx(1, "writev");
}

int
main(int argc, char **argv)
{
	chaosd_fd = chdopen();
	if (chaosd_fd == -1)
		errx(1, "failed to connect to chaosd");
	while (1) {
		int n;

		n = 0;
		for (int i = 0; i < 5; i++) {
			sndpkt(n++);
		}
		sleep(5);
	}
	exit(0);
}
