/* misc.c --- random utilities
 */

#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "chaos.h"

bool
streq(const char *a, const char *b)
{
	return strcmp(a, b) == 0;
}

/*
 * Fork the server process and set up to be a good background daemon.
 */
void
daemonize(char *what)
{
	int r;

	chdir("/tmp");
	r = fork();
	if (r > 0)
		exit(0);
	if (r == -1) {
		fprintf(stderr, "%s: unable to fork new process\n", what);
		perror("fork");
		exit(1);
	}
	close(0);
	close(1);
	close(2);
}

static char
tohex(char b)
{
	b = b & 0xf;
	if (b < 10)
		return '0' + b;
	return 'a' + (b - 10);
}

void
dumpmem(char *ptr, int len)
{
	char line[80];
	char chars[80];
	char *p;
	char b;
	char *c;
	char *end;
	int j;
	int offset;

	end = ptr + len;
	offset = 0;
	while (ptr < end) {
		p = line;
		c = chars;
		printf("%04x ", offset);
		*p++ = ' ';
		for (j = 0; j < 16; j++) {
			if (ptr < end) {
				b = *ptr++;
				*p++ = tohex(b >> 4);
				*p++ = tohex(b);
				*p++ = ' ';
				*c++ = ' ' <= b && b <= '~' ? b : '.';
			} else {
				*p++ = 'x';
				*p++ = 'x';
				*p++ = ' ';
				*c++ = 'x';
			}
		}
		*p = 0;
		*c = 0;
		printf("%s %s\n", line, chars);
		offset += 16;
	}
}

/*
 * Returns the string name of a Chaosnet operation.
 */
char *
chopstr(int pt)
{
	switch (pt) {
	case RFCOP:
		return "RFC";
	case OPNOP:
		return "OPN";
	case CLSOP:
		return "CLS";
	case FWDOP:
		return "FWD";
	case ANSOP:
		return "ANS";
	case SNSOP:
		return "SNS";
	case STSOP:
		return "STS";
	case RUTOP:
		return "RUT";
	case LOSOP:
		return "LOS";
	case LSNOP:
		return "LSN";
	case MNTOP:
		return "MNT";
	case EOFOP:
		return "EOF";
	case UNCOP:
		return "UNC";
	case BRDOP:
		return "BRD";
	default:
		return "???";
	}
}
