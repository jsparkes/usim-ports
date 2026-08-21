/* Copyright © 2023-2024 Björn Victor (bjorn@victor.se) */
/* Code to make FILE.c with defined SELECT run with cbridge instead of "local", in-memory Chaos. */
/*
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

#ifndef CBDEBUG
#define CBDEBUG 0
#endif

#include <sys/socket.h>
#include <sys/types.h>
#include <sys/un.h>
#include <errno.h>
// standards, who knows which we're using
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <sys/time.h>
#include <unistd.h>
#include <pthread.h>

// opcodes etc
#include "chaos.h"
#define ACKOP 0177		/* cbridge specific */
// chlisten
#include "chopen.h"
// stuff we're not using, but needed for chncp.h
#include "chunix/chconf.h"
// struct connection, packet etc
#include "chncp/chncp.h"

#include "cbncp.h"

#ifdef DMALLOC
#include "dmalloc.h"
#endif

// Used for interrupting transfer thread (writing on the net)
extern pthread_t startxfer_thread;

// From chutil.c
extern struct packet *chaos_allocate_packet(struct connection *conn, int opcode, ssize_t len);

// fake
u_short uch11_myaddr = 03412;

// Table of our connections and cbridge sockets
static struct cb_conn_table 
{
  int skt;			/* socket to cbridge */
  struct connection *conn;	/* chaosnet conn, internally */
  char *contact;		/* nice to have */
  int is_main;		   /* whether this is the main command conn */
} conns[MAX_CBRIDGE_CONNS];

static void cb_init_conns()
{
  memset(conns, 0, sizeof(conns));
}

void cb_print_conns(int sig)
{
  fprintf(stderr,"%s: conns follow:\n", __func__);
  for (int i = 0; i < MAX_CBRIDGE_CONNS; i++) {
    if (conns[i].skt != 0)
      fprintf(stderr," %d: %p %d %s\n", i, conns[i].conn, conns[i].skt, conns[i].contact);
  }
}

static int cb_find_free_conn()
{
  for (int i = 0; i < MAX_CBRIDGE_CONNS; i++) {
    if (conns[i].skt == 0)
      return i;
  }
#if CBDEBUG
  fprintf(stderr,"%s: can not find free conn - existing conns follow:\n", __func__);
  cb_print_conns(0);
#endif
  return -1;
}

static int cb_find_conn(struct connection *conn)
{
  for (int i = 0; i < MAX_CBRIDGE_CONNS; i++) {
    if (conns[i].conn == conn) {
      if (conns[i].skt > 0)
	return i;
      else {
#if CBDEBUG
	fprintf(stderr,"%s: found conn %p but it is closed (skt %d)\n", __func__, conn, conns[i].skt);
#endif
	return -1;
      }
    }
  }
#if CBDEBUG
  fprintf(stderr,"%s: did not find conn %p - existing conns follow:\n", __func__, conn);
  for (int i = 0; i < MAX_CBRIDGE_CONNS; i++) {
    if (conns[i].skt != 0)
      fprintf(stderr," %p %d %s\n", conns[i].conn, conns[i].skt, conns[i].contact);
  }
#endif
  return -1;
}

static void cb_close_conn(int i)
{
#if CBDEBUG
  fprintf(stderr,"%s: closing conn %d %p %d %s\n", __func__, i, conns[i].conn, conns[i].skt, conns[i].contact);
#endif
  close(conns[i].skt);
  conns[i].skt = 0;
  if (conns[i].contact != NULL)
    free(conns[i].contact);
  // @@@@ I guess we want to release the conn while we're at it
#if 0 // No, let FILE do that
  struct connection *conn = conns[i].conn;
  conns[i].conn = NULL;
  rlsconn(conn);
#endif
}

static char *chop_names[] = {
  "NIL", "RFC", "OPN", "CLS", "FWD", "ANS", "SNS", "STS",
  "RUT", "LOS", "LSN", "MNT", "EOF", "UNC", "BRD"
};

static char *chop_name(uint c)
{
  if ((c > 0) && (c <= BRDOP))
    return chop_names[c];
  else if (c == ACKOP)
    return "ACK";
  else if (c >= 0300)
    return "DWD";
  else if (c == 0201)
    return "SYN";
  else if (c == 0202)
    return "ASYN";
  else if (c >= 0200)
    return "DAT";
  else
    return NULL;
}

