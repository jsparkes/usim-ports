/* server --- Chaosnet NCP server
 *
 * Chaos node with Chaos protocol processing, connects to chaosd
 * server, forks servers and does protocol procesing (NCP, etc...).
 */

#include <sys/poll.h>
#include <sys/socket.h>
#include <sys/uio.h>

#include <err.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <syslog.h>
#include <time.h>
#include <unistd.h>

#include "chaos.h"
#include "chconf.h"
#include "chsys.h"
#include "../chncp/chncp.h"

#include "hosttab.h"
#include "chcfg.h"
#include "trace.h"

#include "chaosd.h"
#include "misc.h"
#include "sig.h"

int serveraddr = 0404;

static char *config_filename;
bool daemon_flag;
int server_running;
int chaosd_fd;

static int pkt_num;

void
server_shutdown(void)
{
	server_running = false;
}

void
restart_child(void)
{
	/* for sig.c */
}

/*
 * This fork code is a hack; it needs to handle lots of servers but
 * right now we're just supporting FILE and MINI.
 *
 * child process
 *   connection 0...  main rfc pipe
 *     has 2 fd's (one/direction) <--> chaos ncp connection
 *     dgram control fd - for control messages
 *     stream control fd - for passing fd's to ncp connections
 *   connection 1...  create with chopen
 *     has 1 fd's, bidir <--> chaos ncp connection
 *   connection 2...  create with chopen
 *     has 1 fd's, bidir <--> chaos ncp connection
 *   ...
 */

int child_pid;
int child_fd_data_in;
int child_fd_ctl;
int child_fd_sctl;

#define MAX_CHILD_CONN	10

struct {
	void *conn;
	int fd_out;
	int fd_in;
} child_conn[MAX_CHILD_CONN];

int child_conn_count;

/*
 * Send data to a captive server (like FILE).  This is used via the
 * INPUT() macro defiend in chunix/chsys.h.
 */
void
server_input(struct connection *conn)
{
	struct packet *pkt;
	int i;
	int conn_fd;
	int len;

	/*
	 * We should store a hint in the conn.
	 */
	conn_fd = -1;
	for (i = 0; i < child_conn_count; i++) {
		if (child_conn[i].conn == conn) {
			conn_fd = child_conn[i].fd_out;
			DEBUG(TRACE_SERVER, "server: server_input() - found conn %p @ %d, fd %d\n", conn, i, conn_fd);
			break;
		}
	}
	if (conn_fd < 0) {
		DEBUG(TRACE_SERVER, "server: server_input() - NO CONNECTION?\n");
		return;
	}
	while ((pkt = conn->cn_rhead) != NOPKT) {
		char *ptr;

		ptr = (char *)&pkt->pk_phead;
		DEBUG(TRACE_SERVER, "server: server_input() - pkt length (+ header) = %d + %d \n", PH_LEN(pkt->pk_phead) + sizeof(struct pkt_header));
		if (conn_fd) {
			ptr = pkt->pk_cdata;
			len = PH_LEN(pkt->pk_phead);
			ptr--;
			ptr[0] = pkt->pk_op;
			len++;
			write(conn_fd, ptr, len);
		}
		ch_read(conn);
	}
}
void
fork_server(char *app_name, char *arg)
{
	int ret;
	int r;
	int i;
	int svdo[2];
	int svdi[2];
	int svc[2];
	int svs[2];
	int tmp[2];

	DEBUG(TRACE_SERVER, "server: fork_server(app_name = %s, arg = %s)\n", app_name, arg);
	ret = socketpair(AF_UNIX, SOCK_DGRAM, 0, tmp);
	/*
	 * Create a pair of packet based local sockets
	 */
	ret = socketpair(AF_UNIX, SOCK_DGRAM, 0, svdo);
	if (ret) {
		perror("socketpair");
		return;
	}
	ret = socketpair(AF_UNIX, SOCK_DGRAM, 0, svdi);
	if (ret) {
		perror("socketpair");
		return;
	}
	ret = socketpair(AF_UNIX, SOCK_DGRAM, 0, svc);
	if (ret) {
		perror("socketpair");
		return;
	}
	ret = socketpair(AF_UNIX, SOCK_STREAM, 0, svs);
	if (ret) {
		perror("socketpair");
		return;
	}
	close(tmp[0]);
	close(tmp[1]);
	DEBUG(TRACE_SERVER, "server: fork_server() - tmp = %d, %d\n", tmp[0], tmp[1]);
	/*
	 * Make a copy of ourselves.
	 */
	r = fork();
	if (r != 0) {
		child_pid = r;
		DEBUG(TRACE_SERVER, "server: fork_server() - child_pid = %d\n", child_pid);
		child_conn[0].fd_out = svdo[0];
		child_fd_data_in = svdi[0];
		child_conn[0].fd_in = svdi[0];
		child_fd_ctl = svc[0];
		child_fd_sctl = svs[0];
		DEBUG(TRACE_SERVER, "server: fork_server() - fd_out = %d, fd_in = %d, child_fd_ctl = %d, child_fd_sctl = %d\n", child_conn[0].fd_out, child_conn[0].fd_in, child_fd_ctl, child_fd_sctl);
		return;
	}
	if (r == -1) {
		perror("fork");
		DEBUG(TRACE_SERVER, "server:  unable to fork new process; %%m");
		return;
	}
	/*
	 * We're the child, close stdin, stdout, stderr, etc...
	 */
	close(0);
	close(1);
	close(2);
	close(3);
	close(4);
	/*
	 * ... and repoen as pipes.
	 */
	dup2(svdo[1], 0);
	dup2(svdi[1], 1);
	dup2(svc[1], 3);
	dup2(svs[1], 4);
	for (i = 5; i < 256; i++)
		close(i);
	r = execl(app_name, app_name, "1", (char *)0);/* ---!!! argument should come from parsing packet. */
	if (r)
		DEBUG(TRACE_SERVER, "server: can't exec %s; %%m", app_name);
	exit(1);
}

