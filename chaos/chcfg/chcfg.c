/* chcfg.c --- configuration handling
 */

#include <err.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

#include "chcfg.h"
#include "misc.h"
#include "trace.h"

#define INIHEQ(s, n) (streq(s, section) && streq(n, name))

chcfg_t chcfg = {
#define X(s, n, default) default,
#include "chcfg.defs"
#undef X
};

/*
 * Default values that need to be initialized before we read the
 * configuration file so that the user can override them later.
 */
void
chcfg_init(void)
{
	trace_stream = stderr;
	trace_level = LOG_DEBUG;
	trace_facilities = TRACE_ALL;
}

int
chcfg_handler(void *user, const char *section, const char *name, const char *value)
{
	chcfg_t *cfg;

	cfg = (chcfg_t *) user;
	if (0);
#define X(s, n, default)					\
	else if (INIHEQ(#s, #n)) cfg->s##_##n = strdup(value);
#include "chcfg.defs"
#undef X

	/* *INDENT-OFF* */
	if (INIHEQ("trace", "level")) {
		     if (streq(cfg->trace_level, "alert"))   trace_level = LOG_ALERT;
		else if (streq(cfg->trace_level, "crit"))    trace_level = LOG_CRIT;
		else if (streq(cfg->trace_level, "debug"))   trace_level = LOG_DEBUG;
		else if (streq(cfg->trace_level, "emerg"))   trace_level = LOG_EMERG;
		else if (streq(cfg->trace_level, "err"))     trace_level = LOG_ERR;
		else if (streq(cfg->trace_level, "info"))    trace_level = LOG_INFO;
		else if (streq(cfg->trace_level, "notice"))  trace_level = LOG_NOTICE;
		else if (streq(cfg->trace_level, "warning")) trace_level = LOG_WARNING;
		else {
			warnx("unknown trace level: %s", cfg->trace_level);
			return 1;
		}
	}

	if (INIHEQ("trace", "facilities")) {
		char *s;
		char *sp;

		s = strdup(cfg->trace_facilities);
		sp = strtok(s, " ");
		while (sp != NULL) {
			     if (streq(sp, "all"))	trace_facilities = TRACE_ALL;
			else if (streq(sp, "none"))	trace_facilities = TRACE_NONE;
			else if (streq(sp, "chaosd"))	trace_facilities |= TRACE_CHAOSD;
			else if (streq(sp, "server"))	trace_facilities |= TRACE_SERVER;
			else {
				warnx("unknown trace facility: %s", sp);
				return 1;
			}
			sp = strtok(NULL, " ");
		}
		free(s);
	}
	/* *INDENT-ON* */

	return 1;
}
