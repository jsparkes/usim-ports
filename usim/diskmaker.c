/* diskmaker --- manage disk packs (Trident T-300, T-80 or variations)
 */

#include <assert.h>
#include <err.h>
#include <fcntl.h>
#include <libgen.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <unistd.h>

#include <sys/stat.h>

// struct part_s and disk_label_s below are directly used with read/write
// do not modify them if you dont know what you are doing

// 1 disk block is 1 lispm block = 256 words = 1024 bytes
#define BLOCKSIZE 1024

// 7 words
struct __attribute__((packed)) part_s
{
	uint32_t name;
	uint32_t start;
	uint32_t size;
	uint32_t label[4]; // not null terminated
};

struct __attribute__((packed)) disk_label_s
{
	uint32_t magic;	// encodes char[4]
	uint32_t version;
	uint32_t n_cylinders;
	uint32_t n_heads;
	uint32_t n_blocks_per_track;
	uint32_t n_blocks_per_cylinder;
	uint32_t current_microload;	// encodes char[4], not null terminated
	uint32_t current_band;	// encodes char[4], not null terminated

	char drive_name[32]; // not null terminated
	char pack_name[32]; // not null terminated
	char comment[32]; // not null terminated

	// until here 32 words are used
	// next 96 words are unused

	uint32_t __not_used__[96];

	// here starts the partition table from words 0200 (128)
	// there are maximum 18 partitions, because 2 + 18*7 = 128

	uint32_t n_partitions;
	uint32_t words_per_part;
	struct part_s parts[18];
};

static uint32_t
str4(char *s)
{
	return (s[3] << 24) | (s[2] << 16) | (s[1] << 8) | s[0];
}

static char*
unstr4(uint32_t s)
{
	static char b[5];
	b[3] = s >> 24;
	b[2] = s >> 16;
	b[1] = s >> 8;
	b[0] = s;
	b[4] = 0;
	return b;
}

static char*
unstr16(uint32_t *label)
{
	static char s[17];
	memcpy(s, unstr4(label[0]), 4);
	memcpy(s+4, unstr4(label[1]), 4);
	memcpy(s+8, unstr4(label[2]), 4);
	memcpy(s+12, unstr4(label[3]), 4);
	s[16] = 0;
	return s;
}

static void
assert_file_not_exists(char *filename)
{
	assert (filename != NULL);

	if (access(filename, F_OK) != -1)
	{
		errx(1, "file %s already exists", filename);
	}
}

static int
open_file_for_write(char *filename)
{
	assert (filename != NULL);

	int fd = open(filename, O_WRONLY | O_CREAT, S_IWUSR | S_IRUSR); 
	
	if (fd < 0)
	{
		errx(1, "cannot open file for write: %s", filename);
	}

	return fd;
}

static int
open_file_for_read(char *filename)
{
	assert (filename != NULL);

	int fd = open(filename, O_RDONLY);

	if (fd < 0)
	{
		errx(1, "cannot open file for read: %s", filename);
	}

	return fd;
}

static void
read_template(
		char *template_filename, 
		struct disk_label_s* disk_label)
{
	assert (template_filename != NULL);
	assert (disk_label != NULL);

	memset(disk_label, 0, sizeof(struct disk_label_s));

	disk_label->magic = str4("LABL");
	disk_label->version = 1;
	disk_label->words_per_part = 7;

	bool reading_disk_parameters = true;
	char line[256];

	// from io/dledit.lisp: 
	// "First partition starts at block 17. (first track reserved)
	uint64_t next_start = 0;

	FILE *fp = fopen(template_filename, "r");
	
	if (fp == NULL)
	{
		errx(1, "cannot open template file: %s", template_filename);
	}

