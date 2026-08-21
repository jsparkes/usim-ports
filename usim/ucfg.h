#pragma once

#include "sys/queue.h"

#include "usim.h"
#include "ini.h"

struct ucfg_kbd_pair_s
{
    char* name;
    char* value;
    STAILQ_ENTRY(ucfg_kbd_pair_s) entries;
};

STAILQ_HEAD(ucfg_kbd_pair_head, ucfg_kbd_pair_s);

typedef struct
{
#define X(s, n, default) char *s##_##n;
#include "ucfg.defs"
#undef X
    struct ucfg_kbd_pair_head kbd_modifiers;
    struct ucfg_kbd_pair_head kbd;
} ucfg_t;

extern ucfg_t ucfg;

void ucfg_init(void);
void ucfg_quit(void);

bool ucfg_load_config_file(char* config_filename);
void ucfg_dump_running_config(FILE*);
