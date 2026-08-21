/*
 * RTAPE protocol definitions
 */
#define LOGIN		1
#define MOUNT		2
#define PROBE		3
#define READ		4
#define WRITE		5
#define	REWIND		6
#define REWIND_SYNC	7
#define OFFLINE		8
#define FILEPOS		9
#define BLOCKPOS	10
#define WRITE_EOF	12
#define CLOSE		13
#define LOGIN_RESPONSE	33
#define DATA		34
#define EOFREAD		35
#define STATUS		36

#define DLEN		16
#define MAXSTRING	100
struct tape_status {
	char t_version;		/* protocol version */
	char t_probeid[2];	/* Id in corresponding PROBE */
	char t_read[3];		/* Number of blocks read */
	char t_skipped[3];	/* Number of blocks skipped */
	char t_discarded[3];	/* Numbert of writes discarded */
	char t_lastop;		/* last opcode received */
	char t_density[2];	/* Density in BPI */
	char t_retries[2];	/* number of retries in last op. */
	char t_namelength;	/* length of next string */
	char t_drive[DLEN];	/* Drive name in use. */
	char t_solicited:1;	/* This status was asked for */
	char t_bot:1;		/* At BOT */
	char t_pasteot:1;	/* Past EOT */
	char t_eof:1;		/* Last op reached EOF */
	char t_nli:1;		/* Not logged in */
	char t_mounted:1;	/* Tape is mounted */
	char t_message:1;	/* Error message follows */
	char t_harderr:1;	/* Hard error encountered */
	char t_softerr:1;	/* Soft errors encountered */
	char t_offline:1;	/* Drive is offline */
	char t_string[MAXSTRING];	/* Error message */
} ts;

#define MAXMOUNT	CHMAXDATA	/* Maximum size of mount command */
#define DEFBLOCK	4096	/* Default tape buffer size */
