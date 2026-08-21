/* tape-controller.c --- Western Peripherals Model TC-131 Tape Controller
 *
 * ref: Western Peripherals Model TC-131 Tape Controller Hardware Manual
 */

#include <assert.h>
#include <err.h>
#include <pthread.h>
#include <sched.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include <unistd.h>

#include "bus-adaptor.h"
#include "bus-interface.h"
#include "misc.h"
#include "tape-controller.h"
#include "tape-drive.h"
#include "ucode.h"
#include "utrace.h"

#define UNIBUS_INTERRUPT_VECTOR 0224

#define BIT(R, POS) ((((uint16_t) (R >> POS)) & 1) != 0)
#define BUS(R, START, END) (((uint16_t) (R >> END)) & (((uint16_t)1 << (START - END + 1)) - 1))

// the commands as written in TC-131 hardware manual
__attribute__((unused)) static const char* COMMAND_STRS[] =
{
    "Off Line",
    "Read",
    "Write",
    "Write EOF",
    "Space Forward",
    "Space Reverse",
    "Write/E.R.G.",
    "Rewind"
};

static pthread_t tape_controller_thread;
static bool tape_controller_run_flag;

static pthread_cond_t tape_controller_go_cond;
static pthread_mutex_t tape_controller_go_mutex;

#ifndef NDEBUG

// MTS deconstructed for debugging
static struct
{
    // bit 0 to 15
    bool tur;
    bool rew;
    bool wrl;
    bool tsd;
    // bit 4 unused
    bool bot;
    bool selr;
    bool nxm;
    bool bte;
    bool rle;
    bool eot;
    bool bgl;
    bool pae;
    bool crc;
    bool eof;
    bool ilc;
} mts_debug;

// MTC deconstructed for debugging
static struct
{
    // bit 0 to 15
    bool go;
    uint8_t fun; // 3 bits 0-7
    bool xba;
    bool yba;
    bool int_enb;
    bool cur;
    uint8_t slt; // 3 bits 0-7
    bool penv;
    bool pclr;
    bool den5;
    bool den8;
    bool err;
} mtc_debug;

#endif

// MTS=STATUS REGISTER
// this is also called as STATUS in LISPM
// see also encode_mts function
static struct 
{
    bool ilc;
    bool eof;
    // crc error never happens in usim, always false
    // parity error never happens in usim, always false
    // bus grant late never happens in usim, always false
    bool eot;
    bool rle;
    bool bte;
    bool nxm;
    bool selr;
    // bot is read from the selected tape drive
    // 7ch bit is not used, it is always false
    // sdwn is not used in usim, always false
    // wrl bit is read from the selected tape drive (=read_only)
    // rws bit is read from the selected tape drive (=rws)
    // tur bit is read from the selected tape drive (=ready)

} mts;

// MTC=COMMAND REGISTER
// this is also called as COMMAND in LISPM
static struct
{
    // err is set as a function of bits 7-15 of mts
    bool den8; // density / packing-mode
    bool den5; // density / packing-mode
    bool pevn; // lateral parity
    uint32_t slt; // drive select
    bool cur; // controller ready
    bool int_enb; // interrupt enable
    // extended byte address, yba and xba are stored in mtcma
    uint32_t fun;
    // go is not stored
} mtc;

// MTBRC=BYTE/RECORD COUNT REGISTER
// this is also called as BYTEC in LISPM
// signed because 2's compliment number is stored 
static int16_t mtbrc;

// MTCMA=CURRENT MEMORY ADDRESS REGISTER
// this is also called as CMA in LISPM
// this is actually 18-bit but because Unibus is 16-bit
// the most significant 2 bits (17. and 16. bits) are set through MTC
static uint32_t mtcma;

// MTD=DATA BUFFER
// this is also called as BFR in LISPM
// used for diagnostic purposes
// not relevant for usim because it contains CRC and LRC in tape
static uint16_t mtd;

// MTRD=TU-10 READ LINES
// this is also called as DRD in LISPM
// used for diagnostic purposes and for additional status information
// not relevant for usim
// if IBM packing mode needs to be supported, this has to be implemented
static uint16_t mtrd;

#define SELECTED_DRIVE() (tape_drives + mtc.slt)