void
filerfc(struct packet *pkt)
{
	struct packet *p;
	struct connection *conn;
	int i;
	char *parg;
	char *args;
	int arglen;

	args = pkt->pk_cdata;
	arglen = PH_LEN(pkt->pk_phead);
	p = pkalloc(10, 0);
	strcpy(p->pk_cdata, "FILE");
	conn = ch_listen(p, 0);
	lsnmatch(pkt, conn);
	ch_accept(conn);
	child_conn[0].conn = conn;
	child_conn[0].fd_out = -1;
	child_conn[0].fd_in = -1;
	child_conn_count = 1;
	/*
	 * Skip over RFC name.
	 */
	for (i = 0; i < arglen; i++) {
		if (args[i] == ' ')
			break;
	}
	parg = 0;
	if (args[i] == ' ') {
		args[arglen] = 0;
		parg = &args[i + 1];
	}
	fork_server("../cmd/FILE", parg);
}

void
minirfc(struct packet *pkt)
{
	struct packet *p;
	struct connection *conn;
	int i;
	char *parg;
	int arglen;
	char *args;

	args = pkt->pk_cdata;
	arglen = PH_LEN(pkt->pk_phead);
	p = pkalloc(10, 0);
	strcpy(p->pk_cdata, "MINI");
	conn = ch_listen(p, 0);
	lsnmatch(pkt, conn);
	ch_accept(conn);
	child_conn[0].conn = conn;
	child_conn[0].fd_out = -1;
	child_conn[0].fd_in = -1;
	child_conn_count = 1;
	/*
	 * Skip over RFC name.
	 */
	for (i = 0; i < arglen; i++) {
		if (args[i] == ' ')
			break;
	}
	parg = 0;
	if (args[i] == ' ') {
		args[arglen] = 0;
		parg = &args[i + 1];
	}
	fork_server("../cmd/MINI", parg);
}

struct chxcvr intf;

void
ch_rcv_pkt_buffer(unsigned char *buffer, int size)
{
	struct packet *pkt;

	pkt = pkalloc(size, 0);
	if (pkt == 0)
		return;
	memcpy((char *)&pkt->pk_phead, buffer, size);
	intf.xc_rpkt = pkt;
	rcvpkt(&intf);
}

int
chaos_read(void)
{
	int ret;
	int len;
	unsigned char lenbytes[4];
	unsigned char buffer[4096];

	DEBUG(TRACE_SERVER, "server: chaos_read()");
	ret = read(chaosd_fd, lenbytes, 4);
	if (ret <= 0)
		return -1;
	len = (lenbytes[0] << 8) | lenbytes[1];
	ret = read(chaosd_fd, buffer, len);
	DEBUG(TRACE_SERVER, "server: chaos_read() - ret = %d\n", ret);
	if (ret <= 0)
		return -1;
	if (ret != len) {
		DEBUG(TRACE_SERVER, "server: chaos_read() - length error (len = %d)\n", len);
		return -1;
	}
#if 0
	check_packet(buffer, ret);
#else
	ch_rcv_pkt_buffer(buffer, ret);
#endif
	return 0;
}

