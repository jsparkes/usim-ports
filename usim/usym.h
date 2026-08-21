#pragma once

#include <stdint.h>
#include <sys/queue.h>

typedef enum
{
	IMEM = 1,
	DMEM = 2,
	AMEM = 4,
	MMEM = 5,
	NUMBER = 6
} symtype_t;

typedef struct sym
{
	char *name;
	uint32_t v;
	symtype_t mtype;
	LIST_ENTRY(sym) next;
} sym_t;

typedef struct symtab
{
	char *name;
	int sym_count;
	LIST_HEAD(symbols, sym) symbols;
} symtab_t;

extern void sym_read_file(symtab_t *, char *);
extern char *sym_find_by_type_val(symtab_t *, symtype_t, uint32_t, int *);
extern int sym_find(symtab_t *, char *, int *);

void sym_release(symtab_t *tab);
