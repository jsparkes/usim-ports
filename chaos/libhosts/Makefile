CFLAGS = -std=gnu99 -Wall -Wextra -ggdb3 -O0

bindir = /usr/bin
sysconfdir = /etc
includedir = /usr/include
libdir = /usr/lib

all: libhosts.a chaosnames chaosname chaosaddr

hosts: hosts.global hosts.site hosts.local
	-cat hosts.global hosts.site hosts.local > hosts

libhosts.a: hostlib.o readhosts.o hosttab.h
	ar rcs libhosts.a hostlib.o readhosts.o

hosts2: hosts2.o libhosts.a

chaosnames: chaosnames.o libhosts.a

chaostables: chaosnames hosts chaoshosts

chaoshosts: chaosnames
	./chaosnames `cat whoami` > chaoshosts

chaosaddr: chaosaddr.o libhosts.a
chaosname: chaosname.o libhosts.a

$(libdir)/libhosts.a: libhosts.a
	cp libhosts.a $(libdir)/libhosts.a
	ranlib $(libdir)/libhosts.a
	chmod 644 $(libdir)/libhosts.a

install: all $(libdir)/libhosts.a
	install -c -m 644 hosttab.h $(includedir)
	install	-c -m 705 chaosnames $(bindir)
	install	-c -m 705 chaosaddr $(bindir)
	install	-c -m 705 chaosname $(bindir)
	install -c -m 644 chaoshosts $(sysconfdir)

check:
	cp hosts.sample hosts.global
	touch hosts.site
	touch hosts.local
	echo "SCRC-VAX" > whoami
	$(MAKE) chaostables

clean:
	rm -f *.o
	rm -f hosts
	rm -f hosttab.c
	rm -f libhosts.a
	rm -f chaosnames
	rm -f chaosname
	rm -f chaosaddr
	rm -f hosts.global hosts.local hosts.site whoami
