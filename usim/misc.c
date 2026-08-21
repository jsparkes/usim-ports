/* misc.c --- random utilities
 */

#include <errno.h>
#include <ctype.h>
#include <err.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>

#include "misc.h"
#include "utrace.h"

ssize_t
xgetline(char **l, size_t *n, FILE *f)
{
	int old_errno = errno;
	size_t ret;

	old_errno = errno;
	errno = 0;
	ret = getline(l, n, f);
	if (!feof(f) && ferror(f)) {
		perror("getline failed");
		exit(EXIT_FAILURE);
	}
	errno = old_errno;
	return ret;
}

bool
streq(const char *a, const char *b)
{
	return strcmp(a, b) == 0;
}

// src is expected to be a 0-terminated str
// trims src on both sides
// copies it to dst which has a maximum size of n
// dst is terminated with 0 at the end
void
strntrim(char* dst, size_t n, const char* src)
{
    size_t k = 0;
    size_t i = 0;
    bool start = false;

    while ((src[i] != 0) && (k < (n-1)))
    {
        if (start)
        {
            // terminate at first space
            if (src[i] == ' ') break;
            dst[k++] = src[i];
        }
        else
        {
            // start at first non-space
            if (src[i] != ' ') 
            {
                start = true;
                dst[k++] = src[i];
            }
        }

        i++;
    }

    // termiante destination
    dst[k] = 0;
}

