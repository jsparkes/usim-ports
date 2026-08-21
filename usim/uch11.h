#pragma once

#define	UCH11_BACKEND_DAEMON 0
#define	UCH11_BACKEND_LOCAL 1
#define UCH11_BACKEND_UDP 2

extern int uch11_backend;
extern int uch11_serveraddr;
extern int uch11_myaddr;
extern int hybrid_udp_and_local;

extern int uch11_init(void);
extern void uch11_poll(void);

extern int uch11_get_csr(void);
extern void uch11_set_csr(int);
extern int uch11_get_bit_count(void);
extern int uch11_get_rcv_buffer(void);
extern void uch11_put_xmit_buffer(int);
extern void uch11_xmit_pkt(void);
extern void uch11_rx_pkt(void);
extern void uch11_reconnect(void);
extern void uch11_force_reconect(void);
