/* tape-drive.c --- Tape Drive
 *
 * ref: Western Peripherals Model TC-131 Tape Controller Hardware Manual
 *
 * The tape (media) is a file which contains records (with record lengths) and tape marks (file marks).
 * A number of records forms a file which is terminated by a tape mark (file mark).
 * The record length both preceeds (preamble) and proceeds (postamble) the record data. 
 * They contain the same value.
 *
 * TAPE = FILE*
 * FILE = (RECORD_LENGTH RECORD RECORD_LENGTH)* FILE_MARK
 *
 * RECORD = RECORD_LENGTH <as many bytes as RECORD_LENGTH> RECORD_LENGTH
 * RECORD_LENGTH = 4 bytes, but not 0x00000000 (minimum record length is 4 bytes)
 * FILE_MARK = 0x00000000
 *
 * The record length is 4 bytes, file mark is also 4 bytes.
 * The file mark is 0x00000000, if it is not file mark, it is record length.
 *
 * Tape BOT (beginning of tape) is position 0
 * Tape EOT (end of tape) is stored in eotpos because it depends on density.
 *
 * To create an empty tape media (2400 ft, 1600 bpi): 
 * dd if=/dev/zero of=tape0.img count=45000 bs=1024
 * 45000 * 1024 = 46080000 bytes
 * Because:
 * 2400 ft tape = 28800 inch
 * 9-track records 8 bits data, 1 bit parity
 * 1600 bpi (PE 9-track) * 28800 = 46'080'000 bytes
 *
 * A record (and file) can be created manually for testing like this:
 * printf '\x04\x00\x00\x00\x12\x34\x56\x78\x04\x00\x00\x00' | dd of=tape0.img bs=1 seek=0 count=12 conv=notrunc
 * This creates a 4 byte record (04 00 00 00) containing (12 34 56 78).
 */

#include <assert.h>
#include <err.h>
#include <fcntl.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include <time.h>
#include <unistd.h>

#include <sys/mman.h>
#include <sys/stat.h>

#include "tape-controller.h"
#include "tape-drive.h"
#include "ucode.h"
#include "utrace.h"

#ifndef O_BINARY
#define O_BINARY 0
#endif

#undef DEBUG_SEEKS
#undef DEBUG_READS
#undef DEBUG_WRITES

// how far (feet) End-Of-Tape marker from the end of tape
// 4 ft is a typical value
// thus, it is possible to write after EOT, but better to stop very soon
#define EOT_OFFSET_FT 4

#define BIT(R, POS) ((((uint16_t) (R >> POS)) & 1) != 0)
#define BUS(R, START, END) (((uint16_t) (R >> END)) & (((uint16_t)1 << (START - END + 1)) - 1))

struct tape_drive_s tape_drives[NUMBER_OF_TAPE_DRIVES];

