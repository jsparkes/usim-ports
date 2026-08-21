#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

#ifndef O_BINARY
#define O_BINARY 0
#endif

#define BLOCKSZ (256 * 4)

#define _STRINGIFY(s) #s
#define STRINGIFY(s) _STRINGIFY(s)

#define NELEM(x) (sizeof(x)/sizeof(*x))

#define F2S(X) F2S_func(X, #X)

extern ssize_t xgetline(char **, size_t *, FILE *);

extern bool streq(const char *, const char *);
extern char *strlwr(char *);

extern void dumpmem(char *, int);

extern uint16_t read16le(int);
extern uint32_t read32le(int);
extern uint32_t read32pdp(int);

extern int write16le(int, uint16_t);
extern int write32le(int, uint32_t);

extern unsigned long str4(char *);
extern char *unstr4(unsigned long);

extern int read_block(int, int, unsigned char *);
extern int write_block(int, int, unsigned char *);
extern uint32_t read_virt_fd(int, unsigned int, int);
extern char *read_virt_string_fd(int, unsigned int, int);

extern uint64_t load_byte(uint64_t, int, int);
extern uint64_t deposit_byte(uint64_t, int, int, uint64_t);
extern uint32_t ldb(int, uint32_t);
extern uint32_t dpb(uint32_t, int, uint32_t);

extern uint32_t bit_test(uint32_t, uint32_t);
extern bool ldb_test(int, uint32_t);

extern void dump_write_header(int, uint32_t, uint32_t);
extern void dump_write_data(int, ssize_t, void *);
extern void dump_write_segment(int, uint32_t, uint32_t, uint32_t *);
extern void dump_write_value(int, uint32_t, uint32_t);
extern int32_t dump_find_segment(int, uint32_t);
extern int32_t dump_read_segment_single_value(int, uint32_t);

extern void strntrim(char* dst, size_t n, const char* src);

char* F2S_func(bool v, char* s);
