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

#define CBRIDGE_PACKET_SOCKET "/tmp/chaos_packet"
#define CBRIDGE_PACKET_HEADER_SIZE 4
#define CH_PK_MAX_DATALEN 488

#define MAX_CBRIDGE_CONNS 32

// Replacing chutil.c
void chaos_interrupt_connection(struct connection *conn);
int chaos_clear_connection_interrupt(struct connection *conn);
extern pthread_mutex_t xferthread_mutex;
// Replacing things in uch11.c
struct packet *chaos_connection_dequeue(struct connection *conn);
int chaos_connection_queue(struct connection *conn, struct packet *packet);
// To use in chopen
struct connection *chopen_cbridge(struct chopen *rfc, int mode);
// Start a listener fun
void cb_listen_main(char *contact, void (*fun)(struct connection *conn));

// Fake it
extern unsigned short uch11_myaddr;
