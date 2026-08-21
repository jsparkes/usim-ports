/* lashup-debugger.c --- CADR two-machine lashup debugger side
 *
 * See cc; ldbg.lisp 
*/

#include <assert.h>
#include <err.h>
#include <errno.h>
#include <fcntl.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <unistd.h>

#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/time.h>

#include "bus-interface.h"
#include "lashup.h"
#include "lashup-debugger.h"
#include "utrace.h"

// in ms
int lashup_read_timeout = 1000;

char* lashup_debugger_addr = NULL;
int lashup_debugger_port = 0;

char* lashup_target_addr = NULL;
int lashup_target_port = 0;

static struct lashup_request_s request;
static struct lashup_reply_s reply;

static bool
lashup_debugger_xmt()
{
    if (lashup_target_port == 0) return false;

    if (lashup_debugger_addr == NULL) lashup_debugger_addr = "127.0.0.1";
    if (lashup_target_addr == NULL) lashup_target_addr = "127.0.0.1";

    bool success = false;

    struct sockaddr_in src_addr = {0};
    src_addr.sin_family = AF_INET;
    src_addr.sin_addr.s_addr = inet_addr(lashup_debugger_addr);
    src_addr.sin_port = htons(lashup_debugger_port);

    struct sockaddr_in dst_addr = {0};
    dst_addr.sin_family = AF_INET;
    dst_addr.sin_addr.s_addr = inet_addr(lashup_target_addr);
    dst_addr.sin_port = htons(lashup_target_port);

    int sockfd = socket(AF_INET, SOCK_DGRAM, 0);
    if (sockfd < 0){
        warn("socket failed\n");
        return false;
    }

    if (bind(sockfd, (struct sockaddr *)&src_addr, sizeof(src_addr)) < 0)
    {
        warn("bind failed\n");
        close(sockfd);
        return false;
    }

    if (lashup_debugger_port > 0)
    {
        struct sockaddr_in addr = {0};
        socklen_t addr_len = sizeof(addr);
        if (getsockname(sockfd, (struct sockaddr *)&addr, &addr_len) == 0)
        {
            DEBUG(TRACE_USIM, "lashup-debugger: debugger port: %d\n", ntohs(addr.sin_port));
        }
    }

    DEBUG(TRACE_LASHUP, "lashup-debugger: request: (%c, #o%06o, #o%o)\n", 
            request.command, request.uaddr, request.data);

    int ret = sendto(
            sockfd,
            &request, sizeof(request), 
            0, 
            (struct sockaddr *)&dst_addr, sizeof(dst_addr));

    if (ret >= 0)
    {
        struct timeval start;
        gettimeofday(&start, NULL);
        while (true)
        {
            // read timeout check
            struct timeval now;
            gettimeofday(&now, NULL);
            int diff = (now.tv_sec - start.tv_sec) * 1000 + (now.tv_usec - start.tv_usec) / 1000;
            if (diff > lashup_read_timeout) break;

            ret = recvfrom(
                    sockfd, 
                    &reply, sizeof(reply),
                    MSG_DONTWAIT,
                    NULL, NULL);

            if (ret >= 0)
            {
                DEBUG(TRACE_LASHUP, "lashup-debugger: reply: (#o%o, #o%o)\n", 
                        reply.bus_error_status, reply.data);
                success = true;
                break;
            }
            else
            {
                if (errno == EAGAIN || errno == EWOULDBLOCK)
                {
                    // non-blocking, retry
                }
                else
                {
                    warnx("recv error");
                    break;
                }
            }
        }
    } 
    else 
    {
        warnx("send error");
    }

    if (sockfd > 0) close(sockfd);

    if (success)
    {
        bus_interface_set_debuggee_bus_error_status(reply.bus_error_status);
        return true;
    }
    else
    {
        // cannot execute the command
        // I am not exactly sure what to do when this happens
        // this is like debugger cable not connected
        // unibus timeout ?
        bus_interface_set_debuggee_bus_error_status(010);
        return false;
    }
}

bool
lashup_debugger_read(uint32_t uaddr, uint16_t *pv)
{
    request.command = 'r';
    request.uaddr = uaddr;
    request.data = 0;

    if (lashup_debugger_xmt())
    {
        *pv = reply.data;
        return true;
    }
    else
    {
        *pv = 0;
        return false;
    }
}

bool
lashup_debugger_write(uint32_t uaddr, uint16_t v)
{
    request.command = 'w';
    request.uaddr = uaddr;
    request.data = v;
    
    return lashup_debugger_xmt();
}

bool
lashup_debugger_inhibit_nxm(bool inhibit)
{
    request.command = 't';
    request.uaddr = 0;
    request.data = inhibit ? 1 : 0;

    return lashup_debugger_xmt();
}

bool
lashup_debugger_reset_unibus_and_bus_interface()
{
    request.command = 's';
    request.uaddr = 0;
    request.data = 0;

    return lashup_debugger_xmt();
}

bool
lashup_debugger_mark_debuggee(uint8_t symbol)
{
    request.command = 'e';
    request.uaddr = 0;
    request.data = symbol;

    return lashup_debugger_xmt();
}

bool
lashup_debugger_mark_debugger(uint8_t symbol)
{
    warnx("*** DEBUGGER MARK *** #o%o ***", symbol);

    return true;
}

bool
lashup_debugger_ping(void)
{
    warnx("*** DEBUGGER PING ***");

    request.command = 'p';
    request.uaddr = 0;
    request.data = 0;

    return lashup_debugger_xmt();
}

bool
lashup_debugger_usim(uint8_t cmd, uint8_t param)
{
    request.command = 'u';
    request.uaddr = 0;
    request.data = (cmd << 8) | param;

    return lashup_debugger_xmt();
}
