/* sig.c --- signal handling code
 *
 * Common signalling code to handle restarts.
 */

#include <signal.h>
#include <stdio.h>
#include <string.h>

#include "sig.h"
#include "trace.h"

int got_sighup;
int got_sigterm;
int got_sigchld;

void
sighup_handler(int arg)
{
	got_sighup++;
}

void
sigterm_handler(int arg)
{
	got_sigterm++;
}

void
sigpipe_handler(int arg)
{
	printf("sigpipe!\n");
}

void
sigchld_handler(int arg)
{
	printf("sigchld!\n");
	got_sigchld++;
}

int
sig_init(void)
{
	struct sigaction new;
	struct sigaction old;

	memset(&new, 0, sizeof(new));
	new.sa_handler = sighup_handler;
	sigaction(SIGHUP, &new, &old);
	memset(&new, 0, sizeof(new));
	new.sa_handler = sigterm_handler;
	sigaction(SIGTERM, &new, &old);
	memset(&new, 0, sizeof(new));
	new.sa_handler = sigpipe_handler;
	sigaction(SIGPIPE, &new, &old);
	memset(&new, 0, sizeof(new));
	new.sa_handler = sigchld_handler;
	sigaction(SIGCHLD, &new, &old);
	return 0;
}

void
sig_poll(void)
{
	if (got_sighup)
		DEBUG(TRACE_CHAOSD, "got signal SIGHUP; ignoring\n");
	if (got_sigterm) {
		DEBUG(TRACE_CHAOSD, "got signal SIGTERM; shutting down\n");
		server_shutdown();
	}
	if (got_sigchld) {
		DEBUG(TRACE_CHAOSD, "got signal SIGCHLD; child died\n");
		restart_child();
	}
}