int
ch_setmode(struct connection *conn, int mode)
{
	int ret = 0;

	switch (mode) {
	case CHTTY:
		ret = -1;
		break;
	case CHSTREAM:
	case CHRECORD:
		if (conn->cn_mode == CHTTY)
			ret = -1;
		else
			conn->cn_mode = mode;
	}
	return ret;
}

int
ch_full(struct connection *conn)
{
	return chtfull(conn);
}

/*
 * Read control connection from child process.  These are messages
 * from chlib, used to manipulate Chaos connections.  We send them a
 * "connection #" along with the fd which is used as an index into the
 * child connection table.
 */
int
read_child_ctl(void)
{
	int ret;
	int len;
	int req;
	int conn_num;
	int mode;
	char ctlbuf[512];
	char contact[64];

	DEBUG(TRACE_SERVER, "read_child_ctl()\n");
	ret = read(child_fd_ctl, ctlbuf, 512);
	DEBUG(TRACE_SERVER, "server: read_child(): ret = %d\n", ret);
	req = ctlbuf[0];	/* 1st byte is cp_op */
	conn_num = ctlbuf[1];
	mode = ctlbuf[2];
	DEBUG(TRACE_SERVER, "server: read_child_ctl(): req = %d, conn_num = %d, mode = %o\n", req, conn_num, mode);
	switch (req) {
	case 1:		/* chstatus */
	{
		struct chstatus chst;
		struct connection *conn;
		struct packet *pkt;

		memcpy((char *)&chst, &ctlbuf[4], sizeof(struct chstatus));
		conn = child_conn[conn_num].conn;
		chst.st_fhost = CH_ADDR_SHORT(conn->cn_faddr);
		chst.st_cnum = conn->cn_ltidx;
		chst.st_rwsize = conn->cn_rwsize;
		chst.st_twsize = conn->cn_twsize;
		chst.st_state = conn->cn_state;
		chst.st_cmode = conn->cn_mode;
		chst.st_oroom = conn->cn_twsize - (conn->cn_tlast - conn->cn_tacked);
		pkt = conn->cn_rhead;
		chst.st_ptype = 0;
		chst.st_plength = 0;
		if (pkt != NOPKT) {
			chst.st_ptype = pkt->pk_op;
			chst.st_plength = PH_LEN(pkt->pk_phead);
		}
		ctlbuf[2] = 0;
		ctlbuf[3] = 0;
		memcpy(&ctlbuf[4], (char *)&chst, sizeof(struct chstatus));
		len = 4 + sizeof(struct chstatus);
		write(child_fd_ctl, ctlbuf, len);
	}
	break;
	case 2:		/* chopen */
	{
		struct chopen rfc;
		int sv[2];
		char cmsgbuf[sizeof(struct cmsghdr) + sizeof(int)];
		struct msghdr msg;
		struct cmsghdr *cmsg;
		struct iovec vector;
		void *conn;
		int err;
		extern void *chopen(struct chopen *co, int mode, int *perr);

		memcpy((char *)&rfc, &ctlbuf[4], sizeof(rfc));
		rfc.co_contact = 0;
		rfc.co_data = 0;
		if (rfc.co_clength > 0) {
			memcpy(contact, &ctlbuf[4 + sizeof(rfc)], rfc.co_clength);
			contact[rfc.co_clength] = 0;
			rfc.co_contact = contact;
			rfc.co_data = &ctlbuf[4 + sizeof(rfc) + rfc.co_clength];
		}
		DEBUG(TRACE_SERVER, "server: read_child_ctl(req == CHOPEN) - rfc.co_contact = %s, rfc.co_host = %o", rfc.co_contact, rfc.co_host);
		conn = chopen(&rfc, mode, &err);
		/*
		 * Make a socket to talk to the connection.
		 */
		ret = socketpair(AF_UNIX, SOCK_DGRAM, 0, sv);
		child_conn[child_conn_count].conn = conn;
		child_conn[child_conn_count].fd_out = sv[0];
		child_conn[child_conn_count].fd_in = sv[0];
		child_conn_count++;
		ctlbuf[0] = 2;
		ctlbuf[1] = child_conn_count - 1;
		ctlbuf[2] = 0;
		ctlbuf[3] = 0;
		/*
		 * Send socket fd to the server.
		 */
		DEBUG(TRACE_SERVER, "server: read_child_ctl(req == CHOPEN) - sending socket fd to server (sctl = %d, fd = %d, local = %d)", child_fd_sctl, sv[1], sv[0]);
		memset(cmsgbuf, 0, sizeof(cmsgbuf));
		cmsg = (struct cmsghdr *)cmsgbuf;
		cmsg->cmsg_len = sizeof(struct cmsghdr) + sizeof(int);
		cmsg->cmsg_level = SOL_SOCKET;
		cmsg->cmsg_type = SCM_RIGHTS;
		memcpy(CMSG_DATA(cmsg), &sv[1], sizeof(sv[1]));
		vector.iov_base = ctlbuf;
		vector.iov_len = 4;
		msg.msg_flags = 0;
		msg.msg_control = cmsg;
		msg.msg_controllen = cmsg->cmsg_len;
		msg.msg_name = NULL;
		msg.msg_namelen = 0;
		msg.msg_iov = &vector;
		msg.msg_iovlen = 1;
		ret = sendmsg(child_fd_sctl, &msg, 0);
		if (ret < 0)
			perror("sendmsg");
	}
	break;
	case 3:		/* setmode */
		ret = ch_setmode(child_conn[conn_num].conn, mode);
		ctlbuf[0] = 3;
		ctlbuf[1] = ret;
		ctlbuf[2] = 0;
		ctlbuf[3] = 0;
		write(child_fd_ctl, ctlbuf, 4);
		break;
	}
	return 0;
}

