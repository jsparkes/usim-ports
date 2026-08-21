INCLUDES = -I. -I../h -I.. -I../cmd

# where the file server login file lives
FILEUTMP = /tmp/chfile.utmp

DEFINES = -DSELECT=1 -DUSE_CBRIDGE=1 -DFILEUTMP=\"$(FILEUTMP)\" -DMAP_ROOT_DIRECTORY=1 -DPRIVHOSTS=0
# Debug version
# DEFINES = -DSELECT=1 -DUSE_CBRIDGE=1 -DFILEUTMP=\"$(FILEUTMP)\" -DMAP_ROOT_DIRECTORY=1 -DPRIVHOSTS=0 -DCBDEBUG=1
# -DDMALLOC 

CFLAGS = -g -Wall $(INCLUDES) $(DEFINES)

OBJS = cbFILE.o cbncp.o chutil.o chopen.o FILE.o glob.o
SRCS = ../chncp/chutil.c ../chncp/chncp.h ../cmd/chopen.c ../cmd/FILE.c ../cmd/glob.c cbncp.h cbncp.c cbFILE.c hosttab.h Makefile

cbFILE: $(OBJS)
	$(CC) $(CFLAGS) -o $@ $(OBJS) -pthread

dmalloc: $(OBJS)
	$(CC) $(CFLAGS) -o $@ $(OBJS) -pthread -ldmallocth

chutil.o: ../chncp/chutil.c
	$(CC) -c $(CFLAGS) -o $@ $<

chopen.o: ../cmd/chopen.c
	$(CC) -c $(CFLAGS) -o $@ $<

FILE.o: ../cmd/FILE.c
	$(CC) -c $(CFLAGS) -o $@ $<

glob.o: ../cmd/glob.c
	$(CC) -c $(CFLAGS) -o $@ $<

tar:	$(SRCS)
	tar czvf cbFILE.tar.gz $(SRCS)

clean:
	rm -f $(OBJS) cbFILE dmalloc