// all tape units are initialized, but if they are not configured in usim.ini
// they stay as offline
void 
tape_drive_init(uint32_t unit, char *config_string)
{
    if (unit >= NUMBER_OF_TAPE_DRIVES)
    {
        errx(1, "tape-drive: %u exceeds the number of maximum tape drives (%u)\n",
                unit, NUMBER_OF_TAPE_DRIVES);
    }

    struct tape_drive_s* p = tape_drives + unit; 

    p->unit = unit;
    p->online = false;

    if (config_string == NULL) 
    {
        DEBUG(TRACE_TAPE, "tape-drive %u: offline (not configured)\n", unit);
        return;
    }

    p->configured = true;

    // parse config_string and read
    // [length_ft,][mode,]filename
    // length_ft and mode are optional
    // length_ft has to be a number and defaults to 2400 (feet)
    // mode has to be rw or ro and defaults to rw (read write)
    char* length_ft;
    char* mode;
    char* filename;

    char* token1 = strtok(config_string, ",");
    if (token1 == NULL) 
    {
        NOTICE(TRACE_TAPE, "tape-drive %u: offline (no media loaded)\n", p->unit);
        return;
    }
    else
    {
        char* token2 = strtok(NULL, ",");
        if (token2 == NULL) 
        {
            length_ft = "2400";
            mode = "rw";
            filename = token1;
        }
        else
        {
            char* token3 = strtok(NULL, ",");
            if (token3 == NULL) 
            {
                if (atoi(token1) == 0) 
                {
                    length_ft = "2400";
                    mode = token1;
                    filename = token2;
                }
                else
                {
                    length_ft = token1;
                    mode = "rw";
                    filename = token2;
                }
            }
            else
            {
                length_ft = token1;
                mode = token2;
                filename = token3;
            }
        }
    }

    if ((atoll(length_ft) == 0) ||
        ((strcmp(mode, "ro") != 0) && (strcmp(mode, "rw") != 0)) ||
        (strlen(filename) == 0))
    {
        ERR(TRACE_USIM, "tape-drive %u: configuration string %s is invalid\n",
                p->unit, config_string);
        return;
    }

    p->length_ft = atoll(length_ft);
    p->read_only = strcmp(mode, "ro") == 0 ? true : false;
    p->filename = strdup(filename);

    struct stat file_stat;
    stat(filename, &file_stat);

    // maximum possible storage is given tape length in feet * 12 inch/feet * max density=1600 bit/inch
    const off_t minimum_required_file_size = p->length_ft * 12 * 1600;

    if (file_stat.st_size < minimum_required_file_size)
    {
        WARNING(TRACE_USIM, "tape-drive %u: the size of %s is not right. Minimum %llu bytes required.\n",
                p->unit, p->filename, minimum_required_file_size);
        return;
    }

    if ((p->filename == NULL) || (strlen(p->filename) == 0))
    {
        NOTICE(TRACE_TAPE, "tape-drive %u: offline (no media loaded)\n", p->unit);
        return;
    }

    p->fd = open(p->filename, (p->read_only ? O_RDONLY : O_RDWR) | O_BINARY | O_DSYNC);

    if (p->fd < 0)
    {
        WARNING(TRACE_TAPE, "tape-drive %u: offline (cannot open tape file)\n", p->unit);
        return;
    }
  
    p->mmsz = file_stat.st_size;

    p->mm = mmap(
            NULL, p->mmsz,
            p->read_only ? PROT_READ : PROT_READ | PROT_WRITE,
            MAP_SHARED, p->fd, 0);

    if (p->mm == MAP_FAILED)
    {
        NOTICE(TRACE_TAPE, "tape-drive %u: offline (cannot memory map tape file: %s)\n",
                p->unit, p->filename);
        return;
    }

    p->ready = true;
    p->online = true;

    NOTICE(TRACE_USIM, "tape-drive %u: [%u ft, %s] online (%s)\n", 
            p->unit, 
            p->length_ft, 
            p->read_only ? "ro" : "rw",
            p->filename);
}

void
tape_drive_quit(struct tape_drive_s* p)
{
    free(p->filename);
    if (p->mm != MAP_FAILED) munmap(p->mm, p->mmsz);
    if (p->fd < 0) close(p->fd);
    p->fd = 0;
    p->online = false;
}

void
tape_drive_set_bpi(struct tape_drive_s* p, uint32_t bpi)
{
    p->bpi = bpi;
    // 1 ft = 12 inch
    p->eotpos = ((p->length_ft - EOT_OFFSET_FT) * 12 * p->bpi);
}