	while (fgets(line, sizeof(line), fp)) {
		if (line[0]) {
			int l = strlen(line);
			line[l - 1] = 0;
		}
		if (line[0] == '#')
		{
			continue;
		}
		else if (strcmp(line, "partitions:") == 0) 
		{
			if ((disk_label->magic == 0) ||
					(disk_label->version == 0) ||
					(disk_label->n_cylinders == 0) ||
					(disk_label->n_heads == 0) ||
					(disk_label->n_blocks_per_track == 0) ||
					(disk_label->current_microload == 0) ||
					(disk_label->current_band == 0))
			{
				errx(1, "missing disk parameters, please check the template file");
			}
			if (disk_label->n_blocks_per_cylinder == 0)
			{
				disk_label->n_blocks_per_cylinder = disk_label->n_heads * disk_label->n_blocks_per_track;
			}

            // first track is reserved
            next_start = disk_label->n_blocks_per_track;
			reading_disk_parameters = false;
		}
		else
		{
			if (reading_disk_parameters)
			{
				char name[256] = {0};
				char value[256] = {0};
				sscanf(line, "%s \t%[^\n]", name, value);
				if (strcmp(name, "cylinders") == 0)
				{
					uint32_t cylinders = strtol(value, NULL, 0);
					if (cylinders >= 4096)
					{
						errx(1, "cylinders has to be <= 4095");
					}
					disk_label->n_cylinders = cylinders;
				}
				else if (strcmp(name, "heads") == 0)
				{
					uint32_t heads = strtol(value, NULL, 0);
					if (heads >= 256)
					{
						errx(1, "heads has to be <= 255");
					}
					disk_label->n_heads = heads;
				}
				else if (strcmp(name, "blockspertrack") == 0)
				{
					uint32_t blocks_per_track = strtol(value, NULL, 0);
					if (blocks_per_track >= 256)
					{
						errx(1, "blockspertrack has to be <= 255");
					}
					disk_label->n_blocks_per_track = blocks_per_track;
				}
				else if (strcmp(name, "blockspercylinder") == 0)
				{
					uint32_t blocks_per_cylinder = strtol(value, NULL, 0);
					disk_label->n_blocks_per_cylinder = blocks_per_cylinder;
				}
				else if (strcmp(name, "mcr") == 0) 
				{
					assert (strlen(value) == 4);
					disk_label->current_microload = str4(value);
				}
				else if (strcmp(name, "lod") == 0) 
				{
					assert (strlen(value) == 4);
					disk_label->current_band = str4(value);
				}
				else if ((strcmp(name, "brand") == 0) || (strcmp(name, "drive") == 0))
				{
					strncpy(disk_label->drive_name, value, 32);
				}
				else if ((strcmp(name, "text") == 0) || (strcmp(name, "pack") == 0))
				{
					strncpy(disk_label->pack_name, value, 32);
				}
				else if (strcmp(name, "comment") == 0) 
				{
					strncpy(disk_label->comment, value, 32);
				}
				else 
				{
					errx(1, "invalid line in template: %s", line);
				}
			}
			else
			{
				const uint64_t capacity = (
						(uint64_t)(disk_label->n_cylinders) * 
						(uint64_t)(disk_label->n_heads) * 
						(uint64_t)(disk_label->n_blocks_per_track));

				if (disk_label->n_partitions >= 18)
				{
					errx(1, "maximum 18 partitions can be created");
				}

				char name[4] = {0};
				char sstart[256] = {0};
				char ssize[256] = {0};
				uint32_t start;
				uint32_t size;
				char label[32] = {0};

				sscanf(line, "%4c \t%s \t%s \t%s", name, sstart, ssize, label);

				// if start = -, 
				// it means continue from where the last partition finished
				if (sstart[0] == '-') start = next_start;
				else start = strtol(sstart, NULL, 0);

				if (start < 0) errx(1, "partition start has to be >= 0, error in line: %s", line);
                if ((disk_label->n_partitions == 0) && (start != disk_label->n_blocks_per_track))
                {
                    warnx("the start of the first partition (%d) is not equal to the blocks per track (%d)",
                            start,
                            disk_label->n_blocks_per_track);
                }

				// if size = -, 
				// it means size is till the end of the disk capacity
				if (ssize[0] == '-') size = capacity - start;
				if (ssize[0] == 'c') 
				{
					const uint32_t size_in_cyls = strtol(ssize+1, NULL, 0);
					size = size_in_cyls * disk_label->n_blocks_per_cylinder;
					// if c is used, adjust it to the start of cylinder boundary
					const uint32_t nblocks_in_last_cylinder = start % disk_label->n_blocks_per_cylinder;
					start = start + (nblocks_in_last_cylinder > 0 ? (disk_label->n_blocks_per_cylinder - nblocks_in_last_cylinder) : 0);
				}
				else size = strtol(ssize, NULL, 0);

				if (size < 0) errx(1, "partition size has to be >= 0, error in line: %s", line);

				next_start = start + size;

				if (next_start > capacity)
				{
					errx(1, "invalid line: %s, disk capacity exceeded: %llu > %llu", 
							line, next_start, capacity);
				}

				struct part_s *part = &(disk_label->parts[disk_label->n_partitions++]);

				part->name = str4(name);
				part->start = start;
				part->size = size;
				part->label[0] = str4(label);
				part->label[1] = str4(label+4);
				part->label[2] = str4(label+8);
				part->label[3] = str4(label+12);

			}
		}
	}
	fclose(fp);
}

