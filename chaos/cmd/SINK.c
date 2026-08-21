#include <sys/chaos.h>

struct chpacket pkt;

main()
{
	ioctl(0, CHIOCSMODE, CHRECORD);
	while (read(0, (char *)&pkt, sizeof(pkt)) > 0);
}
