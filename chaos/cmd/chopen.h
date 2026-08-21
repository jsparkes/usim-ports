#ifndef CHOPEN_H
#define CHOPEN_H

#ifdef SELECT
struct connection *chopen(int co_host, char *contact, int mode, int async, char *data, int dlength, int rwsize);
#else
int chopen(int co_host, char *contact, int mode, int async, char *data, int dlength, int rwsize);
#endif

#ifdef SELECT
struct connection *chlisten(char *contact, int mode, int async, int rwsize);
#else
int chlisten(char *contact, int mode, int async, int rwsize);
#endif

int chreject(int fd, char *string);
int chstatus(int fd, struct chstatus *chst);
int chwaitfornotstate(int fd, int state);
int chsetmode(int fd, int mode);

#endif