static void
read_disk_label(char *disk_pack_filename, struct disk_label_s* disk_label)
{
	assert (disk_pack_filename != NULL);
	assert (disk_label != NULL);

	// read first block of the disk pack
	// first block contains the disk label
	int fd = open_file_for_read(disk_pack_filename);
	size_t ret = read(fd, disk_label, sizeof(struct disk_label_s));
	assert (ret == sizeof(struct disk_label_s));
	close(fd);

	if (disk_label->magic != str4("LABL"))
	{
		errx(1, "invalid disk pack file: %s (invalid magic)\n", disk_pack_filename);
	}

	if (disk_label->version != 1)
	{
		errx(1, "invalid disk pack file: %s (invalid version %u)\n", 
				disk_pack_filename,
				disk_label->version);
	}

	if (disk_label->n_partitions > 18)
	{
		errx(1, "invalid disk pack file: %s (invalid n_partitions %u)\n", 
				disk_pack_filename,
				disk_label->n_partitions);
	}

	if (disk_label->words_per_part != 7)
	{
		errx(1, "invalid disk pack file: %s (invalid words per part %u)\n", 
				disk_pack_filename,
				disk_label->words_per_part);
	}
}

static void
print_template(struct disk_label_s *disk_label)
{
	assert (disk_label != NULL);

	printf("cylinders %u\n", disk_label->n_cylinders);
	printf("heads %u\n", disk_label->n_heads);
	printf("blockspertrack %u\n", disk_label->n_blocks_per_track);

	printf("drive %s\n", disk_label->drive_name);
	printf("pack %s\n", disk_label->pack_name);
	printf("comment %s\n", disk_label->comment);
	printf("mcr %s\n", unstr4(disk_label->current_microload));
	printf("lod %s\n", unstr4(disk_label->current_band));

	printf("partitions:\n");

	for (uint32_t i = 0; i < disk_label->n_partitions; i++)
	{
		struct part_s *part = &(disk_label->parts[i]);

		char name[5] = {0};
		memcpy(name, unstr4(part->name), 4);

		printf("%s 0%o 0%o %s\n", 
				name,
				part->start, part->size, 
				unstr16(part->label));
	}
}