// to update p->pos, this function should be used
// never update p->pos directly
// this function sets/clears flags
void
tape_drive_seek(struct tape_drive_s* p, off_t offset, int whence)
{
    if (whence == SEEK_SET) 
    {
        p->pos = offset;
#ifdef DEBUG_SEEKS
        DEBUG(TRACE_TAPE, "tape %u: seek (%ld, %s) to %ld\n", 
            p->unit, offset, "SEEK_SET", p->pos);
#endif            
    }
    else if (whence == SEEK_CUR) 
    {
        p->pos = p->pos + offset;
#ifdef DEBUG_SEEKS
        DEBUG(TRACE_TAPE, "tape %u: seek (%ld, %s) to %ld\n", 
            p->unit, offset, "SEEK_CUR", p->pos);
#endif
    }
    else if (whence == SEEK_END) errx(1, "SEEK_END is not supported by tape_drive_seek");
    else errx(1, "invalid whence %d in tape_drive_seek", whence);

    // do not allow negative
    if (p->pos < 0) 
    {
        WARNING(TRACE_TAPE, "tape %u: p->pos (%ld) became negative, setting back to 0, is tape file damaged ?\n", p->unit, p->pos);
        p->pos = 0;
    }
    // do not allow past file size
    else if (p->pos > p->mmsz) 
    {
        WARNING(TRACE_TAPE, "tape %u: p->pos (%ld) became larger than file size (%ld), setting back to %ld, is tape file damaged ?\n", p->unit, p->pos, p->mmsz, p->mmsz);
        p->pos = p->mmsz;
    }

    p->bot = (p->pos == 0);

    if (p->pos >= p->eotpos) tape_controller_set_mts_eot();
    else tape_controller_reset_mts_eot();
}

// helper for rewind, since it is used in two different places
static void 
tape_drive_rewind_ex(struct tape_drive_s* p)
{
    p->rws = true;
    tape_drive_seek(p, 0, SEEK_SET);
    // "MTS.REWIND_STATUS (RWS) is cleared by the selected drive when
    // the tape arrives at BOT."
    p->rws = false;
    // "the bit will be reset when the EOT marker is passed
    // while performing a Rewind or Space Reverse operation."
    // if eot passed (eot is set), rewind will definitely pass eot again in reverse diredction
    // if eot not passed (eot is reset), it will keep its reset state
    tape_controller_reset_mts_eot();
}

// I think there is no command as offline only
// it is either rewind or rewind and offline
void 
tape_drive_rewind_and_offline(struct tape_drive_s* p)
{
    DEBUG(TRACE_TAPE, "tape %u: rewinding and going offline...\n", p->unit);

    // rewind
    tape_drive_rewind_ex(p);

    // offline
    if (p->fd < 0) close(p->fd);
    p->fd = 0;
    p->online = false;

    DEBUG(TRACE_TAPE, "tape %u: rewind and offline completed.\n", p->unit);
}

bool
tape_drive_read_uint8(struct tape_drive_s* p, uint8_t* pv)
{
    if (p->pos == p->mmsz) return false;
    *pv = p->mm[p->pos];
#ifdef DEBUG_READS
    DEBUG(TRACE_TAPE, "tape %u: read_uint8: pos=%d, value=0x%x\n", p->unit, p->pos, *pv);
#endif
    tape_drive_seek(p, 1, SEEK_CUR);
    return true;
}

bool
tape_drive_read_uint32(struct tape_drive_s* p, uint32_t* pv)
{
    uint8_t v1;
    if (!tape_drive_read_uint8(p, &v1)) return false;

    uint8_t v2;
    if (!tape_drive_read_uint8(p, &v2)) return false;

    uint8_t v3;
    if (!tape_drive_read_uint8(p, &v3)) return false;

    uint8_t v4;
    if (!tape_drive_read_uint8(p, &v4)) return false;

    // little-endian
    *pv = (((uint32_t)v4 << 24) | 
           ((uint32_t)v3 << 16) | 
           ((uint32_t)v2 << 8) | 
           ((uint32_t)v1 << 0));

    return true;
}

