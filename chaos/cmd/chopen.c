#include <stdlib.h>
#include <stdio.h>
#include <unistd.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/ioctl.h>
#include <sys/uio.h>
#include <sys/un.h>

#ifdef SELECT
# ifndef USE_CBRIDGE
#include "uch11-backend.h"
# else
#  ifndef CHAOS_H
#define CHAOS_H
#include "chaos.h"
#include "chunix/chconf.h"
#include "chncp/chncp.h"
#  endif /* CHAOS_H */
#  include "chcbridge/cbncp.h"
# endif /* USE_CBRIDGE */
#else
#include "chaos.h"
#endif

#include "hosttab.h"

#ifdef SELECT
struct connection *
chopen_conn(struct chopen *rfc, int mode)
{
	size_t co_clength;
	struct connection *conn;
	struct packet *pkt;
	struct packet *answer;
	struct packet *sts;

	conn = allconn();
	co_clength = strlen(rfc->co_contact);

	conn->cn_rwsize = (unsigned short)(rfc->co_rwsize ? rfc->co_rwsize : 5);

	if (!rfc->co_host)	/* listen */
		return conn;

	pkt = pkalloc(co_clength, 1);

	pkt->pk_op = RFCOP;
	SET_PH_LEN(pkt->pk_phead, (unsigned short)co_clength);
	SET_CH_ADDR(pkt->pk_daddr, (unsigned short)uch11_myaddr);
	SET_CH_INDEX(pkt->pk_didx, 0);
	SET_CH_ADDR(conn->cn_faddr, (unsigned short)uch11_myaddr);
	SET_CH_INDEX(conn->cn_fidx, 0);
	pkt->pk_saddr = conn->cn_laddr;
	pkt->pk_sidx = conn->cn_lidx;
	pkt->pk_pkn = ++conn->packetnumber;
	if (pkt->pk_pkn == 0)
		pkt->pk_pkn = conn->packetnumber = 1;
	pkt->pk_ackn = 0;
	conn->cn_racked = 0;

	memcpy(pkt->pk_cdata, rfc->co_contact, co_clength);

	chaos_connection_queue(conn, pkt);

	conn->cn_state = CSRFCSENT;

	/*
	 * wait for answer 
	 */
	answer = chaos_connection_dequeue(conn);
	if (answer == 0)
		return conn;

	conn->cn_fidx = answer->pk_sidx;
	conn->cn_state = CSOPEN;

	sts = pkalloc(2 * sizeof(unsigned short), 0);

	sts->pk_op = STSOP;
	SET_PH_LEN(sts->pk_phead, 2 * sizeof(unsigned short));
	SET_CH_ADDR(sts->pk_daddr, (unsigned short)uch11_myaddr);
	sts->pk_didx = conn->cn_fidx;
	sts->pk_saddr = conn->cn_laddr;
	sts->pk_sidx = conn->cn_lidx;
	sts->pk_pkn = conn->packetnumber;
	if (sts->pk_pkn == 0)
		sts->pk_pkn = conn->packetnumber = 1;
	sts->pk_ackn = answer->pk_pkn;
	conn->cn_racked = answer->pk_pkn;
	*(unsigned short *)&sts->pk_cdata[0] = conn->cn_racked;
	*(unsigned short *)&sts->pk_cdata[2] = conn->cn_rwsize;

	free(answer);
	chaos_connection_queue(conn, sts);

	return conn;
}
#endif

int map_fd_to_conn[256];

int
chopen_fd(struct chopen *rfc, int mode)
{
	char cmsgbuf[sizeof(struct cmsghdr) + sizeof(int)];
	struct cmsghdr *cmsg;
	int ret, len;
	char buffer[512];
	struct msghdr msg;
	struct iovec vector;
	int connfd;

	len = 4 + sizeof(struct chopen) + rfc->co_clength + rfc->co_length;

	/*
	 * send chopen request 
	 */
	buffer[0] = 2;
	buffer[1] = 0;
	buffer[2] = mode;
	buffer[3] = rfc->co_async;
	memcpy(&buffer[4], (char *)rfc, sizeof(struct chopen));
	memcpy(&buffer[4 + sizeof(struct chopen)], rfc->co_contact, rfc->co_clength);

	if (rfc->co_data && rfc->co_length > 0)
		memcpy(&buffer[4 + sizeof(struct chopen) + rfc->co_length], rfc->co_data, rfc->co_length);

	fprintf(stderr, "chopen: write\n");
	fflush(stderr);
	if (write(3, buffer, len) < 0) {
		perror("chopen request");
		fflush(stderr);
	}

	/*
	 * read back status and connection fd 
	 */
	memset(cmsgbuf, 0, sizeof(cmsgbuf));
	cmsg = (struct cmsghdr *)cmsgbuf;
	cmsg->cmsg_len = sizeof(struct cmsghdr) + sizeof(int);

	vector.iov_base = buffer;
	vector.iov_len = 4;

	/*
	 */
	msg.msg_flags = 0;
	msg.msg_control = cmsg;
	msg.msg_controllen = cmsg->cmsg_len;

	msg.msg_name = NULL;
	msg.msg_namelen = 0;
	msg.msg_iov = &vector;
	msg.msg_iovlen = 1;

	ret = recvmsg(4, &msg, 0);
	fprintf(stderr, "chopen: recvmsg ret %d\n", ret);
	fflush(stderr);
	if (ret < 0) {
		perror("recvmsg");
		fflush(stderr);
	}

	memcpy(&connfd, CMSG_DATA(cmsg), sizeof(connfd));

	fprintf(stderr, "chopen: connfd %d, conn_num %d\n", connfd, buffer[1]);
	fflush(stderr);

	if (connfd > 0) {
		map_fd_to_conn[connfd] = buffer[1];
	}
	return connfd;
}

