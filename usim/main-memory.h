#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

// last 512 pages are reserved to unibus
// before that, 512 pages are reserved as I/O area of the xbus
// before that, 4 pages point to A-memory
// thus last 512+512+4=1028 physical pages (of 16384) cannot be accessed
// so the actual maximum is 16384-1028=15356 pages
// but for the sake of simplicity i set the max to 16K pages
#define NUMBER_OF_MAX_MAIN_MEMORY_PAGES 16384

extern uint32_t main_memory_npages;;

bool main_memory_read(uint32_t paddr, uint32_t * pv);
bool main_memory_write(uint32_t paddr, uint32_t v);

// read/write page methods are used by disk-controller
// this is an optimization, real hardware would read/write word by word
// buffer has to be uint32_t[256]
bool main_memory_read_page(uint32_t paddr, uint32_t *buffer);
bool main_memory_write_page(uint32_t paddr, uint32_t *buffer);

void main_memory_load_page_from_file(uint32_t pn, int fd);
void main_memory_save_page_to_file(uint32_t pn, int fd);

void main_memory_bus_reset(void);