static void
print_disk_label(struct disk_label_s *disk_label)
{
	assert (disk_label != NULL);

	printf("%s %u\n", unstr4(disk_label->magic), disk_label->version);

	printf("%u/%u/%u %u(=%ux%u+%u)\n", 
			disk_label->n_cylinders, 
			disk_label->n_heads, 
			disk_label->n_blocks_per_track,
			disk_label->n_blocks_per_cylinder,
			disk_label->n_heads,
			disk_label->n_blocks_per_track,
			disk_label->n_blocks_per_cylinder - disk_label->n_heads * disk_label->n_blocks_per_track);

	// these are not on the same printf because unstr4 can be used once properly
	// due to static char[] in unstr4
	printf("%s", unstr4(disk_label->current_microload));
	printf(" %s", unstr4(disk_label->current_band));
	printf("\n");

	printf("'%s'\n", disk_label->drive_name);
	printf("'%s'\n", disk_label->pack_name);
	printf("'%s'\n", disk_label->comment);

	uint32_t max_start = 0;
	uint32_t max_size = 0;
	for (uint32_t i = 0; i < disk_label->n_partitions; i++)
	{
		struct part_s *part = &(disk_label->parts[i]);
		if (part->start > max_start) max_start = part->start;
		if (part->size > max_size) max_size = part->size;
	}

	uint32_t start_width	= 6;
	if (max_start > 077777777) start_width = 9;
	else if (max_start > 07777777) start_width = 8;
	else if (max_start > 0777777) start_width = 7;
	uint32_t size_width		= 5;
	if (max_size > 0777777) size_width = 8;
	else if (max_size > 0777777) size_width = 7;
	else if (max_size > 077777) size_width = 6;

	for (uint32_t i = 0; i < disk_label->n_partitions; i++)
	{
		struct part_s *part = &(disk_label->parts[i]);

		const uint32_t c = part->start / disk_label->n_blocks_per_cylinder;
		const uint32_t h = (part->start - c * disk_label->n_blocks_per_cylinder) / disk_label->n_blocks_per_track;
		const uint32_t b = part->start - c * disk_label->n_blocks_per_cylinder - h * disk_label->n_blocks_per_track;

		char name[5] = {0};
		memcpy(name, unstr4(part->name), 4);

		printf("%s: start: %*o [%*u/%*u/%*u] size: %*o '%s'\n", 
				name,
				start_width, part->start,
				4, c, 3, h, 3, b,
				size_width, part->size,
				unstr16(part->label));
	}
}

static void
validate_band_name(char *band_name)
{
	assert (band_name != NULL);

	if (strlen(band_name) != 4)
	{
		errx(1, "band name should have 4 characters but it is '%s'", band_name);
	}
}

static void
write_disk_label(struct disk_label_s *disk_label, char *disk_pack_filename)
{
	assert (disk_label != NULL);
	assert (disk_pack_filename != NULL);

	int fd = open_file_for_write(disk_pack_filename);
	write(fd, disk_label, sizeof(struct disk_label_s));
	close(fd);
}

static void
create_empty_disk(struct disk_label_s *disk_label, char *disk_pack_filename)
{
	assert (disk_label != NULL);
	assert (disk_pack_filename != NULL);

	uint8_t ZERO_BLOCK[BLOCKSIZE] = {0};

	const off_t number_of_blocks = (off_t)disk_label->n_cylinders * 
		(off_t)disk_label->n_blocks_per_cylinder;

	printf("creating empty disk: %s of %llu blocks\n", 
			disk_pack_filename, number_of_blocks);

	int fd = open_file_for_write(disk_pack_filename);

	for (off_t i = 0; i < number_of_blocks; i++)
	{
		write(fd, ZERO_BLOCK, sizeof(ZERO_BLOCK));
	}

	close(fd);
}

static void
set_drive_name(char *disk_pack_filename, char *drive_name)
{
    assert (disk_pack_filename != NULL);
	assert (drive_name != NULL);

    if (strlen(drive_name) > 32)
    {
        err(1, "DRIVE_NAME should be less than 32 characters");
    }

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

    strncpy(disk_label.drive_name, drive_name, 32);

    write_disk_label(&disk_label, disk_pack_filename);
}

