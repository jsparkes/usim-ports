#ifndef CHSOCKET_TRANSPORT_H
#define CHSOCKET_TRANSPORT_H

int fd_add_listen(int, int (*)(int));
int fd_add_reader(int, int (*)(int, void *, int), int(*)(int, void *, int), void *);
int fd_remove(int);
int fd_shutdown(int);
int fd_context_valid(int, int *, void **);
void fd_poll(void);

#endif