// Open a packet socket to cbridge (done for each conn)
static int cbridge_open_socket(void)
{
  int slen, sock;
  struct sockaddr_un server;

  if ((sock = socket(AF_UNIX, SOCK_STREAM, 0)) < 0) {
    // Check for "out of sockets" or similar?
    fprintf(stderr, "%s: socket(AF_UNIX) error: %s\n",
	    __func__, strerror(errno)); // @@@@ handle error better?
    return sock;
  }
  server.sun_family  = AF_UNIX;
  sprintf(server.sun_path, "%s", CBRIDGE_PACKET_SOCKET);
  slen = strlen(server.sun_path) + 1 + sizeof ( server.sun_family );

  if (connect(sock, (struct sockaddr *)&server, slen) < 0) {
    fprintf(stderr,"%s: connect(%s) error: %s\n", __func__,
	    server.sun_path, strerror(errno));
    close(sock);
    sock = -1;
  }
  return sock;
}

// Convert a cbridge packet to a "fake" chaos packet
static struct packet *
cbridge_to_packet_data(u_char *cbuf, int pcnt, int copcode, int c) 
{
  int npklen = pcnt;
  // RFC, ANS, OPN, FWD: special data to parse
  if (copcode == RFCOP) {
    // It would be nice if the RFC data had the contact, even if it is redundant
    // New packet data: contact name + space + args
    npklen = strlen(conns[c].contact);
    char *sp = index((char *)cbuf, ' ');
    if (sp != NULL)		/* Any args? */
      npklen += strlen(sp);
  } else if (copcode == OPNOP) {
    // cbridge pkt contains foreign host, we ignore that for now
    npklen = 0;
  } else if (copcode == ANSOP) {
    u_short fhost = cbuf[0] | (cbuf[1] << 8);
    if (CH_ADDR_SHORT(conns[c].conn->cn_faddr) != fhost) {
      fprintf(stderr, "%s: unexpected foreign address in ANS: %#o expected %#o\n",
	      __func__, fhost, CH_ADDR_SHORT(conns[c].conn->cn_faddr));
    }
    npklen -= 2;		/* Adjust length */
  }
  // @@@@ todo: FWD
#if CBDEBUG
  fprintf(stderr,"%s: %s pkt len %d, new len %d\n", __func__, chop_name(copcode), pcnt, npklen);
#endif
  // Now we know how long the packet needs to be
  struct packet *pkt = chaos_allocate_packet(conns[c].conn, copcode, npklen);
  // Now patch in the data
  if (copcode == RFCOP) {
    u_short faddr;
    cbuf[pcnt] = '\0';
#if CBDEBUG
    fprintf(stderr,"%s: RFC \"%s\"\n", __func__, cbuf);
#endif
    if (sscanf((char *)cbuf, "%ho", &faddr) != 1) {
      fprintf(stderr,"%s: bad foreign address in received RFC: %s\n", __func__, cbuf);
      SET_CH_ADDR(conns[c].conn->cn_faddr,03412); /* amnesia */
    } else {
#if CBDEBUG
      fprintf(stderr,"%s: parsed foreign address \"%s\" to %#o\n", __func__, cbuf, faddr);
#endif
      SET_CH_ADDR(conns[c].conn->cn_faddr,faddr);
    }
    char *cp = pkt->pk_cdata;
    cp = memcpy(cp, conns[c].contact, strlen(conns[c].contact));
    char *sp = index((char *)cbuf, ' ');
    if (sp != NULL) {
      memcpy(cp+strlen(conns[c].contact), sp, npklen-strlen(conns[c].contact));
    }
  } else if (copcode == ANSOP) {
    // Skip fhost bytes, keep data
    memcpy(pkt->pk_cdata, &cbuf[2], npklen);
  } else if (copcode == OPNOP) {
    // data is remote host, octal address or FQDN
    // ignore for now, and nothing to copy
    // @@@@ todo: FWD
  } else {
    // All others, copy all the data
    memcpy(pkt->pk_cdata, cbuf, npklen);
  }
  return pkt;
}

// **** Here is the externally used code