int
read_child_data(int conn_num)
{
	struct packet *pkt;
	int ret;

	DEBUG(TRACE_SERVER, "server: read_child_data()");
	/*
	 * Blocking.
	 */
	if (ch_full(child_conn[conn_num].conn))
		return 0;
	pkt = pkalloc(512, 1);
	/*
	 * 1st byte is cp_op
	 */
	ret = read(child_conn[conn_num].fd_in, pkt->pk_cdata - 1, 512);
	pkt->pk_op = /*DATOP*/ pkt->pk_cdata[-1];
	SET_PH_LEN(pkt->pk_phead, ret - 1);
	DEBUG(TRACE_SERVER, "server: read_child_data() - PH_LEN(pkt->pk_phead) = %d", PH_LEN(pkt->pk_phead));
	ch_write(child_conn[conn_num].conn, pkt);
	return 0;
}

#define MAX_UFDS (2 + MAX_CHILD_CONN)

/*
 * Listen on all the various connections.
 */
int
chaos_poll(void)
{
	struct pollfd ufds[MAX_UFDS];
	int ufds_type[MAX_UFDS];
	int ufds_conn_num[MAX_UFDS];
	int n;
	int ret;

	for (int i = 0; i < MAX_UFDS; i++) {
		ufds[i].revents = 0;
		ufds_type[i] = 0;
		ufds_conn_num[i] = -1;
	}
	/*
	 * Main connection from chaosd.
	 */
	n = 0;
	ufds[0].fd = chaosd_fd;
	ufds[0].events = POLLIN;
	ufds[0].revents = 0;
	ufds_type[0] = 1;
	/*
	 * Control connection.
	 */
	if (child_fd_ctl) {
		ufds[n].fd = child_fd_ctl;
		ufds[n].events = POLLIN;
		ufds_type[n] = 2;
		n++;
	}
	/*
	 * All the child connections.
	 */
	for (int i = 0; i < child_conn_count; i++) {
		ufds[n].fd = child_conn[i].fd_in;
		ufds[n].events = POLLIN;
		ufds_type[n] = 3;
		ufds_conn_num[n] = i;
		n++;
	}
	ret = poll(ufds, n, 10);
	if (ret == 0)
		return 0;
	if (ret == -1) {
		perror("poll:");
		return -1;
	}
	for (int i = 0; i < n; i++) {
		if (ufds[i].revents == 0)
			continue;
		switch (ufds_type[i]) {
		case 1:
			/*
			 * Notice when chaosd dies.
			 */
			if (ufds[i].revents & POLLHUP)
				return -1;
			chaos_read();
			break;
		case 2:
			read_child_ctl();
			break;
		case 3:
			read_child_data(ufds_conn_num[i]);
			break;
		}
	}
	return 0;
}

