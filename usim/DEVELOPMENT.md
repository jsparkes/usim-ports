
# Overview

# cc

cc requires machine to be halted and stepped. also statistics counter and opc shift register.

# Idle

The grand aim for idle is to understand lispm is idling, so the cpu (not cadr, the actual cpu of the computer) can be throttled down and by throttling down cpu can decrease its power usage. This is useful particularly on laptops.

Without modifying lispm side, it is implemented as reading the tv screen memory for the run lights, and if run lights are not working, lispm is idling. So by using select, it kinda sleeps, so cpu has time to sleep/decrease power as well.

# Disk

[disk]
disk0=T-300,disk.img

backward compatibility is not provided. if you see:
[disk]
disk0\_filename=...
assume it is T-300 or maybe read label

- if disk.img is accessible (rw) and has right size, all ok, online
- if there is no disk.img, warn in usim
- if the disk file size is wrong, err in usim, quit from usim (print what it is, what is expected etc.)
- if disk0 is not configured, err in usim

# Code Style

- expand tabs, use spaces
- tab / indent width 4 columns
- do not retab existing files, only change the updated part

# VS Code

To enable terminal bell, use set sound=on of accessibility.signals.terminalBell setting.

# Missing Features

Missing features compared to CADR:

- Unibus:
  - Diagnostic Interface, only the very minimum implemented for machine to work
  - General Purpose I/O Port
  - Serial I/O Port

# Makefile

- objects are created under OBJDIR=.objs/USIM\_BACKEND
- dependency files are created under DEPDIR=.deps/USIM_BACKEND
- <X>_SRCS are compiled into OBJDIR with implicit rule which also creates dependency files.
- chaos repository directory is CHAOSDIR (default $pwd/../chaos), if the default is not correct, set CHAOSDIR externally e.g. make CHAOSDIR=...
- required chaos objects are compiled into OBJDIR with explicit rules and the outputs have chaos. prefix. no dependency files are created.
- libhosts.a is compiled by invoking make and libhosts.a is copied back to OBJDIR with chaos. prefix.

## Make variables

- select backend: `WITH_X11=1`, `WITH_SDL2=1` or `WITH_SDL3=1`

# Code Organization

Looking at this diagram:

