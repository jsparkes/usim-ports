#include <stdio.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <netinet/in_systm.h>
#include <netinet/ip.h>
#include <netdb.h>
#include <ctype.h>
extern errno;


/* open a TCP stream to host for a service */
open_tcp(host, service)
	char *host, *service;
{
	struct hostent *hostp = NULL;
	struct servent *servp = NULL;
	int port;		/* really a network byte order object */

	int s = socket(AF_INET, SOCK_STREAM, 0);

	if (s == -1)
		return s;
	if (isdigit(*host)) {
		struct in_addr sa;

		if ((sa.s_addr = inet_addr(host)) != -1)
			hostp = gethostbyaddr(&sa, sizeof sa, AF_INET);
	} else
		hostp = gethostbyname(host);
	if (hostp == NULL)
		return -2;
	if (isdigit(*service))
		port = htons((short)atoi(service));
	else {
		servp = getservbyname(service, "tcp");
		if (servp == NULL)
			return -3;
		port = servp->s_port;
	}
	if (connecttohost(s, hostp, port) == -1)
		return -1;
	return s;
}

/* connect socket to host at specified port */
connecttohost(s, hostent, port)
	struct hostent *hostent;
{
	struct sockaddr_in sktaddr;
	struct in_addr in_addr;

	bzero(&sktaddr, sizeof sktaddr);
	errno = 0;

	/*
	 * copy the bytes 
	 */
	bcopy(hostent->h_addr, &in_addr, hostent->h_length);

	sktaddr.sin_addr = in_addr;
	sktaddr.sin_family = AF_INET;
	sktaddr.sin_port = port;	/* must match the receiver */

	/*
	 * connect to the receiver 
	 */
	return (connect(s, &sktaddr, sizeof(sktaddr)));
}

/* bind socket at host to port */
bindtohost(s, hostent, port)
	struct hostent *hostent;
{
	struct sockaddr_in sktaddr;
	struct in_addr in_addr;

	bzero(&sktaddr, sizeof sktaddr);

	errno = 0;

	/*
	 * copy the bytes 
	 */
	bcopy(hostent->h_addr, &in_addr, hostent->h_length);

	sktaddr.sin_addr = in_addr;
	sktaddr.sin_family = AF_INET;
	sktaddr.sin_port = port;	/* must match the sender */
	return (bind(s, &sktaddr, sizeof(sktaddr)));
}
