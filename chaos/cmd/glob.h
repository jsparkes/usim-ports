#ifndef GLOB_H
#define GLOB_H

char **glob(char *v);
int letter(char c);
int digit(char c);
int any(int c, char *s);
int blklen(char **av);
char **blkcpy(char **oav, char **bv);
char *blkfree(char **av0);
char **copyblk(char **v);
int gethdir(char *home);

#endif