static void
tape_controller_assert_unibus_interrupt(void)
{
    if (mtc.int_enb)
    {
        assert_unibus_interrupt(UNIBUS_INTERRUPT_VECTOR);
    }
}

static bool
tape_controller_is_mtc_err(void)
{
    // crc, pae and bgl are always false
    return mts.ilc || mts.eof || mts.eot || mts.rle || mts.bte || mts.nxm;
}

static void
tape_controller_reset_mtc_err(void)
{
    mts.ilc = false;
    mts.eof = false;
    mts.eot = false;
    mts.rle = false;
    mts.bte = false;
    mts.nxm = false;
}

#ifdef NDEBUG
static void tape_controller_debug_mts(uint16_t v __attribute__((unused))) {}
static void tape_controller_debug_mtc(uint16_t v __attribute__((unused))) {}
#else
static void
tape_controller_debug_mts(uint16_t v)
{
    mts_debug.tur = BIT(v, 0);
    mts_debug.rew = BIT(v, 1);
    mts_debug.wrl = BIT(v, 2);
    mts_debug.tsd = BIT(v, 3);
    // bit 4 is unused
    mts_debug.bot = BIT(v, 5);
    mts_debug.selr = BIT(v, 6);
    mts_debug.nxm = BIT(v, 7);
    mts_debug.bte = BIT(v, 8);
    mts_debug.rle = BIT(v, 9);
    mts_debug.eot = BIT(v, 10);
    mts_debug.bgl = BIT(v, 11);
    mts_debug.pae = BIT(v, 12);
    mts_debug.crc = BIT(v, 13);
    mts_debug.eof = BIT(v, 14);
    mts_debug.ilc = BIT(v, 15);

    DEBUG(TRACE_TAPE, "tape-controller: MTS=#o%o=%s %s %s %s %s %s %s %s %s %s %s %s %s %s %s\n",
        v,
        F2S(mts_debug.ilc), F2S(mts_debug.eof), F2S(mts_debug.crc),
        F2S(mts_debug.pae), F2S(mts_debug.bgl), F2S(mts_debug.eot),
        F2S(mts_debug.rle), F2S(mts_debug.bte), F2S(mts_debug.nxm),
        F2S(mts_debug.selr), F2S(mts_debug.bot), F2S(mts_debug.tsd),
        F2S(mts_debug.wrl), F2S(mts_debug.rew), F2S(mts_debug.tur));
}

static void
tape_controller_debug_mtc(uint16_t v)
{
    mtc_debug.go = BIT(v, 0);
    mtc_debug.fun = BUS(v, 3, 1);
    mtc_debug.xba = BIT(v, 4);
    mtc_debug.yba = BIT(v, 5);
    mtc_debug.int_enb = BIT(v, 6);
    mtc_debug.cur = BIT(v, 7);
    mtc_debug.slt = BUS(v, 10, 8);
    mtc_debug.penv = BIT(v, 11);
    mtc_debug.pclr = BIT(v, 12);
    mtc_debug.den5 = BIT(v, 13);
    mtc_debug.den8 = BIT(v, 14);
    mtc_debug.err = BIT(v, 15);

    DEBUG(TRACE_TAPE, "tape-controller: MTC=#o%o=%s %s %s %s %s slt:%d %s %s %s %s fun:%d %s\n",
        v,
        F2S(mtc_debug.err), F2S(mtc_debug.den8), F2S(mtc_debug.den5),
        F2S(mtc_debug.pclr), F2S(mtc_debug.penv), mtc_debug.slt,
        F2S(mtc_debug.cur), F2S(mtc_debug.int_enb), F2S(mtc_debug.yba),
        F2S(mtc_debug.xba), mtc_debug.fun, F2S(mtc_debug.go));
}
#endif

