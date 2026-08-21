#ifndef CHSOCKET_CHAOSD_H
#define CHSOCKET_CHAOSD_H

#include <sys/stat.h>

#define CHAOSD_SOCKET_PATH		"/var/tmp/"
#define CHAOSD_SOCKET_CLIENT_NAME	"chaosd_"
#define CHAOSD_SOCKET_SERVER_NAME	"chaosd_server"
#define CHAOSD_SOCKET_PERM		S_IRWXU

extern int chdopen(void);

#endif