// Receive a packet on a conn
struct packet * 
chaos_connection_dequeue(struct connection *conn)
{
  u_char cbuf[CH_PK_MAX_DATALEN + CBRIDGE_PACKET_HEADER_SIZE]; /* Fit data + cbridge header */

  int c = cb_find_conn(conn);
  if (c < 0) {
    fprintf(stderr,"%s: BUG: cannot find conn %p\n", __func__, conn);
    return NULL;
  }
  int skt = conns[c].skt;
  // Read a header.
  int cnt = read(skt, cbuf, CBRIDGE_PACKET_HEADER_SIZE);
  if (cnt != CBRIDGE_PACKET_HEADER_SIZE) {
    if (cnt < 0) 
      fprintf(stderr,"%s: read header error for conn %p: %s\n", __func__, conn, strerror(errno));
    else if (cnt != 0)
      fprintf(stderr,"%s: could not read cbridge header (%d bytes read)\n", __func__, cnt);
#if CBDEBUG
    else
      fprintf(stderr,"%s: read 0 bytes of header, assuming socket was closed\n", __func__);
#endif
    cb_close_conn(c);
    return NULL;
  }
  int copcode = cbuf[0];
  int mbz = cbuf[1];
  int clen = cbuf[2] | (cbuf[3] << 8);
  if ((mbz != 0) || ((copcode > BRDOP) && (copcode < ACKOP))
      || (clen > CH_PK_MAX_DATALEN)) {
    fprintf(stderr, "%s: cbridge header bad: opcode %#o (%s), mbz %d, len %d\n",
	    __func__, copcode, chop_name(copcode), mbz, clen);
    cb_close_conn(c);
    return NULL;
#if CBDEBUG
  } else {
    fprintf(stderr,"%s: %p cbridge header: %s (%#o) pkt len %d\n", __func__, conn, chop_name(copcode), copcode, clen);
#endif
  }
  int pcnt = read(skt, cbuf, clen);
  if (pcnt != clen) {
    if (pcnt < 0) 
      fprintf(stderr,"%s: read packet error for conn %p: %s\n", __func__, conn, strerror(errno));
    else if (pcnt != 0)
      fprintf(stderr,"%s: could not read %d bytes of packet (%d bytes read)\n", __func__, cnt, pcnt);
#if CBDEBUG
    else
      fprintf(stderr,"%s: read 0 bytes of packet, assuming socket was closed\n", __func__);
#endif
    cb_close_conn(c);
    return NULL;
  }
  // Phew, all went well! Now create a fake pkt header and put the data there.
  struct packet *pkt = cbridge_to_packet_data(cbuf, pcnt, copcode, c);
#if CBDEBUG
  if (pkt->pk_op != DATOP)
    fprintf(stderr,"%s: got %s pkt from %#o on conn %p\n", __func__, 
	    chop_name(copcode), CH_ADDR_SHORT(conn->cn_faddr), conn);
#endif
  return pkt;
}

// Interrupt a conn, forcing it to flush input and packets in input window
void
chaos_interrupt_connection(struct connection *conn)
{
  // signal the xferthread if there is one, cf chaos_connection_queue
  int err;
  char ebuf[256];
#if CBDEBUG
  fprintf(stderr,"%s: %p unlocking interruption mutex [%p]\n",__func__, conn, (void *)pthread_self());
#endif
  if ((err = pthread_mutex_unlock(&conn->interruption)) != 0) {
    strerror_r(err, ebuf, sizeof(ebuf));
    fprintf(stderr,"%s: %p unlock interruption mutex failed: %s\n", 
	    __func__, conn, ebuf);
  }
}