static uint16_t
tape_controller_encode_mts(void)
{
    // tape controller flags

    bool ilc    = mts.ilc;
    bool eof    = mts.eof;
    // crc error never happens in usim
    bool cre    = false;
    // parity error never happens in usim
    bool pae    = false;
    // bgl never happens in usim
    bool bgl    = false;
    bool eot    = mts.eot;
    bool rle    = mts.rle;
    bool bte    = mts.bte;
    // non existent memory
    bool nxm    = mts.nxm;
    bool selr   = mts.selr;

    // status bits 00-05 are cleared or set by the 
    // master tape transport, not the controller

    struct tape_drive_s* p = SELECTED_DRIVE();

    bool bot    = p->bot;
    // seven channel bit is not used, always 0
    // sdwn does not make sense (?) in usim, always 0
    bool sdwn   = false;
    bool wrl    = p->read_only;
    bool rws    = p->rws;
    bool tur    = p->ready;

    uint16_t v = 0;

    if (ilc)    v |= (1 << 15);
    if (eof)    v |= (1 << 14);
    if (cre)    v |= (1 << 13);
    if (pae)    v |= (1 << 12);
    if (bgl)    v |= (1 << 11);
    if (eot)    v |= (1 << 10);
    if (rle)    v |= (1 << 9);
    if (bte)    v |= (1 << 8);
    if (nxm)    v |= (1 << 7);
    if (selr)   v |= (1 << 6);
    if (bot)    v |= (1 << 5);
    if (sdwn)   v |= (1 << 3);
    if (wrl)    v |= (1 << 2);
    if (rws)    v |= (1 << 1);
    if (tur)    v |= (1 << 0);

    return v;
}

static uint16_t
tape_controller_encode_mtc(void)
{
    uint16_t v = 0;

    if (tape_controller_is_mtc_err())  v |= (1 << 15);

    if (mtc.den8)           v |= (1 << 14);
    if (mtc.den5)           v |= (1 << 13);
    // bit 12 - PCLR/power clear is always read back by the processor as zero
    if (mtc.pevn)           v |= (1 << 11);
    v |= (mtc.slt << 8);
    if (mtc.cur)            v |= (1 << 7);
    if (mtc.int_enb)        v |= (1 << 6);
    // extended address in bits 5 and 4, kept in MTCMA
    v |= (((mtcma >> 16) & 0b11) << 4);
    // function in bits 3 to 1
    v |= (mtc.fun << 1);
    // go is in bit 0 but it automatically resets

    return v;
}

static void
tape_controller_update_mtc(uint16_t v)
{
    mtc.den8    = BIT(v, 14);
    mtc.den5    = BIT(v, 13);
    // not saved, read as 0
    const bool power_clear  = BIT(v, 12);
    mtc.pevn    = BIT(v, 11);
    mtc.slt     = BUS(v, 10, 8);    
    mtc.int_enb = BIT(v, 6);
    // not saved in mtc but mtcma and read from there
    const uint32_t ea   = BUS(v, 5, 4);
    mtc.fun     = BUS(v, 3, 1);
    // not saved, toggled back to false anyway
    const bool go       = BIT(v, 0);

    mtcma = (mtcma & 0x0000FFFF) | (ea << 16);

    // "the Select Remote bit is set when the addressed tape drive is on line."
    mts.selr = SELECTED_DRIVE()->online;

    if (power_clear)
    {
        DEBUG(TRACE_TAPE, "tape-controller: mtc signals power clear\n");
        tape_controller_bus_reset();
        // bus reset sets controller ready
    }
    else 
    {
        if (go) 
        {            
            DEBUG(TRACE_TAPE, "tape-controller: mtc signals go\n");
            pthread_cond_broadcast(&tape_controller_go_cond);
            // controller ready is set in go thread
        }
        else
        {
            // "an interrupt occurs for an instruction that sets the INT ENB bit
            // but does not set the GO bit"
            tape_controller_assert_unibus_interrupt();
        }
    }
}

static void
tape_controller_offline(struct tape_drive_s* p)
{
    tape_drive_rewind_and_offline(p);    
}