/*
 * Acting like an ethernet driver, transmit a packet to the chaosd
 * server to be sent on the "wire".  This has to be type compatible
 * iwth chxcvr.xc_xmit.
 */
int
chaos_xmit(struct chxcvr *intf, struct packet *pkt, int at_head_p)
{
	int chlength;
	char *ptr;
	struct iovec iov[3];
	unsigned short t[3];
	unsigned char lenbytes[4];
	int ret;
	int plen;

	chlength = PH_LEN(pkt->pk_phead) + sizeof(struct pkt_header);
	ptr = (char *)&pkt->pk_phead;
	DEBUG(TRACE_SERVER, "server: chaos_xmit() - chlength = %d, chaosd_fd = %d", chlength, chaosd_fd);
	/*
	 * Pad odd lengths.
	 */
	if (chlength & 1)
		chlength++;
	plen = chlength + 6;
	/*
	 * Chaosd header BIG ENDIAN?
	 */
	lenbytes[0] = plen >> 8;
	lenbytes[1] = plen;
	lenbytes[2] = 0;
	lenbytes[3] = 0;
#if 1
	/*
	 * For debugging...
	 */
	lenbytes[3] = ++pkt_num;
#endif
	/*
	 * Network header (at end of pkt). N.B. this needs to be in network byte order.
	 */
	t[0] = CH_ADDR_SHORT(pkt->pk_phead.ph_daddr);
	t[1] = CH_ADDR_SHORT(pkt->pk_phead.ph_saddr);
	t[2] = 0;
	iov[0].iov_base = lenbytes;
	iov[0].iov_len = 4;
	iov[1].iov_base = ptr;
	iov[1].iov_len = chlength;
	iov[2].iov_base = t;
	iov[2].iov_len = 6;
	ret = writev(chaosd_fd, iov, 3);
	if (ret < 0) {
		perror("writev");
		return -1;
	}
	xmitdone(pkt);
	return 0;
}

int
chaos_init(char *name, int address)
{
	Chmyaddr = address;
	strcpy(Chmyname, name);
	Chrfcrcv = 1;
	intf.xc_xmit = chaos_xmit;
	SET_CH_ADDR(intf.xc_addr, Chmyaddr);
	Chroutetab[1].rt_type = 0;
#if 1
	/*
	 * Pick random uniq so a restarted server doesn't reuse old ids.
	 */
	extern int uniq;	/* from chncp/chutil.c */

	srand(time(0L));
	uniq = rand();
#endif
	return 0;
}

void
usage(void)
{
	fprintf(stderr, "usage: server [OPTION]...\n");
	fprintf(stderr, "Chaos NCP server\n");
	fprintf(stderr, "\n");
	fprintf(stderr, "  -c FILE        configuration file (default: %s)\n", config_filename);
	fprintf(stderr, "  -s             run as a background daemon\n");
	fprintf(stderr, "  -h             help message\n");
}

int
main(int argc, char *argv[])
{
	int c;
	char *hosts_file;

	config_filename = "chaos.ini";
	daemon_flag = false;
	while ((c = getopt(argc, argv, "sh")) != -1) {
		switch (c) {
		case 'c':
			config_filename = strdup(optarg);
			break;
		case 's':
			daemon_flag = true;
			break;
		case 'h':
			usage();
			exit(0);
		default:
			usage();
			exit(1);
		}
	}
	chcfg_init();
	hosts_file = realpath(chcfg.chaos_hosts, NULL);
	if (hosts_file != NULL) {
		NOTICE(TRACE_CHAOSD, "server: using hosts table from \"%s\"\n", hosts_file);
		readhosts(chcfg.chaos_servername, hosts_file);
		serveraddr = chaos_addr(chcfg.chaos_servername, 0);
	} else
		NOTICE(TRACE_SERVER, "server: no host table; using defaults\n");
	if (daemon_flag == true)
		daemonize(argv[0]);
	sig_init();
	chaos_init(chcfg.chaos_servername, serveraddr);
	NOTICE(TRACE_SERVER, "server: i am %s (0%o)\n", chcfg.chaos_servername, serveraddr);
	chaosd_fd = chdopen();
	if (chaosd_fd == -1)
		errx(1, "chdopen");
#if 0
	send_testpackets();
#endif
	server_running = true;
	while (server_running == true) {
		if (chaos_poll())
			break;
		sig_poll();
	}
	exit(0);
}
