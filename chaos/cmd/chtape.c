#include <stdio.h>
#include <sys/types.h>
#include <sys/mtio.h>
#include <sys/chaos.h>
#include "record.h"
#include "RTAPE.h"

/*
 * Chaos remote tape user end.
 */
char *malloc(), *sprintf();
char *ibuf, *hostname;
int debug, scan, verbose, addr, writing, maxblock, netfd, density = 1600, contin, file, tapeout;
char obuf[MAXMOUNT];
long c3tol();

main(argc, argv)
	int argc;
	char **argv;

{
	register int len, op;
	register char **ap;
	struct record_stream *rs;
	struct chstatus chst;

	for (ap = &argv[1]; *ap; ap++)
		if (ap[0][0] == '-')
			switch (ap[0][1]) {
			case 'w':
				writing++;
				break;
			case 'b':
				maxblock = atoi(&ap[0][2]);
				break;
			case 'd':
				density = atoi(&ap[0][2]);
				break;
			case 'D':
				debug++;
				break;
			case 'v':
				verbose++;
				break;
			case 's':
				scan++;
				break;
			case 'c':
				contin++;
				break;
			case 'f':
				file = atoi(&ap[0][2]);

				break;
			case 't':
				tapeout++;
				continue;
			default:
				fprintf(stderr, "Unknown flag: %s\n", *ap);
				exit(1);
		} else if (hostname) {
			fprintf(stderr, "Only one host please!\n");
			exit(1);
		} else
			hostname = *ap;
	if (hostname == NULL) {
		fprintf(stderr, "Usage is: chtape [-w] [-bblocksize] host\n");
		exit(1);
	}
	if ((addr = chaos_addr(hostname)) == 0) {
		fprintf(stderr, "Unknown host: %s\n", hostname);
		exit(1);
	}
	if (debug)
		fprintf(stderr, "Connecting to: %s\n", hostname);
	if ((netfd = chopen(addr, CHAOS_RTAPE, 2, 1, 0, 0, 0)) < 0) {
		fprintf(stderr, "Can't connect to host: %s\n", hostname);
		exit(1);
	}
	ioctl(netfd, CHIOCSWAIT, CSRFCSENT);
	ioctl(netfd, CHIOCGSTAT, &chst);
	if (chst.st_state != CSOPEN) {
		if (chst.st_ptype == CLSOP || chst.st_ptype == LOSOP) {
			ioctl(netfd, CHIOCPREAD, obuf);
			obuf[chst.st_plength] = '\0';
			fprintf(stderr, "chtape: %s\n", obuf);
		} else
			fprintf(stderr, "chtape: connection refused:%o,%o.\n", chst.st_ptype, chst.st_state);
		exit(1);
	}
	if ((rs = recopen(netfd, 2)) == NULL) {
		fprintf(stderr, "Can't open stream to host: %s\n", hostname);
		exit(1);
	}
	if ((ibuf = malloc(maxblock)) == NULL) {
		fprintf(stderr, "Can't allocate input buffer of %d bytes.\n", maxblock);
		exit(1);
	}
	if (writing) {
		fprintf(stderr, "Sorry, loser, no writing yet.\n");
		exit(1);
	} else {
		long nblocks;

		sprintf(obuf, "READ ANY 0 %d %d", maxblock, density);
		recwrite(rs, MOUNT, obuf, strlen(obuf));
		recforce(rs);
		if (file) {
			char nfiles[10];

			sprintf(nfiles, "%d", file);

			recwrite(rs, FILEPOS, nfiles, strlen(nfiles));
		}
	      loop:
		nblocks = 0;
		recwrite(rs, READ, "", 0);
		recforce(rs);
		while ((op = recop(rs)) == DATA) {
			nblocks++;
			len = recread(rs, ibuf, maxblock);
			if (debug)
				fprintf(stderr, "Read returns %d\n", len);

			if (len <= 0) {
				fprintf(stderr, "Bad read length: %d\n", len);
				exit(1);
			}
			if (verbose)
				fprintf(stderr, "Read block: %5d\n", len);
			if (!scan && write(1, ibuf, len) != len) {
				fprintf(stderr, "Output write error\n");
				exit(1);
			}
		}
		if (op == EOFREAD) {
			fprintf(stderr, "EOF encountered after %d blocks\n", nblocks);
			if (nblocks != 0 && contin) {
				if (tapeout) {
					struct mtop mtop;

					mtop.mt_op = MTWEOF;
					mtop.mt_count = 1;
					if (ioctl(1, MTIOCTOP, &mtop) < 0) {
						fprintf(stderr, "Write EOF error on output tape.\n");
						exit(1);
					}
				}
				goto loop;
			}
		} else if (op == STATUS) {
			if ((len = recread(rs, &ts, sizeof(ts))) <= 0)
				fprintf(stderr, "Couldn't read status record\n");
			else {
				((char *)&ts)[len] = '\0';
				printstatus();
			}
		} else
			fprintf(stderr, "Error encountered: %d\n", op);
	}
}

printstatus()
{
	fprintf(stderr, "Status Version: %d, id: %d\n", ts.t_version, c2toi(ts.t_probeid));
	fprintf(stderr, " Read: %D, Skipped %D, Discarded %D, Lastop: %d\n", c3tol(ts.t_read), c3tol(ts.t_skipped), c3tol(ts.t_discarded), ts.t_lastop);
	fprintf(stderr, " Density: %d, Retries: %d, Drive: %.*s\n", c2toi(ts.t_density), c2toi(ts.t_retries), ts.t_namelength, ts.t_drive);
	fprintf(stderr, " Solicited: %d, BOT: %d, PastEOT: %d, EOF: %d\n", ts.t_solicited, ts.t_bot, ts.t_pasteot, ts.t_eof);
	fprintf(stderr, " NotLoggedIn: %d, Mounted: %d, Hard: %d, Soft: %d\n", ts.t_nli, ts.t_mounted, ts.t_harderr, ts.t_softerr);
	fprintf(stderr, " Offline: %d, Message: '%.*s'\n", ts.t_offline, ts.t_string);
}

itoc2(i, cp)
	register int i;
	register char *cp;
{
	*cp++ = i;
	i >>= 8;
	*cp++ = i;
}

itoc3(l, cp)
	register long l;
	register char *cp;
{

	*cp++ = l;
	l >>= 8;
	*cp++ = l;
	l >>= 8;
	*cp++ = l;
}

c2toi(cp)
	register char *cp;
{
	register int i;

	i = *cp++ & 0377;
	i <<= 8;
	i |= *cp & 0377;
}

long
c3tol(cp)
	register char *cp;
{
	register long l;

	l = *cp++ & 0377;
	l |= (*cp++ & (long)0377) << 8;
	l |= (*cp & (long)0377) << 16;
}
