main(argc, argv)
	int argc;
	char **argv;
{
	register char **ap = glob(argv[1]);

	while (ap && *ap)
		printf("'%s'\n", *ap++);
}

fatal(s)
{
	printf(s);
	exit(1);
}