// Checking for a chaos_interrupt_connection condition.
// We must avoid hanging on write on a full socket,
// so use select() with a timeout, and check if we're interrupted
// both in the case of a timeout and if we can write.
static int 
chaos_check_for_interrupt(int skt, struct connection *conn) 
{
  int sval;
  struct timeval timeout;
  fd_set fds;

  while (1) { 
    FD_ZERO(&fds);
    FD_SET(skt, &fds);
    timeout.tv_sec = 0;
    timeout.tv_usec = 200;	// Short timeout - enough?
    // Check if we can write on the socket
    sval = select(skt+1, NULL, &fds, NULL, &timeout);
    if ((sval == EINTR) || ((sval == 0) && !FD_ISSET(skt, &fds))) {
      // Timeout - we can't write. Check if we're interrupted.
      if (pthread_mutex_trylock(&conn->interruption) == 0) {
	// we could lock it, so we're interrupted. Drop the packet, return -2 to stop sending.
	// Caller (in dowork) should notice, send a SYNC, call xflush, and be done.
#if CBDEBUG
	fprintf(stderr,"%s: %p interrupted (timeout) [%p]\n",__func__, conn, (void *)pthread_self());
#endif
	pthread_mutex_unlock(&conn->interruption);
	return -2;
      } else {
	// Lock is busy
	continue;		// Not interrupted, try again
      }
    } else if (FD_ISSET(skt, &fds)) {
      // OK to write, but first check if we're interrupted.
      if (pthread_mutex_trylock(&conn->interruption) == 0) {
#if CBDEBUG
	fprintf(stderr,"%s: %p interrupted [%p].\n",__func__, conn, (void *)pthread_self());
#endif
	pthread_mutex_unlock(&conn->interruption);
	return -2;
      }
      break;			// Go ahead and write
    } else {
      fprintf(stderr,"%s: error in select(): %s\n", __func__, strerror(errno));
      break;			// Catch error below?
      // return -1;
    }
  }
  return 0;
}

int
chaos_clear_connection_interrupt(struct connection *conn)
{
  int c = cb_find_conn(conn);
  if (c < 0) {
#if CBDEBUG
    fprintf(stderr,"%s: can't find conn %p, punting\n", __func__, conn);
#endif
    return -1;
  }
  if (conns[c].conn != NULL) {
#if CBDEBUG
    fprintf(stderr,"%s: reinitializing interruption mutex for %p [%p]\n", __func__, conns[c].conn, (void *)pthread_self());
#endif
    if (pthread_mutex_init(&conns[c].conn->interruption, NULL) != 0) {
      perror("pthread_mutex_init");
      abort();
    }
    if (pthread_mutex_lock(&conns[c].conn->interruption) != 0) {
      perror("pthread_mutex_lock");
      abort();
    }
    return 0;
  } else {
    fprintf(stderr,"%s: conn %p doesn't have a conn?\n", __func__, conn);
    return -1;
  }
}


// Send a packet on a conn. Frees the packet.
int
chaos_connection_queue(struct connection *conn, struct packet *packet)
{
  u_char cbuf[CH_PK_MAX_DATALEN + CBRIDGE_PACKET_HEADER_SIZE]; /* Fit data + cbridge header */

  int c = cb_find_conn(conn);
  if (c < 0) {
    fprintf(stderr,"%s: BUG: cannot find conn %p\n", __func__, conn);
    return -1;
  }
  int copcode = packet->pk_op;
  int plen = PH_LEN(packet->pk_phead);

  switch (copcode) {
  case FWDOP:
    // These should never be sent to cbridge
  case RUTOP:
  case MNTOP:
  case UNCOP:
  case STSOP:
  case SNSOP:
    // Do nothing.
#if CBDEBUG
    fprintf(stderr,"%s: ignoring %s pkt len %d\n", __func__, chop_name(copcode), plen);
#endif
    return 0;
  }

  if (copcode == RFCOP) {
    // Different data for RFC
#if CBDEBUG
    fprintf(stderr,"%s: %p making RFC for host %#o contact \"%s\"\n",__func__, conn,
	    CH_ADDR_SHORT(conn->cn_faddr), packet->pk_cdata);
#endif
    char *cp = (char *)&cbuf[4];
    cp += sprintf(cp, "%o ", CH_ADDR_SHORT(conn->cn_faddr));
    memcpy(cp, packet->pk_cdata, plen);
    plen += cp-(char *)&cbuf[4];
  } else {
#if CBDEBUG
    fprintf(stderr,"%s: %p making %s len %d\n",__func__, conn, chop_name(copcode), plen);
#endif
    memcpy(&cbuf[4], packet->pk_cdata, plen);
  }
  cbuf[0] = copcode;
  cbuf[1] = 0;
  cbuf[2] = plen & 0xff;
  cbuf[3] = (plen >> 8) & 0xff;
	
#if CBDEBUG
  fprintf(stderr,"%s: sending %s len %d for conn %p\n", __func__, chop_name(copcode), plen, conn);
#endif
  if ((conns[c].is_main == 0) && pthread_equal(pthread_self(), startxfer_thread)) {
    // Only do this in the xfer thread (and only when writing to the net, but we can't know that here)
    // and when we're not writing a sync mark
    int intcheck = chaos_check_for_interrupt(conns[c].skt, conns[c].conn);
    if ((intcheck != 0)  && !((copcode == DATOP+1) && (plen == 0))) {
#if CBDEBUG
      fprintf(stderr,"%s: dropping %s pkt (%#o) len %d for conn %p\n", __func__, chop_name(copcode), copcode, plen, conn);
#endif
      free(packet);
      return intcheck;
    } else if (intcheck != 0) {
#if CBDEBUG
      fprintf(stderr,"%s: interrupted but allowing write of %s\n", __func__, chop_name(copcode));
#endif
    }
  }
  int wval = write(conns[c].skt, cbuf, plen+CBRIDGE_PACKET_HEADER_SIZE);
  if (wval != plen+CBRIDGE_PACKET_HEADER_SIZE) {
    if (wval < 0) 
      fprintf(stderr,"%s: write error for conn %p: %s\n", __func__, conn, strerror(errno));
    else
      fprintf(stderr,"%s: could not write cbridge packet (%d bytes written)\n", __func__, wval);
    cb_close_conn(c);
    return -1;
  }
#if CBDEBUG
  if (packet->pk_op != DATOP)
    fprintf(stderr,"%s: sent %s for conn %p\n", __func__, chop_name(copcode), conn);
#endif
  free(packet);		       /* Done with that */
  // @@@@ what about EOF waiting for ACK?
  // It seems FILE.c doesn't check for acks, just waits for the next command after sending EOF.
  return 0;
}

