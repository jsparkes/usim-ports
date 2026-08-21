#ifndef CHSOCKET_NODE_H
#define CHSOCKET_NODE_H

struct node {
	int fd;
	int index;
};

int node_new(struct node **);
void node_destroy(struct node *);
int node_close(int, void *, int);
void node_set_fd(struct node *, int);
int node_stream_reader(int, void *, int);

#endif
