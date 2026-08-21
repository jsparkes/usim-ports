#include <stdio.h>
#include "sendsys.h"

struct txtblk **last_n();
struct txtblk **keep_from();
struct txtblk **keep_string();

main(argc, argv)
	char *argv[];
{
	struct txtblk **sends;
	struct txtblk *message;
	int n, argno;
	int reverse = 0;

	if ((sends = get_messages()) == 0) {
		fprintf(stderr, "%s: Can't open sends file\n", argv[0]);
		exit(1);
	}
	if (argc == 1) {
		print_one(sends, (int)sends[0] - 1);
		exit(0);
	}
	for (argno = 1; argno < argc; argno++)
		if (sscanf(argv[argno], "%d", &n) != 0)
			if (n < 0)
				if (argc > 2) {
					fprintf(stderr, "%s: negative argument must be sole argument\n", argv[0]);
					exit(1);
				} else {
					if (-n >= (int)sends[0])
						fprintf(stderr, "%s: Message %d not found\n", argv[0], -n);
					else
						print_one(sends, (int)sends[0] + n);
					exit(0);
			} else
				sends = last_n(sends, n);
		else if (argv[argno][0] == '-')
			if (argv[argno][1] == 'r' && argv[argno][2] == '\0')
				reverse = 1;
			else if (argv[argno][1] != 's' || argv[argno][2] != '\0') {
				fprintf(stderr, "usage: %s [user] [#] [-s text]\n", argv[0]);
				exit(1);
			} else
				keep_string(sends, argv[++argno]);
		else
			sends = keep_from(sends, argv[argno]);

	if (reverse)
		print_r(sends);
	else
		print_m(sends);
}


struct txtblk **
last_n(m_array, n)
	struct txtblk **m_array;
	int n;
{
	int m_no;

	for (m_no = (int)m_array[0] - 1; m_no > 0; m_no--)
		if (m_array[m_no] != 0)
			if (--n < 0)
				m_array[m_no] = 0;
	return (m_array);
}

struct txtblk **
keep_from(m_array, from)
	struct txtblk **m_array;
	char *from;
{
	int m_no;

	for (m_no = 1; m_no < (int)m_array[0]; m_no++)
		if (m_array[m_no] != 0)
			if (stindex(mfrom(m_array[m_no]), from) == -1)
				m_array[m_no] = 0;
	return (m_array);
}

struct txtblk **
keep_string(m_array, s)
	struct txtblk **m_array;
	char *s;
{
	int m_no;

	for (m_no = 1; m_no < (int)m_array[0]; m_no++)
		if (m_array[m_no] != 0)
			if (stindex(m_array[m_no]->text, s) == -1)
				m_array[m_no] = 0;
	return (m_array);
}


print_m(m_array)
	struct txtblk **m_array;
{
	int m_no;

	for (m_no = 1; m_no < (int)m_array[0]; m_no++)
		if (m_array[m_no] != NULL)
			print_one(m_array, m_no);
}


print_r(m_array)
	struct txtblk **m_array;
{
	int m_no;

	for (m_no = (int)m_array[0] - 1; m_no >= 1; m_no--)
		if (m_array[m_no] != NULL)
			print_one(m_array, m_no);
}


print_one(m_array, n)
	struct txtblk **m_array;
	int n;
{
	printf("[#%d] %s\n", (int)m_array[0] - n, m_array[n]->text);
}

stindex(s, t)
	char s[], t[];
{
	int i, j, k;

	for (i = 0; s[i] != '\0'; i++) {
		for (j = i, k = 0; t[k] != '\0' && s[j] == t[k]; j++, k++);
		if (t[k] == '\0')
			return (i);
	}
	return (-1);
}
