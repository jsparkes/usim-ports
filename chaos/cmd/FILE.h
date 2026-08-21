/*
 * Protocol errors
 */
struct file_error {
	char *e_code;		/* Standard three letter code */
	char *e_string;		/* Standard error message string */
};

/*
 * Types of error as reported in error().
 */
#define E_COMMAND	'C'	/* Command error */
#define	E_FATAL		'F'	/* Fatal transfer error */
#define	E_RECOVERABLE	'R'	/* Recoverable transfer error */

enum
{
	_000 = 0,
	ATD	= 1,
	ATF	= 2,
	DND	= 3,
	IOD	= 4,
	IOL	= 5,
	IBS	= 6,
	IWC	= 7,
	RAD	= 8,
	REF	= 9,
	WNA	= 10,
	ACC	= 11,
	BUG	= 12,
	CCD	= 13,
	CCL	= 14,
	CDF	= 15,
	CIR	= 16,
	CRF	= 17,
	CSP	= 18,
	DAE	= 19,
	DAT	= 20,
	DEV	= 21,
	DNE	= 22,
	DNF	= 23,
	FAE	= 24,
	FNF	= 25,
	FOO	= 26,
	FOR	= 27,
	HNA	= 28,
	ICO	= 29,
	IP	= 30,
	IPS	= 31,
	IPV	= 32,
	LCK	= 33,
	LNF	= 34,
	LIP	= 35,
	MSC	= 36,
	NAV	= 37,
	NER	= 38,
	NET	= 39,
	NFS	= 40,
	NLI	= 41,
	NMR	= 42,
	UKC	= 43,
	UKP	= 44,
	UNK	= 45,
	UUO	= 46,
	WKF	= 47,
};

/*
 * Definitions of errors
 * If errstring is not set before calling error(), the string below is used.
 * This macro stuff is gross but necessary.
 */
#ifdef DEFERROR
#define ERRDEF(x,y) [x] = { #x , y }
static struct file_error errors[] = {
/*	code	message */
	[_000] = { "000", "No error" },
	ERRDEF(ATD, "Incorrect access to directory"),
	ERRDEF(ATF, "Incorrect access to file"),
	ERRDEF(DND, "Dont delete flag set"),
	ERRDEF(IOD, "Invalid operation for directory"),
	ERRDEF(IOL, "Invalid operation for link"),
	ERRDEF(IBS, "Invalid byte size"),
	ERRDEF(IWC, "Invalid wildcard"),
	ERRDEF(RAD, "Rename across directories"),
	ERRDEF(REF, "Rename to existing file"),
	ERRDEF(WNA, "Wildcard not allowed"),
	ERRDEF(ACC, "Access error"),
	ERRDEF(BUG, "File system bug"),
	ERRDEF(CCD, "Cannot create directory"),
	ERRDEF(CCL, "Cannot create link"),
	ERRDEF(CDF, "Cannot delete file"),
	ERRDEF(CIR, "Circular link"),
	ERRDEF(CRF, "Rename failure"),
	ERRDEF(CSP, "Change property failure"),
	ERRDEF(DAE, "Directory already exists"),
	ERRDEF(DAT, "Data error"),
	ERRDEF(DEV, "Device not found"),
	ERRDEF(DNE, "Directory not empty"),
	ERRDEF(DNF, "Directory not found"),
	ERRDEF(FAE, "File already exists"),
	ERRDEF(FNF, "File not found"),
	ERRDEF(FOO, "File open for output"),
	ERRDEF(FOR, "Filepos out of range"),
	ERRDEF(HNA, "Host not available"),
	ERRDEF(ICO, "Inconsistent options"),
	[IP] = { "IP?", "Invalid password" },
	ERRDEF(IPS, "Invalid pathname syntax"),
	ERRDEF(IPV, "Invalid property value"),
	ERRDEF(LCK, "File locked"),
	ERRDEF(LNF, "Link target not found"),
	ERRDEF(LIP, "Login problems"),
	ERRDEF(MSC, "Misc problems"),
	ERRDEF(NAV, "Not available"),
	ERRDEF(NER, "Not enough resources"),
	ERRDEF(NET, "Network lossage"),
	ERRDEF(NFS, "No file system"),
	ERRDEF(NLI, "Not logged in"),
	ERRDEF(NMR, "No more room"),
	ERRDEF(UKC, "Unknown operation"),
	ERRDEF(UKP, "Unknown property"),
	ERRDEF(UNK, "Unknown user"),
	ERRDEF(UUO, "Unimplemented option"),
	ERRDEF(WKF, "Wrong kind of file"),
};
#endif

/*
 * Fatal internal error messages
 */
#define NOMEM		"Out of memory"
#define BADSYNTAX	"Bad syntax definition in program"
#define CTLWRITE	"Write error on control connection"
#define BADFHLIST	"Bad file_handle list"
#define FSTAT		"Fstat failed"

/*
 * Our own error messages for error responses,
 */
#define WRITEDIR	"Access denied for modifying directory."
#define SEARCHDIR	"Access denied for searching directory."
#define READDIR		"Access denied for reading directory."
#define WRITEFILE	"Access denied for writing file."
#define READFILE	"Access denied for reading file."
#define PERMFILE	"Access denied on file."
#define PATHNOTDIR	"One of the pathname components is not a directory."
#define MISSDIR		"Directory doesn't exist."
#define MISSBRACK	"Missing ] in wild card syntax"

extern char errtype;		/* Error type if not E_COMMAND */
extern char *errstring;		/* Error message if non-standard */

#define ERRSIZE 100
extern char errbuf[ERRSIZE + 1];	/* Buffer for building error messages */
extern int globerr;		/* Error return from glob() */