bool
tape_drive_write_uint8(struct tape_drive_s* p, uint8_t v)
{
    if (p->pos == p->mmsz) return false;
    p->mm[p->pos] = v;
#ifdef DEBUG_WRITES
    DEBUG(TRACE_TAPE, "tape %u: write_uint8: pos=%d, value=0x%x\n", p->unit, p->pos, v);
#endif    
    tape_drive_seek(p, 1, SEEK_CUR);
    return true;
}

bool
tape_drive_write_uint32(struct tape_drive_s* p, uint32_t v)
{
    // little-endian
    if (!tape_drive_write_uint8(p, (v >> 0) & 0xFF)) return false;
    if (!tape_drive_write_uint8(p, (v >> 8) & 0xFF)) return false;
    if (!tape_drive_write_uint8(p, (v >> 16) & 0xFF)) return false;
    if (!tape_drive_write_uint8(p, (v >> 24) & 0xFF)) return false;

    return true;
}

bool
tape_drive_write_record_length(struct tape_drive_s* p, size_t record_length)
{
    return tape_drive_write_uint32(p, record_length);
}

bool 
tape_drive_write_file_mark(struct tape_drive_s* p)
{
    return tape_drive_write_uint32(p, TAPE_FILE_MARK);
}

void 
tape_drive_space_forward(struct tape_drive_s* p)
{
    DEBUG(TRACE_TAPE, "tape %u: space forwarding...\n", p->unit);

    uint32_t preamble;
    tape_drive_read_uint32(p, &preamble);

    uint32_t record_length = preamble;
    // +4 because there is a length also at the end
    tape_drive_seek(p, record_length + 4, SEEK_CUR);

    // check if filemark follows
    uint32_t maybe_filemark;
    tape_drive_read_uint32(p, &maybe_filemark);

    if (maybe_filemark == TAPE_FILE_MARK)
    {
        DEBUG(TRACE_TAPE, "tape %u: space forward eof found\n", p->unit);
        tape_controller_set_mts_eof();
    }
    else
    {
        // it was not a filemark, so rollback
        tape_drive_seek(p, -4, SEEK_CUR);
    }

    DEBUG(TRACE_TAPE, "tape %u: space forward completed.\n", p->unit);
}

void 
tape_drive_space_reverse(struct tape_drive_s* p, bool stop_at_filemark)
{
    DEBUG(TRACE_TAPE, "tape %u: space reversing...\n", p->unit);

    // go back one to read postamble or filemark
    tape_drive_seek(p, -4, SEEK_CUR);

    uint32_t postamble;
    tape_drive_read_uint32(p, &postamble);

    // if it is filemark, go back one more to read actual postamble
    if (postamble == TAPE_FILE_MARK)
    {
        if (stop_at_filemark) 
        {
            tape_controller_set_mts_eof();
            return;
        }

        DEBUG(TRACE_TAPE, "tape %u: space reverse first postamble is filemark\n", p->unit);
        tape_drive_seek(p, -8, SEEK_CUR);
        tape_drive_read_uint32(p, &postamble);
    }

    uint32_t record_length = postamble;
    DEBUG(TRACE_TAPE, "tape %u: space reverse record-length: %u\n", p->unit, record_length);
    // +8 because the length is both at the end and at the beginning
    off_t last_pos = p->pos;
    // attention record_length is uint32_t, if left like that, it wont work
    tape_drive_seek(p, -((int64_t)record_length + 8), SEEK_CUR);
    // "the bit will be reset when the EOT marker is passed
    // while performing a Rewind or Space Reverse operation."
    if ((last_pos >= p->eotpos) && (p->pos < p->eotpos)) tape_controller_reset_mts_eot();

    DEBUG(TRACE_TAPE, "tape %u: space reverse completed.\n", p->unit);
}

void 
tape_drive_rewind(struct tape_drive_s* p)
{
    DEBUG(TRACE_TAPE, "tape %u: rewinding...\n", p->unit);

    tape_drive_rewind_ex(p);

    DEBUG(TRACE_TAPE, "tape %u: rewind completed.\n", p->unit);
}