// this code does not handle eof/eot cases
static void
tape_controller_read(struct tape_drive_s* p)
{
    // for read operations
    // mtbrc is set to the 2's complement of a number
    // equal to or greater than the maximum expected record length

    bus_interface_reset_bus_error_status();

    DEBUG(TRACE_TAPE, "tape-controller: read mtbrc=%d, mtcma=#o%o\n", mtbrc, mtcma);

    uint32_t preamble;
    tape_drive_read_uint32(p, &preamble);

    DEBUG(TRACE_TAPE, "tape-controller: read preamble=0x%08x\n", preamble);

    if (preamble == TAPE_FILE_MARK)
    {
        DEBUG(TRACE_TAPE, "tape-controller: file mark\n");
        mts.eof = true;
        return;
    }
    else
    {
        uint32_t record_length = preamble;

        DEBUG(TRACE_TAPE, "tape-controller: record length=%u\n", record_length);

        while (mtbrc < 0 && record_length > 0)
        {
            uint8_t v1;
            tape_drive_read_uint8(p, &v1);
            mtbrc++;
            DEBUG(TRACE_TAPE, "tape-controller: mtbrc=%d\n", mtbrc);
            record_length--;

            uint8_t v2;
            tape_drive_read_uint8(p, &v2);
            mtbrc++;
            DEBUG(TRACE_TAPE, "tape-controller: mtbrc=%d\n", mtbrc);
            record_length--;

            // little-endian
            uint16_t data = ((uint16_t)v2 << 8) | (uint16_t)v1;
            
            bus_adaptor_unibus_write(mtcma, data);

            if (bus_interface_is_unibus_map_error())
            {
                WARNING(TRACE_TAPE, "tape-controller: unibus map error, mtcma=#o%o\n", mtcma);
                mts.nxm = true;                
                break;
            }
            else if (bus_interface_is_unibus_nxm())
            {
                WARNING(TRACE_TAPE, "tape-controller: unibus nxm error, mtcma=#o%o\n", mtcma);
                mts.nxm = true;
                break;
            }
            else
            {
                DEBUG(TRACE_TAPE, "tape-controller: write to mtcma=#o%o v=#o%o\n", mtcma, data);
            }

            // update mtcma here because after BGL and NXM error conditions
            // mtcma contains the address of the location in which the failure occurred
            mtcma += 2;
        }

        if (mts.nxm)
        {
            //TODO: move tape to interrecord gap (next record start)
        }
        // record length error (RLE) occurs when the actual record length
        // is greater than the allocated memory
        // i.e. MTBRC overflows before EOF detected            
        else if (record_length > 0)
        {
            INFO(TRACE_TAPE, "tape-controller: record length error\n");
            mts.rle = true;
        }
        else
        {
            uint32_t postamble;
            tape_drive_read_uint32(p, &postamble);
            DEBUG(TRACE_TAPE, "tape-controller: read postamble=0x%08x\n", postamble);

            uint32_t maybe_filemark;
            tape_drive_read_uint32(p, &maybe_filemark);
            if (maybe_filemark == TAPE_FILE_MARK)
            {
                DEBUG(TRACE_TAPE, "tape-controller: file mark\n");
                mts.eof = true;
            }
            else
            {
                // no file mark, roll back
                tape_drive_seek(p, -4, SEEK_CUR);
            }

        }
    }
}

