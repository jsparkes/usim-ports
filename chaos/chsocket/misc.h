#ifndef CHSOCKET_MISC_H
#define CHSOCKET_MISC_H

#include <stdbool.h>

bool streq(const char *, const char *);
void daemonize(char *);
void dumpmem(char *, int);
char *chopstr(int);

#endif