// To be used by chopen() in chopen.c, instead of chopen_conn.
// Allocate a conn, open a cbridge socket,
// send an RFC or LSN, wait for response.
struct connection *
chopen_cbridge(struct chopen *rfc, int mode)
{
  struct packet *answer;

  if (rfc->co_host == 0) {
    fprintf(stderr,"%s: BAD CALL: please use cb_listen_and_fork\n", __func__);
    return NULL;
  }

  int c = cb_find_free_conn();
  if (c < 0) {
    fprintf(stderr,"%s: can not find free conn!\n", __func__);
    return NULL;
  }
  struct connection *conn = connalloc(); /* allconn inits locking and queues */
  int skt = cbridge_open_socket();
  if (skt < 0) {
    fprintf(stderr,"%s: can not open socket to cbridge - is it running?\n", __func__);
    sleep(10);
    return NULL;
  }
  conns[c].skt = skt;
  conns[c].conn = conn;
  conns[c].contact = strdup(rfc->co_contact); /* Nice to keep */
  conns[c].is_main = 0;
#if CBDEBUG
  fprintf(stderr,"%s: initializing interruption mutex for %p [%p]\n", __func__, conns[c].conn, (void *)pthread_self());
#endif
  if (pthread_mutex_init(&conns[c].conn->interruption, NULL) != 0) {
    perror("pthread_mutex_init");
    abort();
  }
  if (pthread_mutex_lock(&conns[c].conn->interruption) != 0) {
    perror("pthread_mutex_lock");
    abort();
  }

#if CBDEBUG
  fprintf(stderr,"%s: host %#o contact \"%s\"\n", __func__, rfc->co_host, rfc->co_contact);
#endif

  SET_CH_ADDR(conn->cn_faddr, rfc->co_host); /* for below and _queue to use */
  struct packet *pkt = chaos_allocate_packet(conn, RFCOP, strlen(rfc->co_contact));

  // Both RFC and LSN need this
  memcpy(pkt->pk_cdata, rfc->co_contact, strlen(rfc->co_contact));

  if (rfc->co_host == 0) {
    // Listen
    pkt->pk_op = LSNOP;		/* change of mind */
    // send it off with chaos_connection_queue
    chaos_connection_queue(conn, pkt);
    conn->cn_state = CSLISTEN;
    // get an RFC/CLS/LOS answer with chaos_connection_dequeue,
    answer = chaos_connection_dequeue(conn);
    if (answer == NULL) {
      fprintf(stderr,"%s: null answer\n", __func__);
      return NULL;
    } 
#if CBDEBUG
    else 
      fprintf(stderr, "%s: got %s from %#o on conn for %#o\n", __func__,
	      chop_name(answer->pk_op), CH_ADDR_SHORT(answer->pk_saddr), CH_ADDR_SHORT(conn->cn_faddr));
#endif
    if (answer->pk_op != RFCOP) {
      conn->cn_state = CSLOST;
      free(answer);
      return conn;
    }
    conn->cn_state = CSRFCRCVD;
    // free() the answer
    free(answer);
    // send OPN
    answer = chaos_allocate_packet(conn, OPNOP, 2 * sizeof(unsigned short));
    chaos_connection_queue(conn, answer);
    conn->cn_state = CSOPEN;
    return conn;
  } else {
    // send off the RFC with chaos_connection_queue
    chaos_connection_queue(conn, pkt);
    conn->cn_state = CSRFCSENT;
    // receive a response (ANS/OPN/CLS/LOS) with chaos_connection_dequeue,
    answer = chaos_connection_dequeue(conn);
    if (answer == NULL) {
      fprintf(stderr,"%s: null answer\n", __func__);
      return NULL;
    } 
#if CBDEBUG
    else
      fprintf(stderr, "%s: got %s from %#o on conn for %#o\n", __func__,
	      chop_name(answer->pk_op), CH_ADDR_SHORT(answer->pk_saddr), CH_ADDR_SHORT(conn->cn_faddr));
#endif
    if ((answer->pk_op == LOSOP) || (answer->pk_op == CLSOP))
      conn->cn_state = CSLOST;
    else
      conn->cn_state = CSOPEN;
    // free() the response
    free(answer);
    return conn;
  }
}

