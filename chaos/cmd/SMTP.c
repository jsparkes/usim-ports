#include <sys/chaos.h>

struct chstatus chst;

main()
{
	char *fromhost, *tmp, *chaos_name(), *malloc();

	if (ioctl(0, CHIOCGSTAT, &chst) < 0)
		exit(1);

	fromhost = chaos_name(chst.st_fhost);

	tmp = malloc(strlen(fromhost) + sizeof("-oMs"));
	strcpy(tmp, "-oMs");
	strcat(tmp, fromhost);

	execl("/usr/lib/sendmail", "sendmail", "-bs", "-oMQCHAOS", "-oMrSMTP", tmp, 0);

	chreject("Could not exec /usr/lib/sendmail");
	exit();
}