static void
set_pack_name(char *disk_pack_filename, char *pack_name)
{
    assert (disk_pack_filename != NULL);
	assert (pack_name != NULL);

    if (strlen(pack_name) > 32)
    {
        err(1, "PACK_NAME should be less than 32 characters");
    }

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

    strncpy(disk_label.pack_name, pack_name, 32);

    write_disk_label(&disk_label, disk_pack_filename);
}

static void
set_comment(char *disk_pack_filename, char *comment)
{
    assert (disk_pack_filename != NULL);
	assert (comment != NULL);

    if (strlen(comment) > 32)
    {
        err(1, "COMMENT should be less than 32 characters");
    }

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

    strncpy(disk_label.comment, comment, 32);

    write_disk_label(&disk_label, disk_pack_filename);
}

static void
set_partition_comment(char *disk_pack_filename, char *band_name, char *comment)
{
    assert (disk_pack_filename != NULL);
    assert (band_name != NULL);
	assert (comment != NULL);

    if (strlen(comment) > 16)
    {
        err(1, "COMMENT should be less than 16 characters");
    }

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

	uint32_t band = str4(band_name);
	for (size_t i = 0; i < disk_label.n_partitions; i++)
	{
		if (disk_label.parts[i].name == band)
		{
            // reset the partition comment first
            disk_label.parts[i].label[0] = 0;
            disk_label.parts[i].label[1] = 0;
            disk_label.parts[i].label[2] = 0;
            disk_label.parts[i].label[3] = 0;
            const size_t len = strlen(comment);
            size_t current = 0;
            // update each label cell separately
            for (int j = 0; j < 4 && current < len; j++)
            {
                uint32_t v = 0;
                for (int k = 0; k < 4 && current < len; k++)
                {
                    v |= (comment[current++] << (k*8));
                }
                disk_label.parts[i].label[j] = v;
            }
            write_disk_label(&disk_label, disk_pack_filename);
            return;
        }
    }

	errx(1, "disk does not contain a partition named: %s", band_name);
}

static void
set_current_band(char *disk_pack_filename, char *band_name, bool set_mcr_band)
{
	assert (disk_pack_filename != NULL);
	assert (band_name != NULL);

	validate_band_name(band_name);

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

	uint32_t band = str4(band_name);
	for (size_t i = 0; i < disk_label.n_partitions; i++)
	{
		if (disk_label.parts[i].name == band)
		{
			if (set_mcr_band)
			{
				disk_label.current_microload = band;
			}
			else
			{
				disk_label.current_band = band;
			}

			write_disk_label(&disk_label, disk_pack_filename);

			return;
		}
	}

	errx(1, "disk does not contain a partition named: %s", band_name);
}

static void
restore_band(char *src_filename, char *disk_pack_filename, char *band_name)
{
	assert (src_filename != NULL);
	assert (disk_pack_filename != NULL);
	assert (band_name != NULL);

	validate_band_name(band_name);

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

	struct stat st;
	stat(src_filename, &st);

	uint32_t band = str4(band_name);

	for (size_t i = 0; i < disk_label.n_partitions; i++)
	{
		if (disk_label.parts[i].name == band)
		{
			off_t start = (off_t)disk_label.parts[i].start * BLOCKSIZE;
			off_t size = (off_t)disk_label.parts[i].size * BLOCKSIZE;

			if (st.st_size > size)
			{
				errx(1, "SRC_FILE is larger than the partition %s", band_name);
			}

			int fdst = open_file_for_write(disk_pack_filename);
			lseek(fdst, start, SEEK_SET);

			int fsrc = open_file_for_read(src_filename);

			uint8_t block[BLOCKSIZE] = {0};
			off_t total_read = 0;

			while (true)
			{
				size_t ret = read(fsrc, block, sizeof(block));
				if (ret > 0)
				{
					write(fdst, block, ret);
					total_read += ret;
				}
				else if (ret == 0)
				{
					write(fdst, block, ret);
					break;
				}
				else
				{
					close(fsrc);
					close(fdst);
					errx(1, "error file reading SRC_FILE: %s", src_filename);
				}
			}

			close(fsrc);
			close(fdst);

			char *src_name = basename(src_filename);
			char label[16] = {0};
			strncpy(label, src_name, 16);

			disk_label.parts[i].label[0] = str4(label+0);
			disk_label.parts[i].label[1] = str4(label+4);
			disk_label.parts[i].label[2] = str4(label+8);
			disk_label.parts[i].label[3] = str4(label+12);

			write_disk_label(&disk_label, disk_pack_filename);

			printf("%llu bytes of %s restored to band %s of disk %s\n", 
					total_read, src_filename, band_name, disk_pack_filename);

			return;
		}
	}

	errx(1, "disk does not contain a partition named: %s", band_name);
}

