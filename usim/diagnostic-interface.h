#pragma once

#include <stdbool.h>
#include <stdint.h>

void diagnostic_interface_read(uint32_t offset, uint16_t *pv);
void diagnostic_interface_write(uint32_t offset, uint16_t v);
