#pragma once

#include <stdint.h>

void bus_adaptor_read(uint32_t paddr, uint32_t *pv);
void bus_adaptor_write(uint32_t paddr, uint32_t v);

void bus_adaptor_unibus_read(uint32_t uaddr, uint16_t *pv);
void bus_adaptor_unibus_write(uint32_t uaddr, uint16_t v);