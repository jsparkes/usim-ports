main(argc, argv)
	char **argv;
{
	unsigned short val;
	int f;

	val = (unsigned short)(oatoi(argv[2]));
	f = chopen(oatoi(argv[1]), "ex ", 2, 0, &val, 2, 0);

	if (f < 0)
		exit(printf("no response\n"));
	read(f, &val, 2);;
	printf("%o\n", (int)val);
}

oatoi(p)
	char *p;
{
	unsigned i;

	sscanf(p, "%o", &i);
	return i;
}