// Dummies
void *host_data = NULL;
void readhosts(char *whoami, char *hoststable)
{}
// Non-safe, but doesn't need to be - only used "functionally"
char *chaos_name(short addr) 
{
  static char name[16];
  sprintf(name,"%#o",addr);
  return name;
}

int cb_listen_and_fork(char *contact, int dofork)
{
  int skt = cbridge_open_socket();
  if (skt < 0) {
    fprintf(stderr,"%s: can not open socket to cbridge - is it running?\n", __func__);
    return -1;
  }
  u_char cbuf[CBRIDGE_PACKET_HEADER_SIZE+CH_PK_MAX_DATALEN];
  int plen = strlen(contact);
  cbuf[0] = LSNOP;
  cbuf[1] = 0;
  cbuf[2] = plen & 0xff;
  cbuf[3] = (plen >> 8) & 0xff;
  memcpy(&cbuf[4], contact, plen);
  
  int wval = write(skt, cbuf, plen+CBRIDGE_PACKET_HEADER_SIZE);
  if (wval != plen+CBRIDGE_PACKET_HEADER_SIZE) {
    if (wval < 0) 
      fprintf(stderr,"%s: write error for skt (%s): %s\n", __func__, contact, strerror(errno));
    else
      fprintf(stderr,"%s: could not write cbridge packet (%d bytes written)\n", __func__, wval);
    close(skt);
    return -1;
  } 
#if CBDEBUG
  else 
    fprintf(stderr, "%s: wrote LSN cbridge packet length %d\n", __func__, wval);
#endif
  
  fd_set fds;
  FD_ZERO(&fds);
  FD_SET(skt, &fds);
  // Wait until something to read
  int sval;
  do {
    sval = select(skt+1, &fds, NULL, NULL, NULL);
    if (sval < 0) {
      if (errno == EINTR) {
#if CBDEBUG
	fprintf(stderr,"%s: select interrupted, looping\n", __func__);
#endif
	continue;
      } else {
	fprintf(stderr,"%s: select failed: %s\n", __func__, strerror(errno));
	return -1;
      }
    } else if (!dofork) {
      // Don't fork (for debugging), just return the socket
      return skt;
    } else {
      // fork and return
      int pid = fork();
      if (pid < 0) {
	fprintf(stderr,"%s: fork failed: %s\n", __func__, strerror(errno));
	return -1;
      }
      else if (pid == 0) {
	// child which gets to handle the connection
#if CBDEBUG
	fprintf(stderr,"%s: child (pid %d)\n", __func__, getpid());
#endif
	return skt;
      } else {
	// parent: close socket and return
	close(skt);
#if CBDEBUG
	fprintf(stderr,"%s: parent (pid %d) returning\n", __func__, getpid());
#endif
	return 0;
      }
    }
  } while (sval != EINTR);
  fprintf(stderr, "%s: unreachable code reached!\n", __func__);
  return -1;
}

