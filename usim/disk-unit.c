/* disk.c -- emulate a Trident disk
 *
 * Each disk block contains one Lisp Machine page worth of data,
 * i.e. 256. words or 1024. bytes.
 *
 * The number of total blocks is an uint32_t, thus maximum 4G. This is not a disk size
 * limitation because disk size = block no * block size
 * Thus, disk size < 4T is OK from this perspective.
 *
 * Max possible disk size is 4095 cylinders x 255 heads x 255 blocks per track 
 * This is due to protocol limitiation with LISPM
 * 266277375 blocks = 266277375 * 1K = 272668032000 B < 253.95 GB
 * Thus, maximum total disk size is ~254 GB
 *
 * The position is indicated with C/H/S (or C/H/B).
 * The last component is sometimes called block sometimes called sector. 
 * I call it sector here for the purpose of seeking.
 */

#include <assert.h>
#include <err.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#include "bus-interface.h"
#include "config.h"
#include "disk-controller.h"
#include "disk-unit.h"
#include "main-memory.h"
#include "misc.h"
#include "ucode.h"
#include "utrace.h"

struct disk_unit_s disk_units[NUMBER_OF_DISK_UNITS];

// LABL encoded to uint32_t, do not change
#define LABEL_LABL 011420440514ULL
// block size, do not change
#define BLOCKSIZE 1024

// full name, config name, ncylinders, nheads, nblocks_per_track
// terminate the list with {0}
struct disk_unit_type_s disk_unit_types[] = 
{
    {"Trident T-80",    "T-80",     815,    5,  17},
    {"Trident T-300",   "T-300",    815,    19, 17},
    {0}
};

bool
disk_unit_read(struct disk_unit_s *p, uint32_t *buffer)
{
	DEBUG(TRACE_DISK, "disk-unit %d: read at %u/%u/%u %u\n", 
            p->unit, p->cylinder, p->head, p->sector, p->lba);

	const off_t offset = ((off_t)(p->lba)) * BLOCKSIZE;

    if (offset >= p->mmsz)
	{
		WARNING(TRACE_DISK, "disk-unit %d: reading offset (%lld) past end of disk image (%lld)", 
                p->unit, offset, p->mmsz);
        return false;
	}

	memcpy(buffer, p->mm + offset, BLOCKSIZE);

    return true;
}

bool
disk_unit_write(struct disk_unit_s* p, uint32_t *buffer)
{
	DEBUG(TRACE_DISK, "disk-unit %d: write at %u/%u/%u %u\n", 
            p->unit, p->cylinder, p->head, p->sector, p->lba);

	const off_t offset = ((off_t)(p->lba)) * BLOCKSIZE;

	if (offset >= p->mmsz)
	{
        WARNING(TRACE_DISK, "disk-unit %d: writing offset (%lld) past end of disk image (%lld)", 
                p->unit, offset, p->mmsz);
        return false;
	}

	memcpy(p->mm + offset, buffer, BLOCKSIZE);

    return true;
}

bool
disk_unit_seek(struct disk_unit_s *p, uint32_t cylinder, uint32_t head, uint32_t sector)
{
    DEBUG(TRACE_DISK, "disk-unit %d: seek from %u/%u/%u to %d/%d/%d\n", 
            p->unit, 
            p->cylinder, p->head, p->sector, 
            cylinder, head, sector);

    // already in position ?
    if ((cylinder == p->cylinder) && (head == p->head) && (sector == p->sector))
    {
        return true;
    }

    if (cylinder >= p->ncylinders) 
    {
        p->seek_error = true;
        return false;
    }
    
    if (head >= p->nheads) 
    {
        p->seek_error = true;
        return false;
    }

    if (sector >= p->nblocks_per_track) 
    {
        p->seek_error = true;
        return false;
    }

    p->cylinder = cylinder;
    p->head = head;
    p->sector = sector;
    p->lba = (cylinder * p->nblocks_per_cylinder) + (head * p->nblocks_per_track) + sector;

    return true;
}

// this method is only logic
// actual seek, wait for seek, and check/set error happens in 
// disk_unit_seek
bool 
disk_unit_seek_next_lba(struct disk_unit_s *p)
{
    uint32_t cylinder = p->cylinder;
    uint32_t head = p->head;
    uint32_t sector = p->sector;
    sector++;
    if (sector == p->nblocks_per_track)
    {
        sector = 0;
        head++;
        if (head == p->nheads)
        {
            head = 0;
            cylinder++;
        }
    }

    return disk_unit_seek(p, cylinder, head, sector);
}

void
disk_unit_rotate(struct disk_unit_s *p)
{
    p->sector = (p->sector + 1) >= p->nblocks_per_track ? 0 : p->sector + 1;
}

void
disk_unit_raise_attention(struct disk_unit_s *p)
{
    p->attention = true;
    p->disk_controller_get_attention(p);
}

uint32_t
disk_unit_da(struct disk_unit_s *p)
{
    return (p->unit << 28) | (p->cylinder << 16) | (p->head << 8) | p->sector;
}

static struct disk_unit_type_s*
find_disk_unit_type(char *s)
{   
    struct disk_unit_type_s* p = disk_unit_types;

    while (p->name != NULL)
    {
        if ((strcasecmp(s, p->name) == 0) || (strcasecmp(s, p->short_name) == 0))
        {
            return p;
        }
        p++;
    }

    return NULL;
}

