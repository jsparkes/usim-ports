#pragma once

#include <stdbool.h>
#include <stdint.h>

void bus_interface_set_xbus_nxm(void);
void bus_interface_set_unibus_nxm(void);
void bus_interface_set_nxm_inhibit(bool);

void bus_interface_set_unibus_map_error(void);

void bus_interface_reset_bus_error_status(void);
uint16_t bus_interface_get_bus_error_status(void);

bool bus_interface_is_xbus_nxm(void);
bool bus_interface_is_unibus_nxm(void);
bool bus_interface_is_unibus_map_error(void);

void bus_interface_set_debuggee_bus_error_status(uint16_t);

void bus_interface_read(uint32_t offset, uint16_t *pv);
void bus_interface_write(uint32_t offset, uint16_t v);

void bus_interface_bus_reset(void);
