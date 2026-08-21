#ifndef CHAOS_CHCFG_H
#define CHAOS_CHCFG_H

#include "ini.h"

typedef struct {
#define X(s, n, default) char *s##_##n;
#include "chcfg.defs"
#undef X
} chcfg_t;

extern chcfg_t chcfg;

extern void chcfg_init(void);
extern int chcfg_handler(void *, const char *, const char *, const char *);

#endif
