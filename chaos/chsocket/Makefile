CFLAGS = -O0 -ggdb3 -Wall -Wextra -fno-builtin-log -I../h -I../chncp -I../chunix -I.. -I. -I../chcfg -I../libhosts

CHNCP_OBJS = ../chncp/chclock.o ../chncp/chsend.o ../chncp/chutil.o ../chncp/chuser.o ../chncp/chrcv.o
CHUNIX_OBJS = ../chunix/chaos.o ../chunix/challoc.o ../chunix/chconf.o ../chunix/chtime.o
CHCFG_OBJS = ../chcfg/ini.o ../chcfg/trace.o ../chcfg/chcfg.o

all: chaosd listen server client time

../libhosts/libhosts.a:
	$(MAKE) -C ../libhosts all

chaosd: chaosd.o transport.o node.o sig.o misc.o $(CHCFG_OBJS)  ../libhosts/libhosts.a
server: server.o testpackets.o misc.o chdopen.o $(CHCFG_OBJS) sig.o $(CHUNIX_OBJS) $(CHNCP_OBJS) ../libhosts/libhosts.a
listen: listen.o misc.o chdopen.o ../libhosts/libhosts.a

# Debugging utilities.
client: client.o misc.o chdopen.o
time: time.o misc.o chdopen.o

clean:
	rm -f *.o chaosd listen server client time
	rm -f $(CHUNIX_OBJS) $(CHNCP_OBJS) $(CHCFG_OBJS)
