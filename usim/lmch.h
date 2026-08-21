#pragma once

#include "misc.h"

#define LMCH_CODE_LIMIT 0377

#define LMCH_NoSymbol -1

enum
{
#define X(n, v) LMCH_##n = v,
#include "lmch.defs"
#undef X
};

extern struct lmchar_map
{
	char *name;
	int lmchar;
} lmchar_map[LMCH_CODE_LIMIT];
