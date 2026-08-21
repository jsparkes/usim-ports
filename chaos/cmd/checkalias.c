#include <stdio.h>
#include <ctype.h>
#include <hosttab.h>
#include <pwd.h>

char *checkname();

main(argc, argv)
	int argc;
	char **argv;
{
	char *s;
	int type;

	printf("%s:", argv[1]);
	s = checkname(argv[1], &type);
	printf("'%s' %d\n", s, type);
}

#define NOUSER	0
#define LOCAL	1
#define ALIAS	2
#define FOREIGN	3
#define NOHOST	4

#define isatom(x)	((x) != '@' && (x) != '%' && (x) != '.' && (x) != ':' && (x) != '!')

char *
checkname(name, type)
	char *name;
	int *type;
{
	register char *s = name;
	register char *dp;
	register char *last;
	register struct passwd *pw;
	char save;
	static int dbinit;
	struct name {
		char *dptr;
		int dsize;
	} fetch(), lhs, rhs;
	char *downcase(), *token();
	struct passwd *getpwent();

	(void)downcase(name);
	for (;;) {
		for (last = NULL, dp = token(s); *dp; dp = token(++dp))
			last = dp;
		if (last != NULL && (*last == '@' || *last == '%'))
			if (isme(last + 1))
				*last = '\0';
			else if (host_name(last + 1) == 0) {
				*type = NOHOST;
				return s;
			} else
				break;
		else
			break;
	}
	for (; *(dp = token(s)); s = ++dp)
		if (*dp != '!' && *dp != ':' && *dp != '.')
			break;
		else {
			save = *dp;
			*dp = '\0';
			if (!isme(s)) {
				*dp = save;
				break;
			}
		}
	if (*(dp = token(s))) {
		*type = FOREIGN;
		return s;
	}
	if (dbinit == 0) {
		dbminit("/usr/lib/aliases");
		dbinit++;
	}
	unquote(s);
	lhs.dptr = s;
	lhs.dsize = strlen(s) + 1;
	rhs = fetch(lhs);
	if (rhs.dptr) {
		*type = ALIAS;
		return s;
	}
	setpwent();
	while ((pw = getpwent()) != NULL)
		if (strcmp(pw->pw_name, s) == 0) {
			*type = LOCAL;
			return s;
		}
	*type = NOUSER;
	return s;
}

isme(name)
	char *name;
{
	static struct host_entry *me;
	register char **ap;

	if (!me)
		me = host_here();
	if (strcmp(name, me->host_name) == 0)
		return 1;
	for (ap = me->host_nicnames; *ap; ap++)
		if (strcmp(name, *ap) == 0)
			return 1;
	return 0;
}

char *
downcase(string)
	char *string;
{
	register char *cp;

	for (cp = string; *cp; cp++)
		if (isupper(*cp))
			*cp = tolower(*cp);
	return string;
}

char *
token(cp)
	register char *cp;
{
	while (*cp && isatom(*cp)) {
		if (*cp == '\\' && cp[1])
			cp++;
		else if (*cp == '"')
			while (*++cp && *cp != '"')
				if (*cp == '\\' && cp[1])
					cp++;
		cp++;
	}
	return cp;
}

unquote(s)
	register char *s;
{

	while (*s)
		if (*s == '\\' && s[1])
			strcpy(s, s + 1);
		else if (*s == '"') {
			strcpy(s, s + 1);
			while (*s) {
				if (*s == '\\' && s[1])
					strcpy(s, s + 1);
				else if (*s == '"') {
					strcpy(s, s + 1);
					break;
				}
				s++;
			}
		} else
			s++;
}
