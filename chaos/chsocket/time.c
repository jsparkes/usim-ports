/* time --- sends a fake TIME packet to chaosd ad infinitum
 */

#include <sys/uio.h>

#include <arpa/inet.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <err.h>

#include "chaos.h"
#include "chaosd.h"
#include "misc.h"

int chaosd_fd;

void
sndpkt(void)
{
	uint16_t p[64];
	uint8_t lenbytes[4];
	struct iovec iov[2];
	int wsize;
	int plen;
	int ret;

	wsize = 0;
	memset(p, 0, sizeof(p));
	printf("sending fake time packet...\n");
	p[wsize++] = htons(ANSOP);	/* op */
	p[wsize++] = htons(16 + 8);	/* count */
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = htons(('T' << 8) | 'I');
	p[wsize++] = htons(('M' << 8) | 'E');
	p[wsize++] = 0;
	p[wsize++] = 0;
	p[wsize++] = htons(0401);	/* dest */
	p[wsize++] = 0;		/* source */
	p[wsize++] = 0;		/* checksum */
	plen = wsize * 2;
	lenbytes[0] = plen >> 8;
	lenbytes[1] = plen;
	lenbytes[2] = 1;
	lenbytes[3] = 0;
	iov[0].iov_base = lenbytes;
	iov[0].iov_len = 4;
	iov[1].iov_base = p;
	iov[1].iov_len = wsize * 2;
	ret = writev(chaosd_fd, iov, 2);
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
		sndpkt();
		sleep(1);
	}
	exit(0);
}