// this code does not handle eof/eot cases
static void
tape_controller_write(struct tape_drive_s* p)
{
    // ill_com is set on any write command on a drive which is file protected
    if (p->read_only)
    {
        INFO(TRACE_TAPE, "tape-controller: trying to write but tape %u is write protected\n", p->unit);
        mts.ilc = true;
        return;
    }
    
    // for write and write with extended record gap operations
    // mtbrc is set to the 2's complement of the number of bytes 
    // to be transferred from memory to tape

    DEBUG(TRACE_TAPE, "tape-controller: write mtbrc=%d, mtcma=#o%o\n", mtbrc, mtcma);

    const size_t record_length = -mtbrc;

    // "The minimum record length is two words or four data characters."
    if (record_length < 4)
    {
        // less than 4
        WARNING(TRACE_TAPE, "tape-controller: record length is less than 4, record length=%d\n", record_length);
        mts.ilc = true;
        return;
    }
    // this is not a limitation of wesperco but on LISPM memory addressing is word based
    else if ((record_length & 0x3) != 0)
    {
        // not a multiple of 4
        WARNING(TRACE_TAPE, "tape-controller: record length is not a multiple of 4, record length=%d\n", record_length);
        mts.ilc = true;
        return;
    }
    else
    {
        bus_interface_reset_bus_error_status();

        DEBUG(TRACE_TAPE, "tape-controller: write preamble=0x%08x\n", record_length);
        tape_drive_write_record_length(p, record_length);

        bus_interface_reset_bus_error_status();
        
        while (mtbrc < 0)
        {
            // read from memory
            uint16_t data;
            bus_adaptor_unibus_read(mtcma, &data);

            if (bus_interface_is_unibus_map_error())
            {
                WARNING(TRACE_TAPE, "tape-controller: unibus map error, mtcma=#o%o\n", mtcma);
                mts.nxm = true;                
                break;
            }
            else if (bus_interface_is_unibus_nxm())
            {
                WARNING(TRACE_TAPE, "tape-controller: unibus nxm error, mtcma=#o%o\n", mtcma);
                mts.nxm = true;
                break;
            }
            else
            {
                DEBUG(TRACE_TAPE, "tape-controller: read from mtcma=#o%o v=#o%o\n", mtcma, data);
            }

            // update mtcma here because after BGL and NXM error conditions
            // mtcma contains the address of the location in which the failure occurred
            mtcma += 2;            

            // write to tape
            uint8_t v1 = data & 0xFF;
            uint8_t v2 = (data >> 8) & 0xFF;
            
            // little-endian
            tape_drive_write_uint8(p, v1);
            mtbrc++;
            DEBUG(TRACE_TAPE, "tape-controller: mtbrc=%d\n", mtbrc);

            tape_drive_write_uint8(p, v2);
            mtbrc++;
            DEBUG(TRACE_TAPE, "tape-controller: mtbrc=%d\n", mtbrc);
        }

        if (mts.nxm)
        {
            //TODO: move tape to interrecord gap (next record start)
        }
        else if (mtbrc == 0) 
        {
            DEBUG(TRACE_TAPE, "tape-controller: write postamble=0x%08x\n", record_length);
            tape_drive_write_record_length(p, record_length);
        }
    }
}

static void
tape_controller_write_eof(struct tape_drive_s* p)
{
    // ill_com is set on any write command on a drive which is file protected
    if (p->read_only) 
    {
        INFO(TRACE_TAPE, "tape-controller: trying to write eof but tape %u is write protected\n", p->unit);
        mts.ilc = true;
        return;
    }

    tape_drive_write_file_mark(p);
    // after writing, a read is always performed, so after writing EOF, EOF should be set
    mts.eof = true;
}

static void
tape_controller_space_forward(struct tape_drive_s* p)
{
    // for space forward and space reverse operations
    // mtbrc is set to the 2's complement of the number of records
    // to be spaced over

    // "The controller spaces forward over the given number of records
    // unless it encounters a file mark or the end of tape."

    // "To space over a file, the program can simply give a zero (maximum) byte count."

    if (mts.eot) return;

    if (mtbrc == 0)
    {
        while (!mts.eof && !mts.eot)
        {
            tape_drive_space_forward(p);
        }
    }
    else
    {
        while (mtbrc < 0)
        {
            tape_drive_space_forward(p);
            if (mts.eof || mts.eot) return;
            mtbrc++;
        }
    }
}

static void
tape_controller_space_reverse(struct tape_drive_s* p)
{
    // for space forward and space reverse operations
    // mtbrc is set to the 2's complement of the number of records
    // to be spaced over

    // "The controller spaces reverse over the given number of records,
    // but it stops the tape automatically upon encountering a file mark or
    // the load point." (load point is beginning of tape -BOT-)

    // "To space over a file, the program can simply give a zero (maximum) byte count."

    if (p->bot) return;

    if (mtbrc == 0)
    {
        // the last record might have a file mark
        // if so, it should be skipped
        // the idea is to go back to previous file mark
        // this will not set mts.eof
        tape_drive_space_reverse(p, false);

        while (!mts.eof && !p->bot)
        {
            // this will set mts.eof
            tape_drive_space_reverse(p, true);
        }
    }
    else
    {
        // similar to above, last record might have a file mark
        // this file-mark is not important for space-reverse
        tape_drive_space_reverse(p, false);
        mtbrc++;

        if (!p->bot)
        {
            while (mtbrc < 0)
            {
                tape_drive_space_reverse(p, true);
                if (mts.eof || p->bot) return;
                mtbrc++;
            }
        }
    }
}

