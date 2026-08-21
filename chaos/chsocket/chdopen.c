#include <sys/socket.h>
#include <sys/un.h>

#include <string.h>
#include <stdio.h>
#include <unistd.h>

#include "chaosd.h"

static struct sockaddr_un unix_addr;

/*
 * Connect to the chaosd server.  Returns -1 on error.
 */
int
chdopen(void)
{
	int len;
	int fd;

	fd = socket(AF_UNIX, SOCK_STREAM, 0);
	if (fd < 0) {
		perror("socket(AF_UNIX)");
		return -1;
	}
	memset(&unix_addr, 0, sizeof(unix_addr));
	sprintf(unix_addr.sun_path, "%s%s%05u", CHAOSD_SOCKET_PATH, CHAOSD_SOCKET_CLIENT_NAME, getpid());
	unix_addr.sun_family = AF_UNIX;
#if 0
	len = strlen(unix_addr.sun_path) + sizeof(unix_addr.sun_family);
#else
	len = strlen(unix_addr.sun_path) + sizeof(unix_addr) - sizeof(unix_addr.sun_path);
#endif
	unlink(unix_addr.sun_path);
	if (bind(fd, (struct sockaddr *)&unix_addr, len) < 0) {
		perror("bind(AF_UNIX)");
		return -1;
	}
	if (chmod(unix_addr.sun_path, CHAOSD_SOCKET_PERM) < 0) {
		perror("chmod(AF_UNIX)");
		return -1;
	}
#if 0
	sleep(1);
#endif
	memset(&unix_addr, 0, sizeof(unix_addr));
	sprintf(unix_addr.sun_path, "%s%s", CHAOSD_SOCKET_PATH, CHAOSD_SOCKET_SERVER_NAME);
	unix_addr.sun_family = AF_UNIX;
#if 0
	len = strlen(unix_addr.sun_path) + sizeof(unix_addr.sun_family);
#else
	len = strlen(unix_addr.sun_path) + sizeof(unix_addr) - sizeof(unix_addr.sun_path);
#endif
	if (connect(fd, (struct sockaddr *)&unix_addr, len) < 0) {
		perror("connect(AF_UNIX)");
		return -1;
	}
	return fd;
}