static void
dump_band(char *disk_pack_filename, char *band_name, char *dst_filename)
{
	assert (disk_pack_filename != NULL);
	assert (band_name != NULL);
	assert (dst_filename != NULL);

	validate_band_name(band_name);

	struct disk_label_s disk_label;
	read_disk_label(disk_pack_filename, &disk_label);

	uint32_t band = str4(band_name);

	for (size_t i = 0; i < disk_label.n_partitions; i++)
	{
		if (disk_label.parts[i].name == band)
		{
			off_t start = (off_t)disk_label.parts[i].start * BLOCKSIZE;
			off_t size = (off_t)disk_label.parts[i].size * BLOCKSIZE;

			int fdst = open_file_for_write(dst_filename);
			int fsrc = open_file_for_read(disk_pack_filename);
			lseek(fsrc, start, SEEK_SET);

			uint8_t block[BLOCKSIZE] = {0};
			off_t total_read = 0;

			while (total_read < size)
			{
				size_t toread = size - total_read;
				if (toread > sizeof(block)) toread = sizeof(block);
				size_t ret = read(fsrc, block, toread);
				if (ret > 0)
				{
					write(fdst, block, ret);
					total_read += ret;
				}
				else if (ret == 0)
				{
					write(fdst, block, ret);
					break;
				}
				else
				{
					close(fsrc);
					close(fdst);
					errx(1, "error reading DISK_PACK_FILE %s", disk_pack_filename);
				}
			}

			close(fsrc);
			close(fdst);

			printf("%llu bytes saved to %s\n", total_read, dst_filename);

			return;
		}
	}

	errx(1, "disk does not contain a partition named: %s", band_name);
}

static void
usage(void)
{
	printf("CADR diskmaker\n");
	printf("usage: diskmaker COMMAND PARAMETERS\n");
	printf("\n");
	printf("  diskmaker [h]elp\n");
	printf("  diskmaker [c]reate-disk-pack      DISK_PACK_FILE TEMPLATE_FILE\n");
	printf("  diskmaker [s]how-disk-pack        DISK_PACK_FILE\n");
	printf("  diskmaker set-drive-name          DISK_PACK_FILE DRIVE_NAME\n");
	printf("  diskmaker set-pack-name           DISK_PACK_FILE PACK_NAME\n");
	printf("  diskmaker set-comment             DISK_PACK_FILE COMMENT\n");
	printf("  diskmaker set-partition-comment   DISK_PACK_FILE BAND_NAME COMMENT\n");
	printf("  diskmaker show-[t]emplate         DISK_PACK_FILE\n");
	printf("  diskmaker set-[m]icroload         DISK_PACK_FILE BAND_NAME\n");
	printf("  diskmaker set-[b]and              DISK_PACK_FILE BAND_NAME\n");
	printf("  diskmaker [r]estore-band          SRC_FILE DISK_PACK_FILE BAND_NAME\n");
	printf("  diskmaker [d]ump-band             DISK_PACK_FILE BAND_NAME DST_FILE\n");
	printf("\n");
}

static void
show_usage_and_fail(void)
{
	usage();
	exit(EXIT_FAILURE);
}

