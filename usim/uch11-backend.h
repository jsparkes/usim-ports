#pragma once

#include <pthread.h>
#include <stdbool.h>

enum CHAOS_HARDWARE_VALUES
{
	CHAOS_CSR_TIMER_INTERRUPT_ENABLE = (01 << 00), /* CHBUSY */
	CHAOS_CSR_LOOP_BACK = (01 << 01),	       /* CHLPBK */
	CHAOS_CSR_RECEIVE_ALL = (01 << 02),	       /* CHSPY */
	CHAOS_CSR_RECEIVER_CLEAR = (01 << 03),
	CHAOS_CSR_RECEIVE_ENABLE = (01 << 04),	/* CHREN */
	CHAOS_CSR_TRANSMIT_ENABLE = (01 << 05),	/* CHRIEN */
	CHAOS_CSR_INTERRUPT_ENABLES = (02 << 04),
	CHAOS_CSR_TRANSMIT_ABORT = (01 << 06),	   /* CHABRT */
	CHAOS_CSR_TRANSMIT_DONE = (01 << 07),	   /* CHTDN */
	CHAOS_CSR_TRANSMITTER_CLEAR = (01 << 010), /* CHTCLR */
	CHAOS_CSR_LOST_COUNT = (04 << 011),	   /* CHLC */
	CHAOS_CSR_RESET = (01 << 015),		   /* CHRST */
	CHAOS_CSR_CRC_ERROR = (01 << 016),	   /* CHCRC */
	CHAOS_CSR_RECEIVE_DONE = (01 << 017),	   /* CHRDN */
};

extern struct queue_head queuehead;
extern pthread_mutex_t recvqueue;
extern pthread_mutex_t recvqueue;

extern int chaosd_fd;
extern int uch11_backend;
extern int uch11_myaddr;
extern int uch11_serveraddr;

extern bool reconnect_chaos;

extern int uch11_csr;
extern int uch11_bit_count;
extern int uch11_lost_count;

extern unsigned short uch11_xmit_buffer[4096];
extern int uch11_xmit_buffer_size;
extern int uch11_xmit_buffer_ptr;

extern unsigned short uch11_rcv_buffer[4096];
extern unsigned short uch11_rcv_buffer_toss[4096];
extern int uch11_rcv_buffer_ptr;
extern int uch11_rcv_buffer_size;
extern bool uch11_rcv_buffer_empty;

// This hack is so that uch11-backend.h (this file) can be included in
// Chaosnet for Unix.
#ifndef CHAOS_H
#define CHAOS_H

#include <pthread.h>
#include <sys/queue.h>

struct packet_queue
{
	TAILQ_ENTRY(packet_queue) next;
	struct packet *packet;
};

TAILQ_HEAD(queue_head, packet_queue);

#include "chaos.h"
#include "chunix/chsys.h"
#include "chunix/chconf.h"
#include "chncp/chncp.h"
#endif