void
disk_unit_init(uint32_t unit, char *config_string)
{
    if (unit >= NUMBER_OF_DISK_UNITS)
    {
        errx(1, "disk-unit: %u exceeds the number of maximum disk units (%u)\n", 
                unit, NUMBER_OF_DISK_UNITS);
    }

    struct disk_unit_s *p = disk_units + unit;

    p->unit = unit;
    p->online = false;

    if (config_string == NULL) 
    {
        NOTICE(TRACE_USIM, "disk-unit %u: offline (not configured)\n", p->unit);
        return;
    }

    DEBUG(TRACE_DISK, "disk-unit %u: config_string=%s\n", p->unit, config_string);

    p->configured = true;
    
    char disk_unit_type[1024] = {0};
    char filename[1024] = {0};

    char* token1_str = strtok(config_string, ",");
    char* token2_str = (token1_str == NULL  ? NULL : strtok(NULL, ","));

    DEBUG(TRACE_DISK, "disk-unit %u: token1_str=%s\n", p->unit, token1_str == NULL ? "NULL" : token1_str);
    DEBUG(TRACE_DISK, "disk-unit %u: token2_str=%s\n", p->unit, token2_str == NULL ? "NULL" : token2_str);

    if (token1_str == NULL)
    {
        NOTICE(TRACE_USIM, "disk-unit %u: offline (empty configuration)\n", p->unit);
        return;
    }
    else
    {
        if (token2_str == NULL)
        {
            snprintf(disk_unit_type, 1024, "T-300");
            strntrim(filename, 1024, token1_str);
        }
        else
        {
            strntrim(disk_unit_type, 1024, token1_str);
            strntrim(filename, 1024, token2_str);
        }
    }

    p->type = find_disk_unit_type(disk_unit_type);

    if (p->type == NULL)
    {
        errx(1, "disk-unit %u: invalid disk unit type: '%s', config_string: '%s'", 
                p->unit, disk_unit_type, config_string);
    }

    p->filename = strdup(filename);

    p->ncylinders = p->type->ncylinders;
    p->nheads = p->type->nheads;
    p->nblocks_per_track = p->type->nblocks_per_track;
    p->nblocks_per_cylinder = p->nheads * p->nblocks_per_track;

    // disk unit has no disk pack loaded, not online
	if ((p->filename == NULL) || (strlen(p->filename) == 0)) 
    {
        NOTICE(TRACE_USIM, "disk-unit %u: [%s]: offline (no disk pack)\n", 
                p->unit, p->type->name);
        return;
    }

	p->fd = open(p->filename, O_RDWR | O_BINARY | O_DSYNC);

    // cannot open disk pack file, not online
	if (p->fd < 0) 
    {
        errx(1, "disk-unit %u: [%s]: offline (cannot open disk pack: %s)\n",
                p->unit, p->type->name, p->filename);
        p->fd = 0;
        return;
	}

	struct stat st = {0};

    // cannot fstat disk pack file, not online
	if (fstat(p->fd, &st) < 0)
	{
        errx(1, "disk-unit %u: [%s]: offline (cannot fstat disk pack: %s)\n",
                p->unit, p->type->name, p->filename);
        return;
	}

    const off_t expected_disk_pack_size = 
        (off_t)p->ncylinders * (off_t)p->nblocks_per_cylinder * (off_t)BLOCKSIZE;

    // disk pack file exists but it probably belongs to something else
    // fatal error, quit usim
    if (st.st_size != expected_disk_pack_size)
    {
        errx(1, "disk-unit %u: [%s]: disk pack (%s) size (%llu) is not the expected one (%llu)",
                p->unit, p->type->name, p->filename, st.st_size, expected_disk_pack_size);
        return;
    }

    if (st.st_size > INT32_MAX)
    {
        if (sizeof(off_t) < 8)
        {
            warnx("disk-unit %u: [%s]: disk pack having size (%llu) is not supported because sizeof(off_t)=%zu < 8.\n", 
                    p->unit, p->type->name, st.st_size, sizeof(off_t));
            return;
        }
    }

	p->mmsz = st.st_size;

	p->mm = mmap(NULL, p->mmsz, PROT_READ | PROT_WRITE, MAP_SHARED, p->fd, 0);

    // cannot memory map the file, not online
	if (p->mm == MAP_FAILED)
	{
        errx(1, "disk-unit %u: [%s]: offline (cannot memory map disk pack: %s)\n",
                p->unit, p->type->name, p->filename);
        return;
	}

    // reset in case the structure is not zeroed
    p->cylinder = 0;
    p->head     = 0;
    p->sector   = 0;
    p->lba      = 0;

    p->online = true;

    NOTICE(TRACE_USIM, "disk-unit %u: [%s]: online (%s)\n", 
        p->unit, p->type->name, p->filename);
}

void
disk_unit_quit(struct disk_unit_s *p)
{
    if (p->filename != NULL) free(p->filename);
    if (p->mm != MAP_FAILED) munmap(p->mm, p->mmsz);
    if (p->fd > 0) close(p->fd);
}