static bool
is_command(char c, const char *cmd, char *s)
{
	if (strcmp(cmd, s) == 0) return true;

	return c != 0 && s[1] == 0 && s[0] == c;
}

int
main(int argc, char *argv[])
{
	if (argc <= 1) 
	{
		show_usage_and_fail();
	}
	else if (is_command('h', "help", argv[1]))
	{
		usage();
	}
	else if (is_command('c', "create-disk-pack", argv[1]))
	{
		if (argc < 3) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
		char *template_filename = argv[3];
		struct disk_label_s disk_label;

		assert_file_not_exists(disk_pack_filename);
	
		read_template(template_filename, &disk_label);
		print_disk_label(&disk_label);

		create_empty_disk(&disk_label, disk_pack_filename);
		write_disk_label(&disk_label, disk_pack_filename);

		read_disk_label(disk_pack_filename, &disk_label);
		print_disk_label(&disk_label);
	}
	else if (is_command('s', "show-disk-pack", argv[1]))
	{
		if (argc < 2) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
		struct disk_label_s disk_label;
		read_disk_label(disk_pack_filename, &disk_label);
		print_disk_label(&disk_label);
	}
    else if (strcmp(argv[1], "set-drive-name") == 0)
	{
		if (argc < 3) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
        char *drive_name = argv[3];
        set_drive_name(disk_pack_filename, drive_name);
		struct disk_label_s disk_label;
		read_disk_label(disk_pack_filename, &disk_label);
		print_disk_label(&disk_label);
	}
    else if (strcmp(argv[1], "set-pack-name") == 0)
	{
		if (argc < 3) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
        char *pack_name = argv[3];
        set_pack_name(disk_pack_filename, pack_name);
		struct disk_label_s disk_label;
		read_disk_label(disk_pack_filename, &disk_label);
		print_disk_label(&disk_label);
	}
    else if (strcmp(argv[1], "set-comment") == 0)
	{
		if (argc < 3) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
        char *comment = argv[3];
        set_comment(disk_pack_filename, comment);
		struct disk_label_s disk_label;
		read_disk_label(disk_pack_filename, &disk_label);
		print_disk_label(&disk_label);
	}
    else if (strcmp(argv[1], "set-partition-comment") == 0)
	{
		if (argc < 4) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
        char *band_name = argv[3];
        char *comment = argv[4];
        set_partition_comment(disk_pack_filename, band_name, comment);
		struct disk_label_s disk_label;
		read_disk_label(disk_pack_filename, &disk_label);
		print_disk_label(&disk_label);
	}
	else if (is_command('t', "show-template", argv[1]))
	{
		if (argc < 2) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
		struct disk_label_s disk_label;
		read_disk_label(disk_pack_filename, &disk_label);
		print_template(&disk_label);
	}
	else if (is_command('m', "set-microload", argv[1]))
	{
		if (argc < 4) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
		char *band_name = argv[3];
		set_current_band(disk_pack_filename, band_name, true);
	}
	else if (is_command('b', "set-band", argv[1]))
	{
		if (argc < 4) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
		char *band_name = argv[3];
		set_current_band(disk_pack_filename, band_name, false);
	}
	else if (is_command('r', "restore-band", argv[1]))
	{
		if (argc < 5) show_usage_and_fail();
		char *src_filename = argv[2];
		char *disk_pack_filename = argv[3];
		char *band_name = argv[4];
		restore_band(src_filename, disk_pack_filename, band_name);
	}
	else if (is_command('d', "dump-band", argv[1]))
	{
		if (argc < 5) show_usage_and_fail();
		char *disk_pack_filename = argv[2];
		char *band_name = argv[3];
		char *dst_filename = argv[4];
		assert_file_not_exists(dst_filename);
		dump_band(disk_pack_filename, band_name, dst_filename);
	}
	else
	{
		show_usage_and_fail();
	}

	exit(EXIT_SUCCESS);
}
