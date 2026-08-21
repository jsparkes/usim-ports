/* syms.c --- routines for handling CADRLP symbol tables
 */

#include <err.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/queue.h>

#include "misc.h"
#include "usym.h"

static void
sym_add(symtab_t *tab, int memory, char *name, uint32_t v)
{
	sym_t *s;

	s = malloc(sizeof(sym_t));
	s->name = strdup(name);
	s->v = v;
	s->mtype = memory;
	if (LIST_EMPTY(&tab->symbols))
		LIST_INSERT_HEAD(&tab->symbols, s, next);
	else {
		sym_t *ss;
		sym_t *p;

		p = LIST_FIRST(&tab->symbols);
		if (p->v >= v)
			LIST_INSERT_HEAD(&tab->symbols, s, next);
		else {
			LIST_FOREACH(ss, &tab->symbols, next) {
				if (ss->v < v)
					p = ss;
			}
			LIST_INSERT_AFTER(p, s, next);
		}
	}
	tab->sym_count++;
}

char *
sym_find_by_type_val(symtab_t *tab, symtype_t memory, uint32_t v, int *offset)
{
	sym_t *s;
	sym_t *closest;

	closest = NULL;
	LIST_FOREACH(s, &tab->symbols, next) {
		if (s->mtype != memory)
			continue;
		/*
		 * Found exact match?
		 */
		if (s->v == v) {
			if (offset)
				*offset = 0;
			return s->name;
		} else if (s->v < v)
			closest = s;
		else if (s->v > v)
			break;

	}
	if (closest && offset) {
		*offset = v - closest->v;
		return closest->name;
	}
	return NULL;
}

int
sym_find(symtab_t *tab, char *name, int *pval)
{
	sym_t *s;

	LIST_FOREACH(s, &tab->symbols, next) {
		if (strcasecmp(name, s->name) == 0) {
			*pval = s->v;
			return 0;
		}
	}
	return -1;
}

static int
sym_typeno(char *n)
{
	int type;

	type = 0;
	if (strcmp(n, "I-MEM") == 0)
		type = 1;
	else if (strcmp(n, "D-MEM") == 0)
		type = 2;
	else if (strcmp(n, "A-MEM") == 0)
		type = 4;
	else if (strcmp(n, "M-MEM") == 0)
		type = 5;
	else if (strcmp(n, "NUMBER") == 0)
		type = 6;
	else
		errx(1, "unknown section type in symbol table: %s", n);
	return type;
}

/*
 * Read a CADR MCR symbol file.
 *
 * This very much expects a correctly formated symbol file, and does
 * not try to handle anything else very gracefully.  See
 * WRITE-SYMBOL-TABLE from SYS: UCADR; QWMCR LISP and
 * CONS-DUMP-SYMBOLS from SYS: SYS; CDMP LISP for details.
 */
void
sym_read_file(symtab_t *tab, char *filename)
{
	FILE *f;
	size_t lsz;
	int loc;
	int n;
	char *l;
	char sym[64];
	char symtype[64];

	l = NULL;
	lsz = 0;
	f = fopen(filename, "r");
	if (f == NULL) {
		err(1, "failed to open: %s", filename);
	}
	LIST_INIT(&tab->symbols);
	tab->name = strdup(filename);
	xgetline(&l, &lsz, f);
	xgetline(&l, &lsz, f);	/* -4 assembler state info. */
	if (strcmp(l, "-4 \n") != 0)
    {
        free(l);
		errx(1, "sym_read_file: failed to find assembler state info section (-4)");
    }
	xgetline(&l, &lsz, f);
	xgetline(&l, &lsz, f);	/* -2 symbol dump start. */
	/*
	 * First symbol is handled specially, since directly after the
	 * -2 marker the symbol, type and address follows.
	 */
	n = sscanf(l, "-2 %s %s %o \n", sym, symtype, &loc);
	if (n != 3)
    {
        free(l);
		errx(1, "sym_read_file: failed to find symbol dump section (-2)");
    }
	sym_add(tab, sym_typeno(symtype), sym, loc);
	while (xgetline(&l, &lsz, f) != -1) {
		if (strcmp(l, "-1 ") == 0) {	/* -1 EOF. */
			fclose(f);
            free(l);
			return;
		}
		n = sscanf(l, "%s %s %o \n", sym, symtype, &loc);
		if (n != 3) continue;
		sym_add(tab, sym_typeno(symtype), sym, loc);
	}
	fclose(f);
    free(l);
	errx(1, "sym_read_file: failed to eof section marker (-1)");
}

void
sym_release(symtab_t *tab)
{
    sym_t *s;
	LIST_FOREACH(s, &tab->symbols, next) {
        free(s->name);
	}   
    free(tab->name);
}
