/* chaosd --- Chaosnet for Unix daemon
 *
 * Accepts Unix socket connections from other Chaos nodes and forwards
 * packets.
 */

#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/un.h>

#include <err.h>
#include <fcntl.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "chcfg.h"
#include "trace.h"

#include "chaosd.h"
#include "misc.h"
#include "node.h"
#include "sig.h"
#include "transport.h"

static char *config_filename;
bool server_started;
bool server_running;
bool daemon_flag;

void
restart_child(void)
{
	DEBUG(TRACE_CHAOSD,  "restart_child\n");
#if 0
	/*
	 * Normally we would want to restart ./server; but to not end
	 * up in a tight loop let it crash so we can debug.
	 */
	server_started = false;
#endif
}

void
server_shutdown(void)
{
	server_running = false;
}

/*
 * Create a new socket and set it up to listen for connections.
 * Returns -1 on error.
 */
static int
server_listen(int *newfd)
{
	struct sockaddr_un unix_addr;
	int fd;
	int len;
	char name[1024];

	fd = socket(PF_UNIX, SOCK_STREAM, 0);
	if (fd < 0) {
		perror("socket(PF_UNIX, SOCK_STREAM)");
		return -1;
	}
	sprintf(name, "%s%s", CHAOSD_SOCKET_PATH, CHAOSD_SOCKET_SERVER_NAME);
	unlink(name);
	memset(&unix_addr, 0, sizeof(unix_addr));
	unix_addr.sun_family = PF_UNIX;
	strcpy(unix_addr.sun_path, name);
	len = SUN_LEN(&unix_addr);
	DEBUG(TRACE_CHAOSD, "chaosd: binding socket to %s ...\n", name);
	if (bind(fd, (struct sockaddr *)&unix_addr, len) < 0) {
		perror("bind(PF_UNIX, SOCK_STREAM)");
		return -1;
	}
	if (listen(fd, 5) < 0) {
		perror("listen(PF_UNIX, SOCK_STREAM)");
		return -1;
	}
	*newfd = fd;
	return 0;
}

/*
 * After a socket has indicated it has half a connection, call
 * accept() to make a new socket and create the second half of the
 * connection.  Returns -1 on error.
 */
static int
server_accept(int listenfd, int *newfd)
{
	struct sockaddr_un unix_addr;
	struct stat statbuf;
	socklen_t len;
	int fd;

	DEBUG(TRACE_CHAOSD, "chaosd: server_accept(listenfd = %d)\n", listenfd);
	len = sizeof(unix_addr);
	memset(&unix_addr, 0, len);
	fd = accept(listenfd, (struct sockaddr *)&unix_addr, &len);
	if (fd < 0) {
		perror("accept(listenfd)");
		return -1;
	}
	unix_addr.sun_path[len] = 0;
	DEBUG(TRACE_CHAOSD, "chaosd: server_accept() - fd = %d, len = %d, unix_addr.sun_path = %s\n", fd, len, unix_addr.sun_path);
	if (stat(unix_addr.sun_path, &statbuf) < 0) {
		perror("stat(accepted-socket)");
		return -1;
	}
	if (S_ISSOCK(statbuf.st_mode) == 0) {
		perror("stat(accepted-socket) not a socket?");
		return -1;
	}
	unlink(unix_addr.sun_path);
	*newfd = fd;
	return 0;
}

/*
 * Wrapper to accept a new connection, create a new server and connect
 * the nerw server to new fd.  Returns -1 on error.
 */
static int
server_accept_upcall(int fd)
{
	int new_fd;
	struct node *node;

	DEBUG(TRACE_CHAOSD, "chaosd: server_accept_upcall(fd = %d)\n", fd);
	if (server_accept(fd, &new_fd) != -1) {
		if (node_new(&node) == -1) {
			close(new_fd);
			return -1;
		}
		node_set_fd(node, new_fd);
		DEBUG(TRACE_CHAOSD, "chaosd: server_accept_upcall() - node = %p, new_fd = %d\n", node, new_fd);
		fd_add_reader(new_fd, node_stream_reader, node_close, (void *)node);
	}
	return 0;
}

static int
start_serverd(void)
{
	int r;
	char buf[256];

	r = fork();
	if (r > 0)
		return 0;
	if (r == -1) {
		DEBUG(TRACE_CHAOSD,  "unable to fork new process\n");
		perror("fork");
		exit(1);
	}
	for (int i = 0; i < 256; i++)
		close(i);
	/*
	 * Repoen stdin, stdout, stderr.
	 */
	open("/dev/null", O_RDONLY);
	open("/dev/null", O_WRONLY);
	open("/dev/null", O_WRONLY);
	sprintf(buf, "-c %s", config_filename);
	execl("./server", "server", buf, (char *)0);
	DEBUG(TRACE_CHAOSD,  "exec of ./server failed\n");
	return 0;
}

static void
usage(void)
{
	fprintf(stderr, "usage: chaosd [OPTION]...\n");
	fprintf(stderr, "Chaosnet for Unix Daemon\n");
	fprintf(stderr, "\n");
	fprintf(stderr, "  -c FILE        configuration file (default: %s)\n", config_filename);
	fprintf(stderr, "  -n             don't start chaos server\n");
	fprintf(stderr, "  -s             run as a background daemon\n");
	fprintf(stderr, "  -h             help message\n");
}

int
main(int argc, char *argv[])
{
	int c;
	int fd;

	config_filename = "chaos.ini";
	daemon_flag = false;
	server_started = false;
	while ((c = getopt(argc, argv, "c:nsh")) != -1) {
		switch (c) {
		case 'c':
			config_filename = strdup(optarg);
			break;
		case 'n':
			/*
			 * Pretend that ./server is already started;
			 * so we don't start it.
			 */
			server_started = true;
			break;
		case 's':
			daemon_flag = true;
			break;
		case 'h':
			usage();
			exit(0);
		default:
			usage();
			exit(1);
		}
	}
	chcfg_init();
	if (ini_parse(config_filename, chcfg_handler, &chcfg) < 0)
		fprintf(stderr, "Can't load '%s', using defaults\n", config_filename);
	if (daemon_flag == true)
		daemonize(argv[0]);
	if (sig_init() == -1)
		errx(1, "sig_init");
	server_running = true;
	if (server_listen(&fd) ==-1)
		errx(1, "server_listen");
	fd_add_listen(fd, server_accept_upcall);
	DEBUG(TRACE_CHAOSD, "chaosd: starting server loop\n");
	while (server_running == true) {
		fd_poll();
		sig_poll();
		if (server_started == false) {
			server_started = true;
			DEBUG(TRACE_CHAOSD, "chaosd: started ./server...\n");
			start_serverd();
		}
	}
	exit(0);
}