static void
tape_controller_rewind(struct tape_drive_s* p)
{
    tape_drive_rewind(p);
    // "an interrupt occurs whenever a rewinding tape unit arrives at BOT"
    if (p->bot) tape_controller_assert_unibus_interrupt();
}

static void
tape_controller_go(void)
{
    INFO(TRACE_TAPE, "tape-controller: GO -> %s\n", COMMAND_STRS[mtc.fun]);

    tape_controller_reset_mtc_err();

    // ilc is set, 
    // when "any tape command where the selected drive is not on-line (selr bit is false)"
    if (!mts.selr)
    {
        WARNING(TRACE_TAPE, "tape-controller: GO: but selected drive is not online (SELR bit is false)\n");
        mts.ilc = true;
        return;
    }

    struct tape_drive_s* p = SELECTED_DRIVE();

    p->ready = false;

    // maybe this should be set early but it doesnt matter 
    // because bpi and eotpos are not used without commands
    // default (0) is 1600 bpi
    tape_drive_set_bpi(p, mtc.den8 ? 800 : 1600);

    switch (mtc.fun) 
    {
        case 0:    
            tape_controller_offline(p);
            break;

        case 1:
            tape_controller_read(p);
            break;

        case 2:
        // for usim there is no concept of ERG
        // so write_erg is handled as same as write
        case 6:
            tape_controller_write(p);
            break;

        case 3:
            tape_controller_write_eof(p);
            break;

        case 4:
            tape_controller_space_forward(p);
            break;

        case 5:
            tape_controller_space_reverse(p);
            break;

        case 7:
            tape_controller_rewind(p);
            break;

        default:
            errx(1, "invalid command, possible bug");
    }

    DEBUG(TRACE_TAPE, "tape-controller: GO completed\n");

    p->ready = true;
}

static void*
tape_controller_run(__attribute__((unused)) void* arg)
{
    tape_controller_run_flag = true;

    while (tape_controller_run_flag)
    {
        // wait until go is set
        pthread_mutex_lock(&tape_controller_go_mutex);
        pthread_cond_wait(&tape_controller_go_cond, &tape_controller_go_mutex);
        pthread_mutex_unlock(&tape_controller_go_mutex);

        // quit early if run is false
        if (!tape_controller_run_flag) break;

        // ilc is set, 
        // when "any tape command initiated during a tape operation (cu rdy bit is false)"
        if (!mtc.cur)
        {
            WARNING(TRACE_TAPE, "tape-controller: GO: but controller is not ready (CU RDY is false)\n");
            mts.ilc = true;
        }
        else
        {
            mtc.cur = false;
            tape_controller_go();
            mtc.cur = true;
        }

        // "an interrupt occurs whenever either 
        // the Controller Ready bit or
        // the ERR bit goes true or
        // whenever a rewinding tape unit arrives at BOT."
        tape_controller_assert_unibus_interrupt();
    }

    return NULL;
}

bool 
tape_controller_init(void)
{
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);

    if (pthread_create(&tape_controller_thread, &attr, tape_controller_run, NULL) != 0)
    {
        warnx("cannot create tape-controller thread\n");
        return false;
    }

    if (pthread_cond_init(&tape_controller_go_cond, NULL) != 0)
    {
        warnx("cannot create tape-controller go cond\n");
        return false;
    }

    if (pthread_mutex_init(&tape_controller_go_mutex, NULL) != 0)
    {
        warnx("cannot create tape-controller go mutex\n");
        return false;
    }

    NOTICE(TRACE_USIM, "tape-controller: attached to tape drive(s):");
    for (size_t i = 0; i < NUMBER_OF_TAPE_DRIVES; i++)
    {
        struct tape_drive_s* p = tape_drives + i;
        if (p->configured) NOTICE(TRACE_USIM, " %d", i);
    }
    NOTICE(TRACE_USIM, "\n");
    return true;
}

void
tape_controller_quit(void)
{
    tape_controller_run_flag = false;
    pthread_cond_broadcast(&tape_controller_go_cond);

    pthread_mutex_destroy(&tape_controller_go_mutex);
    pthread_cond_destroy(&tape_controller_go_cond);
}

