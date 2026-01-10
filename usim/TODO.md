
# bugs

- warm boot

# ideas

# work in progress

- tape support 

# done

- dump running config
- icon file is embedded to header icon.h

## minor changes
 
## unknown/major changes

- change FILE and uch11 to support both fs\_root and sys\_dir options. 
  so sys dir can be mapped to different physical dirs from usim.ini

- remove poll methods in ucode\_run
- move/consolidate machine state
- headless
- performance improvements, profile and check first
- change tracing to have level per facility 

# needs to be clarified

# known to be not supported / postponed

- change adata and mdata to uint32\_t, requires a lot of changes in ALU
- second disk controller (not handled, access will stall lispm)
- cc debuggee side, because it requires many changes that will be used rarely
