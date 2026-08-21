#ifndef CHSOCKET_SIG_H
#define CHSOCKET_SIG_H

int sig_init(void);
void sig_poll(void);

/* 
 *
 * The following functions must be defined by anything that uses
 * sig.c.  
 */
extern void server_shutdown(void);
extern void restart_child(void);

#endif
