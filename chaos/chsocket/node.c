/* node.c -- keep track of client nodes which have connected
 */

#include <sys/uio.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "misc.h"
#include "node.h"
#include "trace.h"
#include "transport.h"

#define MAX_NODES	10

int node_count;
struct node *nodes[MAX_NODES];

int
node_new(struct node **pnode)
{
	struct node *node;

	if (node_count >= MAX_NODES)
		return -1;
	node = (struct node *) malloc(sizeof(struct node));
	if (node == 0)
		return -1;
	memset((char *)node, 0, sizeof(struct node));
	DEBUG(TRACE_CHAOSD, "node_new() - node_count = %d, new node = %p\n", node_count, node);
	node->index = node_count;
	nodes[node_count++] = node;
	*pnode = (void *)node;
	return 0;
}

void
node_destroy(struct node *node)
{
	int i;

	i = node->index;
	DEBUG(TRACE_CHAOSD, "node_destroy(node = %p) - removing node->index = %d\n", node, i);
	nodes[i] = 0;
	/*
	 * If it's not the last one, pull up the vector.
	 */
	for (; i < node_count - 1; i++) {
		nodes[i] = nodes[i + 1];
		nodes[i]->index = i;
	}
	free((char *)node);
	node_count--;
}

/*
 * Callback routine called when an socket is closed down; resets the
 * state of the client object and erases the client.  Returns -1 on
 * error.
 */
int
node_close(int fd, void *node, int context)
{
	int xfd;
	void *pnode;

	DEBUG(TRACE_CHAOSD, "node_close(fd = %d, node = %p)\n", fd, node);
	/*
	 * Don't need to close the fd - transport.c does this.
	 */
	if (fd_context_valid(context, &xfd, &pnode))
		return -1;
	node_destroy((struct node *) node);
	return 0;
}

void
node_set_fd(struct node *node, int fd)
{
	node->fd = fd;
}

/* 
 * Read a message serialized on a stream.
 */
int
node_stream_reader(int fd, void *void_node, int context)
{
	int i;
	int ret;
	int size;
	int  op;
	unsigned long len;
	unsigned char lenbytes[4];
	unsigned char msg[4096];
	struct iovec iov[2];

	DEBUG(TRACE_CHAOSD, "node_stream_reader(fd = %d)\n", fd);
	ret = read(fd, lenbytes, 4);
	if (ret <= 0) {
		DEBUG(TRACE_CHAOSD, "node_stream_reader() - read header error (ret = %d)\n", ret);
		return -1;
	}
	if (ret != 4) {
		DEBUG(TRACE_CHAOSD, "node_stream_reader() - length data error (ret %d != 4): %04X %04X %04X %04X\n", ret, lenbytes[0], lenbytes[1], lenbytes[2], lenbytes[3]);
		return -1;
	}
	len = (lenbytes[0] << 8) | lenbytes[1];
	ret = read(fd, msg, len);
	if (ret <= 0) {
		DEBUG(TRACE_CHAOSD, "node_stream_reader() - read data error (ret = %d)\n", ret);
		return -1;
	}
	if (ret != len) {
		DEBUG(TRACE_CHAOSD, "node_stream_reader()- length data error (ret %d != len %d): %04X %04X %04X %04X ...\n", ret, len, lenbytes[0], lenbytes[1], lenbytes[2], lenbytes[3]);
		return -1;
	}
	size = ret;
	DEBUG(TRACE_CHAOSD, "node_stream_reader() - node_count = %d\n", fd, node_count);
	op = msg[0] | (msg[1] << 8);
	DEBUG(TRACE_CHAOSD, "node_stream_reader() - op = %04x, msg = %02x%02x%02x%02x%02x%02x%02x%02x\n", op, msg[16], msg[17], msg[18], msg[19], msg[20], msg[21], msg[22], msg[23]);
	lenbytes[2] = 1;
	lenbytes[3] = 0;
	iov[0].iov_base = lenbytes;
	iov[0].iov_len = 4;
	iov[1].iov_base = msg;
	iov[1].iov_len = size;
	for (i = 0; i < node_count; i++) {
		DEBUG(TRACE_CHAOSD, "node_stream_reader() - [%d] %x %x, fd %d\n", i, void_node, (void *)nodes[i], nodes[i] ? nodes[i]->fd : -1);
#if 0
		if (void_node == (void *)nodes[i])
			continue;
#endif
		ret = writev(nodes[i]->fd, iov, 2);
		if (ret < 0) {
			DEBUG(TRACE_CHAOSD, "node_stream_reader() - writev(d = %d) -> %d, size = %d\n", nodes[i]->fd, ret, size);
			perror("writev");
		}
		DEBUG(TRACE_CHAOSD, "node_stream_reader() - send to fd = %d (ret = %d)\n", nodes[i]->fd, ret);
	}
	return 0;
}
