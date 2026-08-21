struct chetherinfo {
#ifdef LINUX_KERNEL
	void *prot_hook1;
	void *prot_hook2;
	void *bound_dev;
#else
	struct arpcom *che_arpcom;
#endif
};

#define ETHERTYPE_CHAOS		0x0804	/* Chaos protocol */
