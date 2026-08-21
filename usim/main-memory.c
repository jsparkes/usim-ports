/* main-memory.c --- CADR main memory routines
 *
 * main-memory corresponds to main memory in the CADR diagram.
 * main-memory (statically) allocates maximum number of pages possible.
 * The pages are not exported, not accesible fom other files.
 *
 * main-memory also provides save_page_to_file and load_page_from_file methods
 * for saving/restoring the state (aka dump file).
 */

#include <assert.h>
#include <err.h>
#include <string.h>
#include <unistd.h>

#include <err.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>

#include "config.h"
#include "main-memory.h"
#include "ucode.h"
#include "utrace.h"

#define NUMBER_OF_WORDS_PER_PAGE 256
#define NUMBER_OF_BYTES_PER_PAGE (NUMBER_OF_WORDS_PER_PAGE * 4)

// default 2048MW=8192 pages, this can be modified in usim.ini (memory.size)
// the upper limit (NUMBER_OF_MAX_MAIN_MEMORY_PAGES) is enforced in ucfg.c
uint32_t main_memory_npages;

// actual memory allocation is static since the maximum is not much (16K*256*4=16MB)
// so the main_memory_npages is only a usage limit from usim's perspective
static uint32_t pages[NUMBER_OF_MAX_MAIN_MEMORY_PAGES][NUMBER_OF_WORDS_PER_PAGE];

void
main_memory_load_page_from_file(uint32_t pn, int fd)
{
	assert (pn < main_memory_npages);
	if (read(fd, pages[pn], NUMBER_OF_BYTES_PER_PAGE) < 0)
	{
		errx(1, "main_memory: read from file failed pn:%o\n", pn);
	}
}

void
main_memory_save_page_to_file(uint32_t pn, int fd)
{
	assert (pn < main_memory_npages);
	if (write(fd, pages[pn], NUMBER_OF_BYTES_PER_PAGE) < 0)
	{
		errx(1, "main_memory: write to file failed pn:%o\n", pn);
	}
}

// read from physical address paddr into *pv
// paddr is 22-bit
bool
main_memory_read(uint32_t paddr, uint32_t *pv)
{
	// paddr[22:8] is page number
	const uint32_t pn = (paddr >> 8) & 0x3FFF;
	// populated memory area ?
	if (pn < main_memory_npages)
	{
		// paddr[7:0] is offset
		*pv = pages[pn][paddr & 0xFF];
        return true;
	}
	else
	{
		// this may happen during memory probing by the prom
        if (pn == main_memory_npages) 
        {
            INFO(TRACE_USIM, "main-memory: read from invalid physical page: %lu (npages: %lu), memory probe?\n", 
                pn, main_memory_npages);
        }
        else
        {
            WARNING(TRACE_USIM, "main-memory: read from invalid physical page: %lu (npages: %lu)\n", 
                pn, main_memory_npages);
        }
		// the actual value returned below might be important 
        //  depending on what caller is expecting
		*pv = 0xffffffff;
        return false;
	}
}

// write word v to physical address paddr
// paddr is 22-bit
bool
main_memory_write(uint32_t paddr, uint32_t v)
{
	// paddr[22:8] is page number
	const uint32_t pn = (paddr >> 8) & 0x3FFF;
	// populated memory area ? 
	if (pn < main_memory_npages)
	{
		// paddr[7:0] is offset
		pages[pn][paddr & 0xFF] = v;
        return true;
	}
	else
	{
		// this may happen during memory probing by the prom
        if (pn == main_memory_npages) 
        {
            INFO(TRACE_USIM, "main-memory: write to invalid physical page: %lu (npages: %lu), memory probe?\n", 
                pn, main_memory_npages);
        }
        else
        {
            WARNING(TRACE_USIM, "main-memory: write to invalid physical page: %lu (npages: %lu)\n", 
                pn, main_memory_npages);
        }

        return false;
	}
}

bool main_memory_read_page(uint32_t paddr, uint32_t *buffer)
{
	// paddr[22:8] is page number
	const uint32_t pn = (paddr >> 8) & 0x3FFF;
	// populated memory area ?
	if (pn < main_memory_npages)
	{
        memcpy(buffer, pages[pn], NUMBER_OF_BYTES_PER_PAGE);
        return true;
	}
	else
	{
        WARNING(TRACE_USIM, "main-memory: read page from invalid physical page: %lu (npages: %lu)\n", 
                pn, main_memory_npages);
        return false;
	}
}

bool main_memory_write_page(uint32_t paddr, uint32_t *buffer)
{
	// paddr[22:8] is page number
	const uint32_t pn = (paddr >> 8) & 0x3FFF;
	// populated memory area ? 
	if (pn < main_memory_npages)
	{
        memcpy(pages[pn], buffer, NUMBER_OF_BYTES_PER_PAGE);
        return true;
	}
	else
	{
        WARNING(TRACE_USIM, "main-memory: write page to invalid physical page: %lu (npages: %lu)\n", 
                pn, main_memory_npages);
        return false;
	}
}

void
main_memory_bus_reset(void)
{
}
