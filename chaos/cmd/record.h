#include <stdio.h>

#define RECMAGIC "RECORD STREAM VERSION 1\215"

struct record_stream {
	FILE *r_rfp;
	FILE *r_wfp;
	int r_rlen;
	int r_wlen;
};

extern struct record_stream *recopen(int, int);
extern void recforce(struct record_stream *);
extern void recclose(struct record_stream *);
extern int recop(struct record_stream *);
extern int reclength(struct record_stream *);
extern int recread(struct record_stream *, char *, int);
extern int recchar(struct record_stream *);
extern int recstart(struct record_stream *, int, int);
extern int recwchar(struct record_stream *, int);
extern int recwrite(struct record_stream *, int, char *, int);
extern void recerr(char *);
extern int recrfileno(struct record_stream *);
extern int recwfileno(struct record_stream *);
