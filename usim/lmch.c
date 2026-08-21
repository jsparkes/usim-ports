#include "lmch.h"

struct lmchar_map lmchar_map[LMCH_CODE_LIMIT] = {
#define X(n, v) {STRINGIFY(n), v},
#include "lmch.defs"
#undef X
	{(char *) 0, 0}
};