![Processor Data Paths](https://tumbleweed.nu/lm-3/schematics/lmdoc/fig1.png "Processor Data Paths")

The components in the diagram are implemented as:

- PROCESSOR by ucode, uexec, m32, uvmem
- BUS ADAPTOR by bus-adaptor
- Not shown on the diagram, unibus mapping, two-machine lashup, 
  error and interrupt registers by bus-interface
- IOB by iob
  - IOB/Chaos Net by uch11* and chaos
  - IOB/Keyboard by backend specific implementations, e.g. sdl3-keyboard and sdl3-audio
  - IOB/Mouse by backend specific implementations, e.g. sdl3-mouse
- MEMORY by main-memory
- DISK CONTROL by disk-controller (and DISK UNITs by disk-unit)
- TV CONTROL by tv and backend specific implementations, e.g. sdl3_video

Two-machine lashup is implemented with lashup-debugger and lashup-debuggee.

Other backend sources:

- cadet, kbd, knight, lmch, mouse for X11 and SDL2 backends
- x11 for X11 backend
- sdl2 for SDL2 backend

Not exist in a hardware CADR but required by the usim emulator are following:

- entry point: usim.c
- configuration management: ini and ucfg
- tracing: trace, utrace
- disassembler: udiss
- symbol management: usym 
- misc functions: misc
- disk management (diskmaker utility): diskmaker
- dump microcode (readmcr utility): readmcr

# Error Handling and Logging

Unrecoverable errors are logged with errx(1, ...) so they are always printed to stderr and application quits.

Important warnings are logged with warnx(...) so they are always printed to stderr.

All other, logging or tracing style messages are logged using the configurable trace facility. Register accesses are normally printed using INFO but for things outputing too much log it is DEBUG or totally disabled.

# CC requires

- lashup / debugger-debuggee cable connection
- unibus mapping
- step
- nop11
- ld

## Notes on Debug Connection

NOP11 IDEBUG STEP is asserted at the same time (by CC). I think the reason is the machine is normally halted after the read phase of the clock. So IR is decoded but targets are not written. NOP11 prevents the write phase effects. IDEBUG causes to load next IR from DEBUGIR. STEP steps once more, executing effectively DEBUGIR.

# Boot

- prom jumps to I-MEM:6 (uc-cadr.lisp)
  - reads 764112 (KBD CSR) to see if there is something on keyboard (CSR<5>)
  - if not, jumps to COLD-BOOT
  - if there is, reads the character from 764100 (KBD LOW)
    - if keycode (scancode[6:0]) is 46 (rubout), it is cold-boot
  - write PROM-DISABLE=1 and SPEED=NORMAL to 766012 (MODE)
  - jump BEG0000

- resets virtual memory mapping
- sets err stop = 1, speed = 0
- reads disk

# Core

# Backends

## X11

## SDL2

## SDL3

### Entry-point (sdl3_main)

SDL3 backend uses SDL callbacks. This means the C main is implemented internally in SDL3 library which calls four callbacks (Init, Iterate, Event, Quit) in sdl3_main.

Iterate updates the display (copies tv:tv_bitmap to display) and Event handles the keyboard and mouse events.

This means the CADR execution has to be run in a separate thread, which is created at the end of Init callback. Thus, CADR execution and display update/event handling are in different threads.

### Keyboard (sdl3_keyboard)

SDL3 keyboard support has major differences, and it is implemented independent of X11 and previous keyboard implementations; knight.\*, cadet.\*, lmch.\* and kbd.\* are not required. It is a space-cadet keyboard implementation.

The major differences compared to previous implementations are as follows:

- SDL Scancodes (not X11 keysym) are used internally for keyboard mapping. The mapping is given with SDL KeyCode name because it is independent of layout, this is immediately converted to SDL Scancode to add the mapping. This means a physical key in the keyboard is mapped as a (space-)cadet keyboard key. 

- SDL3 keyboard implementation contains an independent thread to supply the codes to IOB when IOB is not busy.

SDL3 keyboard implementation includes a queue and an independent running thread. Generated up-down and all-keys-up codes are enqueued first. The thread contains an infinite loop, pops/dequeues a scancode from the queue, and then waits a bit until keyboard ready (CSR<5>) is reset (zero) and then supplies the code to IOB and sets keyboard ready (CSR<5>). If the queue is full or iob is not available in a short time, the scancode is dropped and a logged.

When an SDL3 key down event arrives:

- if the key is not mapped, event is ignored
- boot combination is checked (all ctrl and meta keys + return or rubout)
- alternative boot combination is checked (F12 + return or rubout)
- if the key is a lock key (caps, alt, mode), its state is reversed
- an up-down code is enqueued
- if boot requested, boot code enqueued and up codes are prevented (a flag is set)

When an SDL3 key up event arrives:

- if up code is prevented because boot is requested, event is ignored
- if the key is not mapped, event is ignored
- state of the lock keys are checked, if any are down, it means a modifier is down
- keyboard state is checked to find out if any modifiers and any non-modifiers are down
- if a non-modifier is down or there is no key down, an up-down code is enqueued
- else, an all-keys-up code is enqueued

#### Keyboard mapping

SDL3 keyboard implementation has a default keyboard mapping (in sdl3_keyboard_default_mapping.defs) for obvious space-cadet keys in a modern keyboard and a reasonable set of special cadet keys to allow quickly start using the emulator. The default mapping contains:

- alphanumeric-keys (including letters, numbers, grave, minus, equals, left and right brackets, backslash, semicolon, apostrophe, comma, period, slash) and space
- tab, backspace (rubout) and return
- left and right shift (shift)
- left and right control (ctrl)
- left and right win (super)
- left and right alt (meta)
- caps lock (caps_lock)
- F1 (system), F2 (network), F3 (status), F4 (terminal)
- F5 (help), F6 (clear_input), F7 (clear_screen)
- Escape (alt_mode)
- home (break), end (end), page up (abort), page down (resume)
- left, right, up and down (hand keys)

All these keys can be reassigned and the other keys not mentioned above can be assigned.

The mapping is done in the configuration file kbd section. kbd.modifiers section is not used, all keys are mapped in kbd section.

If kbd is included in trace facility and level is set to debug, all keycode names are printed to help defining a custom mapping.

In the configuration file kbd section, a mapping is given as `SDL_KeyCode_Name = Cadet_Key_Name`. All cadet key names are listed in sdl3_keyboard_cadet_scancodes.defs file and SDL keycode names for en-US keyboard is listed in sdl3_keyboard_default_mapping.defs. kbd section can be like this:

```
[kbd]
F9 = top
F10 = greek
F11 = hyper
```

### Mouse (sdl3_mouse)

SDL3 mouse implementation is similar to SDL2. Only differences are:

- mouse_warp is called (and done if needed) on each mouse event
- IOB does not use the variables in sdl3_mouse but has its own variables

### Beep/Audio (sdl3_audio)

Beep/Audio is tricky to implement to cover everything.

CADR does not call beep once even for a single tone. It calls it repeatedly.
For example, for standard beep, it calls beep multiple times with parameters: 
wavelength=1488 us, duration=1058 us. Because the duration is very short, 
it is harmful to resume/pause or flush audio stream at each call. This means 
the audio stream should be resumed at init. There are probably two solutions:

- dont use audio callback. feed data when requested (beep called). 
  flush data and change freq when a call with different wavelength is made.

- use audio callback. feed data continuously. when beep is called, feed valid 
  data. when call is expired, feed silence.

First seems to be simpler to implement, and it is implemented at the moment. 
The problem is beep cannot wait for very small durations or audio buffer 
suffers. So it checks if duration is less than a magical number 100ms, it does 
not wait.

Second may solve the wait problem just described, but it is tricky to implement.

### TV (sdl3_video)

SDL3 video implementation has major differences compared to X11 and SDL2.

SDL3 video output is synced to display vsync, thus the frame buffer (tv:tv_bitmap) is always displayed with display refresh (typically 60Hz).

SDL3 hidpi support (scaling) has two modes:

- if usim:scale configuration option is omitted or set to zero, pixel density reported by the display (video driver) is used. This means using the native scale (of the system) because display driver coordinates are already scaled. (e.g. using a 4K display in 200% scale reports FullHD coordinates)

- if usim:scale configuration option is not set to zero, it is used explicitly to create a scaled window (width x scale, height x scale) but frame buffer (tv:tv_bitmap) is kept at unscaled resolution.

The window can be resized but the aspect-ratio of the rendering area cannot be changed.

### Other Differences in SDL3

In X11 and SDL2, there is a `iob_poll` and `tv_poll` calls in main loop 
(in ucode\_run). There is no need for `tv_poll` in SDL3 and there is no need 
for `mouse_poll` called in `iob_poll` but `uch11_poll` is needed. Rather than
calling this, a separate thread runs calling uch11\_poll and sleeping 1ms 
continuously. This is uch11\_thread in ucode.

### Known Issues

- audio not working
- warm boot from keyboard not working
- when using wayland, window position cannot be set
- when using wayland, on resize, texture scaling quality is bad