void
tape_controller_bus_reset(void)
{
    memset(&mts, 0, sizeof(mts));
    memset(&mtc, 0, sizeof(mtc));
    mtbrc = 0;

    mtc.cur = true;
}

void
tape_controller_set_mts_eof(void)
{
    mts.eof = true;
}

bool
tape_controller_is_mts_eot(void)
{
    return mts.eot;
}

void
tape_controller_set_mts_eot(void)
{
    mts.eot = true;
}

void
tape_controller_reset_mts_eot(void)
{
    mts.eot = false;
}

void
tape_controller_set_mts_bte(void)
{
    mts.bte = true;
}

void 
tape_controller_unibus_read(uint32_t uaddr, uint16_t *pv)
{
    switch (uaddr)
    {
        // STATUS REGISTER / MTS
        case 0772520:
            *pv = tape_controller_encode_mts();
            DEBUG(TRACE_TAPE, "tape-controller: read MTS=#o%o\n", *pv);
            tape_controller_debug_mts(*pv);            
            break;

        // COMMAND REGISTER / MTC
        case 0772522:
            *pv = tape_controller_encode_mtc();
            DEBUG(TRACE_TAPE, "tape-controller: read MTC=#o%o\n", *pv);
            tape_controller_debug_mtc(*pv);
            break;

        // BYTE/RECORD COUNT REGISTER / MTBRC
        case 0772524:
            *pv = mtbrc;
            DEBUG(TRACE_TAPE, "tape-controller: read MTBRC=#o%o\n", *pv);
            break;

        // CURRENT MEMORY ADDRESS REGISTER / MTCMA
        case 0772526:
            // bits 17-16 of mtcma holds the extended address bits in mtc
            *pv = mtcma & 0x0000FFFF;
            DEBUG(TRACE_TAPE, "tape-controller: read MTCMA=#o%o\n", *pv);
            break;

        // DATA BUFFER REGISTER / MTD
        case 0772530:
            *pv = mtd;
            DEBUG(TRACE_TAPE, "tape-controller: read MTD=#o%o\n", *pv);
            break;

        // TU-10 READ LINES / MTRD
        case 0772532:
            *pv = mtrd;
            DEBUG(TRACE_TAPE, "tape-controller: read MTRD=#o%o\n", *pv);
            break;

        default:
			errx(1, "tape: unknown read uaddr=%06o\n", uaddr);
    }
}

void 
tape_controller_unibus_write(uint32_t uaddr, uint16_t v)
{
    switch (uaddr)
    {
        // case 0772520: status is read-only

        // COMMAND REGISTER / MTC
        case 0772522:
            DEBUG(TRACE_TAPE, "tape-controller: write #o%o to MTC\n", v);            
            tape_controller_debug_mtc(v);
            tape_controller_update_mtc(v);
            DEBUG(TRACE_TAPE, "tape-controller: MTC=#o%o\n", mtc);
            DEBUG(TRACE_TAPE, "tape-controller: MTCMA=#o%o\n", mtcma);
            break;

        // BYTE/RECORD COUNT REGISTER / MTBRC
        case 0772524:
            DEBUG(TRACE_TAPE, "tape-controller: write #o%o to MTBRC\n", v);
            mtbrc = (int16_t) v;
            DEBUG(TRACE_TAPE, "tape-controller: MTBRC=#o%o (%d)\n", mtbrc, mtbrc);
            break;

        // CURRENT MEMORY ADDRESS REGISTER / MTCMA
        case 0772526:
            DEBUG(TRACE_TAPE, "tape-controller: write #o%o to MTCMA\n", v);
            // bits 17-16 holds the extended address
            mtcma = (mtcma & 0xFFFF0000) | v;
            DEBUG(TRACE_TAPE, "tape-controller: MTCMA=#o%o\n", mtcma);
            break;

        // case 0772530: data buffer register / MTD is read-only

        // TU10 read lines / MTRD
        case 0772532:
            DEBUG(TRACE_TAPE, "tape-controller: write #o%o to MTRD\n", v);
            mtrd = v;
            DEBUG(TRACE_TAPE, "tape-controller: MTRD=#o%o\n", mtrd);
            break;

        default:
			errx(1, "tape: unknown write uaddr=%06o\n", uaddr);
    }
}