char *
strlwr(char *s)
{
	for (char *p = s; *p; p++)
		*p = tolower(*p);
	return s;
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

int
write16(int fd, uint16_t v)
{
	unsigned char b[2];
	int ret;

	b[0] = (v >> 0) & 0xff;
	b[1] = (v >> 8) & 0xff;
	ret = write(fd, b, 2);
	if (ret != 2)
		errx(1, "write error; ret %d, size %d", ret, 2);
	return 0;
}

int
write32le(int fd, uint32_t v)
{
	unsigned char b[4];
	int ret;

	b[0] = (v >> 0) & 0xff;
	b[1] = (v >> 8) & 0xff;
	b[2] = (v >> 16) & 0xff;
	b[3] = (v >> 24) & 0xff;
	ret = write(fd, b, 4);
	if (ret != 4)
		errx(1, "write error; ret %d, size %d", ret, 4);
	return 0;
}

uint16_t
read16le(int fd)
{
	unsigned char b[2];
	int ret;

	ret = read(fd, b, 2);
	if (ret != 2)
		errx(1, "read16le: read error; ret %d, size %d", ret, 2);
	return (b[1] << 8) | b[0];
}

/* read a 32 bit value in little-endian (3210) */
uint32_t
read32le(int fd)
{
	unsigned char b[4];
	int ret;

	ret = read(fd, b, 4);
	if (ret != 4)
		errx(1, "read32le: read error; ret %d, size %d", ret, 4);
	return ((uint32_t) b[3] << 24 | (uint32_t) b[2] << 16 | (uint32_t) b[1] << 8 | (uint32_t) b[0] << 0);
}

/* read a 32 bit value in PDP-endian (1032), also called mixed-endian or middle-endian */
uint32_t
read32pdp(int fd)
{
	unsigned char b[4];
	int ret;

	ret = read(fd, b, 4);
	if (ret != 4)
		errx(1, "read32pdp: read error; ret %d, size %d", ret, 4);
	return ((uint32_t) b[1] << 24 | (uint32_t) b[0] << 16 | (uint32_t) b[3] << 8 | (uint32_t) b[2] << 0);
}

unsigned long
str4(char *s)
{
	return (s[3] << 24) | (s[2] << 16) | (s[1] << 8) | s[0];
}

char *
unstr4(unsigned long s)
{
	static char b[5];

	b[3] = s >> 24;
	b[2] = s >> 16;
	b[1] = s >> 8;
	b[0] = s;
	b[4] = 0;
	return b;
}

int
read_block(int fd, int block_no, unsigned char *buf)
{
	off_t offset;
	off_t ret;
	int size;

	offset = block_no * BLOCKSZ;
	ret = lseek(fd, offset, SEEK_SET);
	if (ret != offset) {
		perror("lseek");
		return -1;
	}
	size = BLOCKSZ;
	ret = read(fd, buf, size);
	if (ret != size) {
		warnx("disk read error; ret %d, size %d", (int) ret, size);
		perror("read");
		return -1;
	}
	return 0;
}

int
write_block(int fd, int block_no, unsigned char *buf)
{
	off_t offset;
	off_t ret;
	int size;

	offset = block_no * BLOCKSZ;
	ret = lseek(fd, offset, SEEK_SET);
	if (ret != offset) {
		perror("lseek");
		return -1;
	}
	size = BLOCKSZ;
	ret = write(fd, buf, size);
	if (ret != size) {
		warnx("disk write error; ret %d, size %d", (int) ret, size);
		perror("write");
		return -1;
	}
	return 0;
}

static int bnum = -1;

uint32_t
read_virt_fd(int fd, unsigned int addr, int addroff)
{
	int b;
	off_t offset;
	static unsigned int bbuf[256];

	addr &= 077777777;	/* 24 bit address. */
	b = addr / 256;
	offset = (b + addroff) * BLOCKSZ;
	if (b != bnum) {
		off_t ret;

		bnum = b;
		ret = lseek(fd, offset, SEEK_SET);
		if (ret != offset) {
			perror("seek");
		}
		ret = read(fd, bbuf, BLOCKSZ);
		if (ret != BLOCKSZ) {
			perror("read");
		}
	}
	return bbuf[addr % 256];
}

char *
read_virt_string_fd(int fd, unsigned int addr, int addroff)
{
	unsigned int v;
	unsigned int t;
	unsigned int j;
	static char s[256];

	v = read_virt_fd(fd, addr, addroff);
	t = v & 0xff;
	j = 0;
	for (unsigned int i = 0; i < t; i += 4) {
		unsigned int l;

		l = addr + 1 + (i / 4);
		v = read_virt_fd(fd, l, addroff);
		s[j++] = (char) (v >> 0);
		s[j++] = (char) (v >> 8);
		s[j++] = (char) (v >> 16);
		s[j++] = (char) (v >> 24);
	}
	s[t] = 0;
	return s;
}

uint64_t
load_byte(uint64_t w, int p, int s)
{
	return w >> p & ((1 << s) - 1);
}

uint64_t
deposit_byte(uint64_t w, int p, int s, uint64_t v)
{
	uint64_t m = ((1 << s) - 1) << p;

	return ((w & ~m) | (v << p & m));
}

uint32_t
ldb(int ppss, uint32_t w)
{
	return load_byte(w, ppss >> 6 & 077, ppss & 077);
}

uint32_t
dpb(uint32_t v, int ppss, uint32_t w)
{
	return deposit_byte(w, ppss >> 6 & 077, ppss & 077, v);
}

uint32_t
bit_test(uint32_t bits, uint32_t word)
{
	return (bits & word) != 0;
}

bool
ldb_test(int ppss, uint32_t word)
{
	return ldb(ppss, word) != 0;
}

void
dump_write_header(int fd, uint32_t type, uint32_t size)
{
	write32le(fd, type);
	write32le(fd, size);
}

void
dump_write_data(int fd, ssize_t size, void *data)
{
	ssize_t ret;
	uint32_t nullpage[256] = { 0 };

	if (data == 0 && size == 1024)
		data = nullpage;
	ret = write(fd, data, size);
	if (ret != size)
		errx(1, "write error; ret %ld, size %ld, ptr #x%llx\n", ret, size, (uint64_t) data);
}

void
dump_write_segment(int fd, uint32_t type, uint32_t size, uint32_t *data)
{
	dump_write_header(fd, type, size);
	dump_write_data(fd, size * 4, data);
}

void
dump_write_value(int fd, uint32_t type, uint32_t value)
{
	dump_write_header(fd, type, 1);
	write32le(fd, value);
}

int32_t
dump_find_segment(int fd, uint32_t tag)
{
	uint32_t t;

	t = 0;
	lseek(fd, 4 * 2, SEEK_SET);
	do {
		int32_t s;

		t = read32le(fd);
		s = read32le(fd);
		if (t == tag)
			return s;
		lseek(fd, s * 4, SEEK_CUR);
	} while (t != str4("EOF_"));
	return -1;
}

int32_t
dump_read_segment_single_value(int fd, uint32_t tag)
{
	int32_t size;

	size = dump_find_segment(fd, tag);
	if (size != 1)
		errx(1, "read error; failed to read segment (%s)", unstr4(tag));
	return read32le(fd);
}

// for bool a or a.b or a.b.c use F2S(a.b.c) 
// which will be replaced with F2S_func(a.b.c, "a.b.c")
// where
// if a.b.c is true, returns "C"
// if false, returns "c"
// if !a.b.c is given, prepends - to output
// no extra allocation is done
// the buffer returned is internal
#define MAX_NUMBER_OF_SIMULTANEOUS_F2S_CALLS 32
char* 
F2S_func(bool v, char* s)
{
    // because this is called in printf
    // different buffers are needed
    static char buffers[MAX_NUMBER_OF_SIMULTANEOUS_F2S_CALLS][256];
    static int buffer_idx = 0;
    char *buffer = buffers[buffer_idx++ % MAX_NUMBER_OF_SIMULTANEOUS_F2S_CALLS];
    char *dst = buffer;

	// if s contains a dot, move src to the last dot+1
	// if s contains no dot, move src to the beginning
	char *src = s;
	// go to the end of the string
    while (*src != 0) src++;
	// move back to the last dot
    while (src > s && *src != '.') src--;
	// if this is a dot, move after it
    if (s != src && *src == '.') src++;

	// if s starts with !, set dst to '-'	
    if (s[0] == '!') 
    {
        dst[0] = '-';
        dst++;
    }

    // copy src to dst using toupper or tolower depending on the value of v
    while (*src != 0)
    {
        if (v) *dst = toupper(*src);
        else *dst = tolower(*src);
        src++;
        dst++;
    }

    // null terminate dst
    *dst = 0;

	//DEBUG(TRACE_MISC, "F2S_func: %s=%s -> %s\n", s, v ? "true" : "false", buffer);

    return buffer;
}