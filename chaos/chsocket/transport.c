/* transport.c -- listen to fd's using poll(2)
 *
 * Simple machinery to listen to a list of fd's via poll() and
 * dispatch when something needs to be done.
 *
 * Main poll routine (fd_poll) is called from main server loop.
 */

#include <sys/poll.h>

#include <unistd.h>

#include "chaosd.h"
#include "trace.h"

#define MAX_SERVER_FDS	32

int fd_count;
unsigned short fd_generation;

struct {
	int fd;
	int shutdown;
	void *server;
	int generation;
	int context;
	int (*read_func)(int fd, void *server, int context);
	int (*close_func)(int fd, void *server, int context);
	int (*accept_func)(int fd);
} fd_list[MAX_SERVER_FDS];

/*
 * Add an fd to the list of fd's we're listening to.  Returns the
 * index into fd_list on success, or -1 on error.
 */
static int
fd_add(int fd)
{
	int i;

	DEBUG(TRACE_CHAOSD, "fd_add(fd=%d)\n", fd);
	for (i = 0; i < MAX_SERVER_FDS; i++)
		if (fd_list[i].fd == 0)
			break;
	if (i == MAX_SERVER_FDS)
		return -1;
	fd_list[i].fd = fd;
	fd_list[i].server = (void *)0;
	if (fd_generation == 0)
		fd_generation = 1;
	fd_list[i].generation = fd_generation;
	fd_list[i].context = (fd_generation++ << 16) | i;
	fd_count++;
	DEBUG(TRACE_CHAOSD, "fd_add(fd=%d) index %d\n", fd, i);
	return i;
}

/*
 * Add an fd which is listening via listen().
 */
int
fd_add_listen(int fd, int (*accept_func)(int fd))
{
	int index;

	index = fd_add(fd);
	if (index < 0)
		return index;
	fd_list[index].accept_func = accept_func;
	return index;
}

/*
 * Add an fd which is readable.
 */
int
fd_add_reader(int fd, int (*read_func)(int fd, void *server, int context), int(*close_func)(int fd, void *server, int context), void *server)
{
	int index;

	index = fd_add(fd);
	if (index < 0)
		return index;
	fd_list[index].read_func = read_func;
	fd_list[index].close_func = close_func;
	fd_list[index].server = server;
	return index;
}

/*
 * Remove an fd from the active list.
 */
int
fd_remove(int fd)
{
	int i;

	for (i = 0; i < MAX_SERVER_FDS; i++)
		if (fd_list[i].fd == fd)
			break;
	if (i == MAX_SERVER_FDS)
		return -1;
	fd_count--;
	fd_list[i].fd = 0;
	fd_list[i].generation = 0;
	return 0;
}

/*
 * Mark an fd for orderly shutdown.
 */
int
fd_shutdown(int fd)
{
	int i;

	for (i = 0; i < MAX_SERVER_FDS; i++)
		if (fd_list[i].fd == fd)
			break;
	if (i == MAX_SERVER_FDS)
		return -1;
	fd_list[i].shutdown = 1;
	return 0;
}

/*
 * If given context is valid, return fd, else error.
 *
 * The 'context' is passed around in queued messages this routine
 * keeps us from doing something bad if a socket is shut down when
 * messages are still in flight.
 */
int
fd_context_valid(int context, int *pfd, void **pserver)
{
	int index, gen;

	gen = context >> 16;
	index = context & 0xffff;
	if (index < 0 || index > MAX_SERVER_FDS)
		return -1;
	if (fd_list[index].generation != gen)
		return -1;
	*pfd = fd_list[index].fd;
	*pserver = fd_list[index].server;
	return 0;
}

/*
 * Close down fd in an orderly way.
 */
static int
fd_close(int index)
{
	fd_list[index].shutdown = 0;
	if (fd_list[index].close_func) {
		DEBUG(TRACE_CHAOSD, "fd_close(index = %d)\n", index);
		(*fd_list[index].close_func) (fd_list[index].fd, fd_list[index].server, fd_list[index].context);
	}
	close(fd_list[index].fd);
	fd_remove(fd_list[index].fd);
	return 0;
}

/*
 * If the fd is readable; do the right thing.
 */
static int
fd_read(int index)
{
	int ret;

	ret = 0;
	DEBUG(TRACE_CHAOSD, "fd_read(index=%d)\n", index);
	if (fd_list[index].accept_func) {
		DEBUG(TRACE_CHAOSD, "fd_read() - calling accept function\n");
		ret = (*fd_list[index].accept_func) (fd_list[index].fd);
	}
	if (fd_list[index].read_func) {
		DEBUG(TRACE_CHAOSD, "fd_read() - calling read function\n");
		ret = (*fd_list[index].read_func) (fd_list[index].fd, fd_list[index].server, fd_list[index].context);
	}
	/*
	 * If reader or acceptor returns an error, shut down the
	 * socket.
	 */
	if (ret)
		fd_close(index);
	return 0;
}

static int
fd_except(int index)
{
	DEBUG(TRACE_CHAOSD, "fd_except(index=%d)\n", index);
	return 0;
}

/*
 * Main polling routine; pass a list of fd's to poll() and dispatch.
 */
void
fd_poll(void)
{
	int ret;
	int fd_count;
	int timeout;
	struct pollfd ufds[MAX_SERVER_FDS];
	int ufds_index[MAX_SERVER_FDS];

	/*
	 * Build up list of file descriptors.
	 */
	fd_count = 0;
	for (int i = 0; i < MAX_SERVER_FDS; i++) {
		if (fd_list[i].fd == 0)
			continue;
		ufds_index[fd_count] = i;
		ufds[fd_count].fd = fd_list[i].fd;
		ufds[fd_count].events = POLLIN | POLLPRI | POLLERR | /*POLLHUP | */ POLLNVAL;
		ufds[fd_count].revents = 0;
		fd_count++;
	}
	timeout = 1 * 1000;
	DEBUG(TRACE_CHAOSD, "fd_poll() - fd_count = %d\n", fd_count);
	/*
	 * Wait for I/O from the list of file descriptors.
	 */
	ret = poll(ufds, fd_count, timeout);
	if (ret < 0)
		DEBUG(TRACE_CHAOSD, "fd_poll() - poll() returned %d\n", ret);
	if (ret == 0) {
		DEBUG(TRACE_CHAOSD, "fd_poll() - timeout\n");
		return;
	}
	/*
	 * Ret > 0; process I/O.
	 */
	for (int i = 0; i < MAX_SERVER_FDS && ret > 0; i++) {
		if (ufds[i].revents == 0)
			continue;
		DEBUG(TRACE_CHAOSD, "fd_poll() - ufds[%d].revents 0x%x\n", i, ufds[i].revents);
		if (ufds[i].revents & ~(POLLIN | POLLHUP)) {
			fd_except(ufds_index[i]);
			ret--;
		}
		if (ufds[i].revents & POLLIN) {
			fd_read(ufds_index[i]);
			ret--;
		}
	}
	/*
	 * See if any fd's want to be shut down.
	 */
	for (int i = 0; i < MAX_SERVER_FDS; i++) {
		if (fd_list[i].shutdown)
			fd_close(i);
	}
}
