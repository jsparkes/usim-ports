# where the host library lives
HOSTLIB = /usr/lib/libhosts.a

# Global flags:
#
#   CHAOS   -> Chaos support is to be included
#   BSD42   -> Machine is running 4.2 BSD
#
DEFINES = -DCHAOS -DBSD42
all:	send reply what SEND newsends

send:	send.o sendsys.o chopen.o 
	cc -o send send.o sendsys.o chopen.o $(HOSTLIB)

send.o: send.c
	cc $(DEFINES) -c send.c

reply:	reply.o sendsys.o sendtxt.o chopen.o 
	cc -o reply reply.o sendsys.o sendtxt.o chopen.o $(HOSTLIB)

reply.o:	reply.c
		cc $(DEFINES) -c reply.c 

what:		what.o sendtxt.o
		cc -o what what.o sendtxt.o

what.o:		what.c
		cc $(DEFINES) -c what.c

sendsys.o:	sendsys.c
		cc $(DEFINES) -c sendsys.c

sendtxt.o:	sendtxt.c
		cc $(DEFINES) -c sendtxt.c

newsends:	newsends.o
		cc -o newsends newsends.o

newsends.o:	newsends.c
		cc $(DEFINES) -c newsends.c

chopen.o:	chopen.c
		cc $(DEFINES) -c chopen.c

SEND:		SEND.o 
		$(CC) -o $@ $@.o chopen.o $(HOSTLIB)