// Half of chopen_cbridge
static struct connection *
cb_pick_up_connection(int skt, char *contact)
{
  struct packet *answer;

  int c = cb_find_free_conn();
  if (c < 0) {
    fprintf(stderr,"%s: can not find free conn!\n", __func__);
    return NULL;
  }
  struct connection *conn = connalloc(); /* allconn inits locking and queues */
  conns[c].skt = skt;
  conns[c].conn = conn;
  conns[c].contact = strdup(contact); /* Nice to keep */
  conns[c].is_main = 1;
#if CBDEBUG
  fprintf(stderr,"%s: initializing interruption mutex for %p [%p]\n", __func__, conns[c].conn, (void *)pthread_self());
#endif
  if (pthread_mutex_init(&conns[c].conn->interruption, NULL) != 0) {
    perror("pthread_mutex_init");
    abort();
  }
  if (pthread_mutex_lock(&conns[c].conn->interruption) != 0) {
    perror("pthread_mutex_lock");
    abort();
  }

  conn->cn_state = CSLISTEN;
  // get an RFC/CLS/LOS answer with chaos_connection_dequeue,
  answer = chaos_connection_dequeue(conn);
  if (answer == NULL) {
    fprintf(stderr,"%s: null answer\n", __func__);
    return NULL;
  } 
#if CBDEBUG
  else 
    fprintf(stderr, "%s: got %s from %#o on conn for %#o\n", __func__,
	    chop_name(answer->pk_op), CH_ADDR_SHORT(answer->pk_saddr), CH_ADDR_SHORT(conn->cn_faddr));
#endif
  if (answer->pk_op != RFCOP) {
    conn->cn_state = CSLOST;
    free(answer);
    return conn;
  }
  conn->cn_state = CSRFCRCVD;
  // free() the answer
  free(answer);
  // send OPN
  answer = chaos_allocate_packet(conn, OPNOP, 2 * sizeof(unsigned short));
  chaos_connection_queue(conn, answer);
  conn->cn_state = CSOPEN;
  return conn;
}

// Main function (cf chaos_queue_file_pkt)
// Arg is contact to listen on, and a function
// which given a conn handles it. 
// The handler is run in a separate fork to contain all memory leaks and races.
// It should return or exit only when the conn is/should be ended.
void
cb_listen_main(char *contact, void (*func)(struct connection *conn)) 
{
  char now[128];
  cb_init_conns();
  while (1) {
#if CBDEBUG
    int skt = cb_listen_and_fork(contact, 0);
#else
    int skt = cb_listen_and_fork(contact, 1);
#endif
    if (skt < 0) {
#if CBDEBUG
      fprintf(stderr,"%s: Listen failed.\n", __func__);
#endif
      sleep(10);
#if CBDEBUG
      fprintf(stderr,"%s: Trying again.\n", __func__);
#endif
    } else if (skt != 0) {
      // Child, in a new fork. Handle the conn
      struct connection *conn = cb_pick_up_connection(skt, contact);
      if (conn != NULL) {
	// This is executed in the child fork, where leaks and races are contained
	time_t nowt = time(NULL);
	strftime(now, sizeof(now), "%F %T", localtime(&nowt));
	uch11_myaddr = CH_ADDR_SHORT(conn->cn_faddr); // fake it
	int x = cb_find_conn(conn);
	fprintf(stderr,"%s: pid %d got conn from %#o on \"%s\"\n", now, getpid(), uch11_myaddr, conns[x].contact);
	// start a thread to handle this conn
	func(conn);
#if 1 || CBDEBUG
	fprintf(stderr,"%s: pid %d handler exited, for conn from %#o on \"%s\"\n", __func__, getpid(), uch11_myaddr, conns[x].contact);
#endif
	// Clean up all memory leaks
	exit(0);
      } else if (conn->cn_state != CSOPEN) {
	// we're in child, and the conn failed to open. Die.
	exit(1);
      }
    } else {
      // parent. Just loop back.
    }
  }
}

// cb_listen_main("FILE", &_processdata)
