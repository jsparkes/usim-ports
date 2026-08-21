#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <stdint.h>

#include <sys/types.h>

struct disk_unit_type_s
{
    const char* name;
    const char* short_name;
    uint32_t ncylinders;
    uint32_t nheads;
    uint32_t nblocks_per_track;
};

struct disk_unit_s
{
    // these are read from usim.ini
    uint32_t unit; 
    // this is set from disk_unit_type in usim.ini
    struct disk_unit_type_s *type;
    // these are set from disk_unit_type
    uint32_t ncylinders;
    uint32_t nheads;
    uint32_t nblocks_per_track;
    uint32_t nblocks_per_cylinder;
    // disk pack filename
    char* filename;
    // 
    void (*disk_controller_get_attention)(struct disk_unit_s *);

    // set once during power on
    bool configured;
    bool online;
    bool read_only;

    // dynamic conditions
    bool seek_error;
    bool has_fault;
    bool attention;

    // this is used for memory address register by the controller
    uint32_t last_memory_address;

	int fd;
	uint8_t *mm;
	off_t mmsz;

    // disk position C/H/S
    uint32_t cylinder;
    uint32_t head;
    uint32_t sector;

    // disk position LBA
    // when disk_unit_seek is done, lba is adjusted so it follows C/H/S
    uint32_t lba;
};

#define NUMBER_OF_DISK_UNITS 8
extern struct disk_unit_s disk_units[NUMBER_OF_DISK_UNITS];

void disk_unit_init(uint32_t unit, char *config_string);
void disk_unit_quit(struct disk_unit_s *p);

bool disk_unit_read(struct disk_unit_s *p, uint32_t *buffer);
bool disk_unit_write(struct disk_unit_s* p, uint32_t *buffer);
bool disk_unit_seek(struct disk_unit_s *p, uint32_t cylinder, uint32_t head, uint32_t sector);
bool disk_unit_seek_next_lba(struct disk_unit_s *p);
void disk_unit_raise_attention(struct disk_unit_s *p);
uint32_t disk_unit_da(struct disk_unit_s *p);
void disk_unit_rotate(struct disk_unit_s *p);