#ifdef SELECT
struct connection *
#else
int
#endif
chopen(int address, char *contact, int mode, int async, char *data, int dlength, int rwsize)
{
	struct chopen rfc;
#ifndef SELECT
	int connfd = -1;
#endif

	rfc.co_host = address;
	rfc.co_contact = contact;
	rfc.co_data = data;
	rfc.co_length = data ? (dlength ? dlength : strlen(data)) : 0;
	rfc.co_clength = strlen(contact);
	rfc.co_async = async;
	rfc.co_rwsize = rwsize;
	/*
	 * If we already have done readhosts(), don't do it again.
	 */
	if (host_data == NULL)
		readhosts("SERVER", "hosts");
#if 0
#if defined(BSD42) || defined(linux) || defined(__OpenBSD__)
	int f = open(CHAOSDEV, mode);
	if (f >= 0) {
		connfd = ioctl(f, CHIOCOPEN, &rfc);
	}
	close(f);
#else
	if (f >= 0 && ioctl(f, CHIOCOPEN, &rfc))
		close(f);
	else
		connfd = f;
#endif
#else
#ifdef SELECT
# ifdef USE_CBRIDGE
	return chopen_cbridge(&rfc, mode);
# else
	return chopen_conn(&rfc, mode);
# endif /* USE_CBRIDGE */
#else
	connfd = chopen_fd(&rfc, mode);
	return connfd;
#endif
#endif
}

#ifdef SELECT
struct connection *
#else
int
#endif
chlisten(char *contact, int mode, int async, int rwsize)
{
	return chopen(0, contact, mode, async, 0, 0, rwsize);
}

int
chreject(int fd, char *string)
{
	struct chreject chr;

	if (string == 0 || strlen(string) == 0)
		string = "No Reason Given";
	chr.cr_reason = string;
	chr.cr_length = strlen(string);
	return 0;
}

int
chstatus(int fd, struct chstatus *chst)
{
#ifndef SELECT
	char buffer[512];
	int len, ret, conn_num;

	fprintf(stderr, "chstatus(fd=%d, chst=%p)\n", fd, chst);
	fflush(stderr);

	conn_num = map_fd_to_conn[fd];

	buffer[0] = 1;
	buffer[1] = conn_num;
	buffer[2] = 0;
	buffer[3] = 0;

	memcpy(&buffer[4], (char *)chst, sizeof(struct chstatus));
	len = 4 + sizeof(struct chstatus);

	ret = write(3, buffer, len);
	ret = read(3, buffer, len);
	fprintf(stderr, "chstatus: read ret %d\n", ret);
	fflush(stderr);

	memcpy((char *)chst, &buffer[4], sizeof(struct chstatus));

	fprintf(stderr, "cnum %o, fhost %o, state %d\n", chst->st_cnum, chst->st_fhost, chst->st_state);
	fflush(stderr);
#endif
	return 0;
}

int
chwaitfornotstate(int fd, int state)
{
#ifndef SELECT
	int ret;

	fprintf(stderr, "chwaitfornotstate(fd=%d, state=%d)\n", fd, state);
	fflush(stderr);

	while (1) {
		struct chstatus chst;

		fprintf(stderr, "chwaitfornotstate(fd=%d, state=%d) loop\n", fd, state);
		fflush(stderr);

		ret = chstatus(fd, &chst);
		if (ret < 0)
			return ret;

		if (chst.st_state != state)
			break;

		sleep(1);
	}

	fprintf(stderr, "chwaitfornotstate(fd=%d, state=%d) done\n", fd, state);
	fflush(stderr);
#endif
	return 0;
}

int
chsetmode(int fd, int mode)
{
#ifndef SELECT
	char buffer[512];
	int len, ret, conn_num;

	fprintf(stderr, "chsetmode(fd=%d, mode=%d)\n", fd, mode);
	fflush(stderr);

	conn_num = map_fd_to_conn[fd];

	buffer[0] = 3;
	buffer[1] = conn_num;
	buffer[2] = mode;
	buffer[3] = 0;
	len = 4;

	ret = write(3, buffer, len);
	ret = read(3, buffer, len);
	fprintf(stderr, "chsetmode: read ret %d\n", ret);
	fflush(stderr);
#endif
	return 0;
}

#ifndef SELECT
char *
chaos_name(short addr)
{
	return "server";
}
#endif
