# Global defines

# For running under GNU/Linux:
JOBS =
DBM =
DEFINES = -USYSLOG  -DCHAOS -DZEPHYR -Wno-return-type
# MAILDAEMON =
# MAILER = chaosmail

# For running under 4.2 BSD:
# JOBS =
# DBM =
# # no more XWIND - gdt 6/10/89
# DEFINES = -DBSD42 -DSYSLOG  -DCHAOS -DZEPHYR
# #DEFINES = -DBSD42 -DCHAOS 
# # MAILDAEMON =
# # MAILER = chaosmail

# For running under 4.1 BSD:
# JOBS = -ljobs
# DBM = -ldbm
# DEFINES =
# MAILDAEMON = chmailer
# MAILER = chmail

ROOT =
# where the system is built
SYSINCLUDES = /sys/ALLSPICE

# if the chaos includes are any where special
INCLUDES = ../h

# where the user programs go
DESTUSERS = $(ROOT)/usr/site

# where the servers go - this is carried into chserver
DESTSERVERS = $(ROOT)/usr/lib/chaos

# where the daemons goes
DESTDAEMONS = $(ROOT)/etc

# where the mail spooler goes
DESTMAILER = $(ROOT)/usr/lib/mailers

# where the host library lives
HOSTLIB = ../libhosts/libhosts.a

# where the host library headers live
HOSTINC = ../libhosts

# where the file server login file lives
FILEUTMP = /etc/chfile.utmp

# Major device number of CHAOS device driver what a bunch of crap
CHRMAJOR = 40  

SERVERS = NAME SEND FILE TELNET RTAPE SUPDUP SPICE TTYLINK RAMTEK \
	  LGP MAIL SMTP NETLOAD UU

# Servers that want to only read from the network
RSERVERS = SEND RAMTEK LGP

# Servers that want to only write to the network
WSERVERS = NAME NETLOAD

USERS = chread chosend chstat chup hostat chtelnet cftp chsupdup chfinger chtime \
	chtape prtable conn chaccept chwrite \
	chsend reply what newsends chrut


DAEMONS = chserver chinit cheaddr $(MAILDAEMON) chconnect

# PROGS = $(USERS) $(SERVERS) $(DAEMONS) $(MAILER) chmkdev
PROGS = FILE MINI
CFLAGS = -O -I$(INCLUDES) -I$(HOSTINC) -DDESTSERVERS=\"$(DESTSERVERS)\" \
	 -DFILEUTMP=\"$(FILEUTMP)\" -DDESTUSERS=\"$(DESTUSERS)\" \
	 -DCHRMAJOR=$(CHRMAJOR) $(DEFINES)

all: $(PROGS)

# chreject is a link to chaccept
chaccept: chaccept.o
	$(CC) -o $@ $@.o chopen.o

chread: chread.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chosend: chosend.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chmkdev:	chmkdev.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chstat:		chstat.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chwrite: chwrite.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

prtable:	prtable.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

conn:		conn.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chtelnet:	chtelnet.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

cftp:		cftp.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chsupdup:	chsupdup.o /usr/lib/libtermlib.a
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB) -ltermlib

chfinger:	chfinger.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

hostat:		hostat.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chup:		chup.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chtime:		chtime.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chrut:		chrut.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chtape:	chtape.o record.o
	$(CC) -o $@ $@.o record.o chopen.o $(HOSTLIB)

chtape.o:	chtape.c record.h RTAPE.h

MAIL:		MAIL.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB) $(DBM) TCP/netsubrs.o

NAME:		NAME.o
	$(CC) -o $@ $@.o chopen.o

NETLOAD:	NETLOAD.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

SEND:		SEND.o $(HOSTLIB)
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB) -lzephyr -lkrb -lcom_err

SMTP:		SMTP.o $(HOSTLIB)
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

TELNET:		TELNET.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

SUPDUP:		SUPDUP.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

TTYLINK:	TTYLINK.o
	$(CC) -o $@ $@.o chopen.o

RTAPE:		RTAPE.o record.o
	$(CC) -o $@ $@.o record.o chopen.o

RTAPE.o record.o: record.h

SPICE:	SPICE.o
	$(CC) -o $@ $@.o chopen.o

RAMTEK:	RAMTEK.o
	$(CC) -o $@ $@.o chopen.o

LGP:	LGP.o
	$(CC) -o $@ $@.o chopen.o

UU:	UU.o
	$(CC) -o $@ $@.o chopen.o

$(USERS): chopen.o $(HOSTLIB)

$(SERVERS): chopen.o

chstat.o: chstat.c
	$(CC) -c $(CFLAGS) -I$(SYSINCLUDES) -O chstat.c

prtable.o: prtable.c
	$(CC) -c $(CFLAGS) -I$(SYSINCLUDES) -O prtable.c

FILE.o glob.o: FILE.h

FILE: FILE.o glob.o chopen.o
	$(CC) -o $@ $@.o glob.o chopen.o $(HOSTLIB)

MINI: MINI.o chopen.o
	$(CC) -o $@ $@.o chopen.o $(HOSTLIB)

chserver: chserver.o chopen.o
	$(CC) -o chserver chserver.o chopen.o $(HOSTLIB) $(JOBS)

chinit: chinit.o
	$(CC) -o chinit chinit.o $(HOSTLIB)

cheaddr: cheaddr.o
	$(CC) -o cheaddr cheaddr.o

chmailer: chmailer.o
	$(CC) -o chmailer chmailer.o chopen.o $(HOSTLIB)

chmail: chmail.o
	$(CC) -o chmail chmail.o $(HOSTLIB)

chaosmail: chaosmail.o
	$(CC) -o chaosmail chaosmail.o chopen.o $(HOSTLIB)

chconnect: chconnect.o
	$(CC) -o chconnect chconnect.o chopen.o $(HOSTLIB)

		
chsend:	chsend.o sendsys.o chopen.o 
	cc -o chsend chsend.o sendsys.o chopen.o $(HOSTLIB)

reply:	reply.o sendsys.o sendtxt.o chopen.o 
	cc -o reply reply.o sendsys.o sendtxt.o chopen.o $(HOSTLIB)

what:		what.o sendtxt.o
		cc -o what what.o sendtxt.o

webster:	webster.o
		cc -o $@ $@.o $(HOSTLIB)

newsends:	newsends.o
		cc -o newsends newsends.o


install:	$(PROGS)
	-for i in $(SERVERS); do install -c -s -m 775 $$i $(DESTSERVERS)/$$i; done
	-for i in  $(USERS); do install -c -s -m 755 $$i $(DESTUSERS)/$$i; done
#	-rm $(DESTUSERS)/tn
#	ln $(DESTUSERS)/chtelnet $(DESTUSERS)/chtn
#	ln $(DESTUSERS)/chfinger $(DESTUSERS)/f
	-cd $(DESTDAEMONS); rm -f $(DAEMONS)
	-for i in $(DAEMONS); do install -c -s -o root -m 755 $$i $(DESTDAEMONS)/$$i; done
#	install -s -m 755 -o network $(MAILER) $(DESTMAILER)/$(MAILER)
	cd $(DESTSERVERS); chmod 775 $(SERVERS); chmod 755 $(RSERVERS); chmod 735 $(WSERVERS); strip $(SERVERS)


clean:
	rm -f $(PROGS) *.o

special: chmkdev
	chmkdev | sh
	chmod 666 /dev/ch*
