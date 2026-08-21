/* MINI --- Chaosnet MINI protocol server
 *
 * Reverse engineered from MIT Lisp Machine code by Robert Swindells.
 */

#include <sys/stat.h>

#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#ifdef SELECT
#include "uch11-backend.h"
#else
#include "chaos.h"
#endif
#include "chopen.h"

void buffer_to_lispm(unsigned char *data, int length);
int parsepath(char *path, char **dir, char **real, int blankok);

struct chstatus chst;		/* Status buffer for connections */

#ifdef SELECT
void *
_processmini(void *conn)
#else
int
main(int argc, char **argv)
#endif
{
#ifdef SELECT
	struct packet *p;
	struct packet *pout;
	ssize_t length;
	int fd;
	int binary;
	unsigned char tbuf[20];
	struct stat sbuf;
	struct tm *ptm;
	char *dirname;
	char *realname;
	int errcode;

	printf("MINI\n");
	binary = 0;
	for (;;) {
		p = chaos_connection_dequeue(conn);
		if (p == NOPKT)
			break;
		switch (p->pk_op) {
		case 0200:
		case 0201:
			p->pk_cdata[PH_LEN(p->pk_phead)] = '\0';
			printf("MINI: op %o %s\n", p->pk_op, p->pk_cdata);
			pout = chaos_allocate_packet(conn, DWDOP, CHMAXDATA);
			dirname = 0;
			realname = 0;
			errcode = parsepath((char *)p->pk_cdata, &dirname, &realname, 0);
			if (errcode != 0) {
				pout->pk_op = 0203;
				printf("MINI: parsepath failed %s\n", strerror(errno));
				chaos_connection_queue(conn, pout);
				continue;
			}
			fd = open(realname, O_RDONLY);
			printf("MINI: realname = %s\n", realname);
			if (fd < 0) {
				pout->pk_op = 0203;
				printf("MINI: open failed %s\n", strerror(errno));
				chaos_connection_queue(conn, pout);
				if (dirname)
					free(dirname);
				if (realname)
					free(realname);
				continue;
			} else {
				if (dirname)
					free(dirname);
				if (realname)
					free(realname);
				pout->pk_op = 0202;
				fstat(fd, &sbuf);
				ptm = localtime(&sbuf.st_mtime);
				strftime(tbuf, sizeof(tbuf), "%D %T", ptm);
				length = sprintf(pout->pk_cdata, "%s%c%s", p->pk_cdata, 0215, tbuf);
				SET_PH_LEN(pout->pk_phead, (unsigned short)length);
				chaos_connection_queue(conn, pout);
				binary = (p->pk_op) & 1;
				printf("MINI: binary = %d\n", binary); 
			}
			free(p);
			do {
				char buffer[CHMAXDATA];

				length = read(fd, buffer, CHMAXDATA);
#if 0
				printf("MINI: read %d\n", length);
#endif
				if (length == 0)
					break;
				pout = chaos_allocate_packet(conn, (binary) ? DWDOP : DATOP, length);
				memcpy(pout->pk_cdata, buffer, length);
				if (binary == 0)
					buffer_to_lispm(pout->pk_cdata, length);
				chaos_connection_queue(conn, pout);
			} while (length > 0);
#if 0
			printf("MINI: before eof\n");
#endif
			pout = chaos_allocate_packet(conn, EOFOP, 0);
			chaos_connection_queue(conn, pout);
			close(fd);
			break;
		default:
			printf("MINI: op %o\n", p->pk_op);
			break;
		}
	}
#else
	struct chpacket p;
	struct chpacket pout;
	ssize_t length;
	int fd;
	int binary;
	unsigned char tbuf[20];
	struct stat sbuf;
	struct tm *ptm;

	chsetmode(0, CHRECORD);
	chstatus(0, &chst);
	printf("MINI: %s\n", argv[1]);
	binary = 0;
	for (;;) {
		length = read(0, (char *)&p, sizeof(p));
		if (length <= 0) {
			printf("MINI: Ctl connection broken(%d,%d)\n", length, errno);
			exit(0);
		}
		switch (p.cp_op) {
		case 0200:
		case 0201:
			((char *)&p)[length] = '\0';
			printf("MINI: op %o %s\n", p.cp_op, p.cp_data);
			fd = open(p.cp_data, O_RDONLY);
			if (fd < 0) {
				pout.cp_op = 0203;
				printf("MINI: open failed %s\n", strerror(errno));
			} else {
				pout.cp_op = 0202;
				fstat(fd, &sbuf);
				ptm = localtime(&sbuf.st_mtime);
				strftime(tbuf, sizeof(tbuf), "%D %T", ptm);
				length = sprintf(pout.cp_data, "%s%c%s", p.cp_data, 0215, tbuf);
				while (write(1, (char *)&pout, length + 1) < 0)
					usleep(10000);
				binary = p.cp_op & 1;
				printf("MINI: binary = %d\n", binary); 
			}
			do {
				pout.cp_op = (binary) ? DWDOP : DATOP;
				length = read(fd, pout.cp_data, CHMAXDATA);
				printf("MINI: read %d\n", length); 
				if (length == 0)
					break;
				if (binary == 0)
					buffer_to_lispm(pout.cp_data, length);
				while (write(1, (char *)&pout, length + 1) < 0)
					usleep(10000);
			} while (length > 0);
			printf("MINI: before eof\n");
			pout.cp_op = EOFOP;
			while (write(1, (char *)&pout, 1) < 0)
				usleep(10000);
			close(fd);
			break;
		default:
			printf("MINI: op %o\n", p.cp_op);
			break;
		}
	}
#endif
}

void
buffer_to_lispm(unsigned char *data, int length)
{
	int c;
	int i;

	for (i = 0; i < length; i++) {
		c = data[i] & 0377;
		switch (c) {
		case 0210:	/* Map SAIL symbols back */
		case 0211:
		case 0212:
		case 0213:
		case 0214:
		case 0215:
		case 0377:	/* Give back infinity */
			c &= 0177;
			break;
		case '\n':	/* Map canonical newline to lispm */
			c = CHNL;
			break;
		case 015:	/* Reverse linefeed map kludge */
			c = 0212;
		case 010:	/* Map the format effectors back */
		case 011:
		case 013:
		case 014:
		case 0177:
			c |= 0200;
		}
		data[i] = c;
	}
}

#ifdef SELECT
static pthread_t processmini_thread;

void
processmini(struct chaos_connection *conn)
{
	pthread_create(&processmini_thread, NULL, _processmini, (void *)conn);
}
#endif
