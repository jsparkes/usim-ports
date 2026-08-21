#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <unistd.h>

#define TAPE_FILE_MARK 0xFFFFFFFF

struct tape_drive_s
{
    // same as index of tape_drives
    uint32_t unit;

    // if it exists in usim.ini
    bool configured;

    // these are initialized from the config
    char* filename;
    off_t length_ft;
    bool read_only;

    // fd of filename
    int fd;

    // mm
	uint8_t *mm;
	off_t mmsz;

    // this becomes true if initialized values are good
    bool online;

    // density, selected by the software
    // this also sets the eotpos
    uint32_t bpi;

    // this is the position of eot in bytes
    // changes if bpi is changed
    off_t eotpos;

    // position of tape
    off_t pos;

    // beginning of tape is set when pos = 0
    bool bot;
    // slowing down is set when rewinding is cleared
    // write_lock is set from read_only
    // rewind status is set when rewinding, cleared when bot is reached
    bool rws;
    // ready is set when stopped, cleared when executing sth
    bool ready;
};

// TC-131 supports maximum 8 units/tape drives
#define NUMBER_OF_TAPE_DRIVES 8
extern struct tape_drive_s tape_drives[NUMBER_OF_TAPE_DRIVES];

void tape_drive_init(uint32_t unit, char *config_string);
void tape_drive_quit(struct tape_drive_s* p);
void tape_drive_set_bpi(struct tape_drive_s* p, uint32_t bpi);
void tape_drive_seek(struct tape_drive_s* p, off_t offset, int whence);

// block and record is the same thing
// file mark and tape mark is the same thing
// some texts/vendors use block others use record

void tape_drive_rewind_and_offline(struct tape_drive_s* p);
bool tape_drive_read_uint8(struct tape_drive_s* p, uint8_t* pv);
bool tape_drive_read_uint32(struct tape_drive_s* p, uint32_t* pv);
bool tape_drive_write_uint8(struct tape_drive_s* p, uint8_t v);
bool tape_drive_write_uint32(struct tape_drive_s* p, uint32_t v);
bool tape_drive_write_record_length(struct tape_drive_s* p, size_t record_length);
bool tape_drive_write_file_mark(struct tape_drive_s* p);
void tape_drive_space_forward(struct tape_drive_s* p);
void tape_drive_space_reverse(struct tape_drive_s* p, bool stop_at_filemark);
void tape_drive_rewind(struct tape_drive_s* p);