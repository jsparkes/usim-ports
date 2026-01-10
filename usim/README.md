<title>MIT CADR simulator</title>

This program was originally written by Brad Parker <brad@heeltoe.com>  ,
and is designed to emulate the MIT CADR microprocessor and hardware
peripherals.  The CADR is a second generation MIT Lisp Machine, and a
good description can be found at
[https://tumbleweed.nu/r/lm-3/uv/cadr.html](https://tumbleweed.nu/r/lm-3/uv/cadr.html).

There is sufficient hardware support for the disk and network to
provide a reasonably functional Lisp Machine experience.

# Getting started (quickly)

Please see the
[README](https://tumbleweed.nu/r/l/file?name=README.md&ci=tip) in the
l project for how to quickly get up and running.

# How to compile

`usim` supports multiple backends for keyboard, video (tv) and mouse support. 
When compiling, one of these backends has to be used. At the moment, there are 
three backends: 

- `x11`: requires X11 client-side library with development headers (e.g. libx11-dev)
- `sdl2`: requires both X11 client-side library with development headers and 
SDL2 library with development files (e.g. libsdl2-dev)
- `sdl3`: requires SDL3 library with development files

`usim` Makefile has following targets:

- `x11`: builds a release usim binary with X11 backend
- `sdl2`: builds a release usim binary with SDL2 backend
- `sdl3`: builds a release usim binary with SDL3 backend
- `x11-debug`: same as x11 but builds a debug usim binary
- `sdl2-debug`: same as sdl2 but builds a debug usim binary
- `sdl3-debug`: same as sdl3 but builds a debug usim binary
- `tools`: builds the tools (readmcr, showmcr, diskmaker, lod and di)
- `all`: builds a release usim binary with backend specified with `WITH_<BACKEND>=1` variable 
   (e.g. `make WITH_X11=1`) and the tools
- `debug`: same as all but builds a debug usim binary
- `all-variants`: builds debug and release usim binaries for all backends
   (e.g. usim-sdl3-debug, usim-sdl3-release)
- `tape.img`: creates a tape.img file to be used as tape media (45000 KB, 2400ft 1600bpi)

The default goal of Makefile is `sdl3`.

## Debug vs. Release

Release binary does not include the following:

- -f, -l, -t, -u command line options
- trace levels debug and info
- assert (release is built with NDEBUG)

Release binary is considerable faster than the debug binary.

# Configuration of usim (usim.ini)

When usim starts, it tries to read usim.ini which is a INI style
configuration file consisting of sections, and key/value pairs.

If you do not already have a usim.ini, one with default parameters 
can be created with usim's `-p` option.

You can include another file in usim.ini using `%include <filename>`.

## `usim` section

  - `state_filename`: Specifies a state file that is saved when usim exits, 
    or either when a user sends SIGINFO/SIGUSR1, or on halt if the -d/-D option 
    was passed to usim.

  - `sys_directory`: Specifies a directory where all System sources
	live, by mapping the `/tree` prefix in the FILE server to this directory.
	Note that all other files and directories, outside `/tree` are
	also available through the local FILE server. 
    Better: use the `fs_root_directory` option below.

  - `fs_root_directory`: Specifies where the root of the files served by 
    the local FILE server is. This provides a better "sandbox" for the FILE server 
    than the `sys_directory` option.  If your sources were previously in 
    your `sys` subdirectory, if you move them to a `tree` subdirectory of 
    the `fs_root_directory`, everything will work fine.
	You can use links in the `fs_root_directory` if you want to use directories 
    outside it, e.g. `/tmp` or `/home`.

  - `kbd`: Specify what keyboard type to use.  This affects what type
	of a keyboard the CADR sees, _not_ the actual keyboard layout from
	the host. Possible values are:

	  - `knight`: Send Knight (old) keyboard scancodes.  [Keyboard
		layout](https://tumbleweed.nu/lm-3/schematics/knight-1-layout.png)
	  - `cadet`: Send Space-Cadet (new) keyboard scancodes. [Keyboard
	Layout](https://tumbleweed.nu/lm-3/schematics/cadet-1-layout.png),
	[Front
	keys](https://tumbleweed.nu/lm-3/schematics/cadet-2-layout.png).

    NOTE: only works with the X11 and SDL2 backends.
    NOTE: SDL3 keyboard is always a space-cadet (new) keyboard.

  - `monitor`: Specify which monitor type to use, cpt or other.
     There is no reason to set this manually or set it to cpt.
     (default: other)
 
  - `grab_keyboard`: If set to `true`, try to grab the keyboard.
     NOTE: only works with the X11 backend.

  - `geometry`: Explicitly sets the position and the display of 
    the usim window. Given as x,y[,disp]. If disp is omitted, 
    the current display is used. The display number can be found in 
    the info level output of usim.
    usim -g command line option overrides this value.
    NOTE: setting display works only with the SDL3 backend.
    (default: -1 -1 -1)

  - `always_on_top`: Makes the usim window always on top. 
    NOTE: only works with the SDL3 backend.
    (default: false)

  - `scale`: Sets the scaling factor for the CADR display.
    NOTE: only works with the SDL2 and SDL3 backend.
    NOTE: When SDL3 backend is used, scale can be set to zero to use the 
    reported display pixel density of the screen. "x11" driver always reports 
    this as 1, but "wayland" driver reports the correct value.
    (default: 1.0)

  - `scale_filter`: Sets the scale filter for the usim window. Available
    values are `nearest` and `linear`. If you set the scaling factor to an
    integer value, the recommended scaling filter is `nearest`.
    NOTE: only works with the SDL2 and SDL3 backend.
    (default: linear)

  - `allow_resize`: If set to `true`, allow the CADR window to be resized.
    NOTE: only works with the SDL2 and SDL3 backend. (default: false)

  - `beep_amplitude`: Volume/amplitude [0..1] of audio beep signal. 
    0 corresponds to muted, 1 corresponds to maximum volume.
    If set to 0, sdl3 audio subsystem is not initialized.
    NOTE: only works with the SDL3 backend. (default: 0.8)

  - `use_ascii_beep`: Use ASCII \a (BEL) for default beep - (beep) asks for
    a beep having wavelength=1488 us, duration=1058us.
    NOTE: only works with the SDL3 backend. (default: false)

  - `special_key`: Use this SDL scancode name as special key.
    NOTE: only works with the SDL3 backend. (default: none)

## `ucode` section

  - `prommcr_filename`: Specifies the boot PROM microcode load.

  - `promsym_filename`: Specifies the symbol table for the boot PROM.

  - `mcrsym_filename`: Specifies the symbol table for the microcode.

## `chaos` section

  - `backend`: Specifies which back-end to use; possible values are:
	 - `daemon`: Tries to connect to the `chaosd` daemon.
	 - `local`: Uses the embedded Chaosnet NCP.
	 - `udp`: Uses Chaos-over-UDP to a Chaosnet bridge.
	 - `hybrid`: Use a hybrid of "local" and "udp" - see `udp_local_hybrid` below.

  - `myname`: Specifies the name of the simulated machine; this is
	used to figure out which Chaosnet address we have.

  - `servername`: Specifies the name of the NCP; this is only used
	when the Chaosnet backend is `local` (or `udp` with the 
    `udp_local_hybrid` option, see below).

  - `bridgeip`: IP or host name of the Chaosnet bridge to use for Chaos-over-UDP.

  - `bridgeport`: UDP port of the Chaosnet bridge (default 42042)

  - `bridgeport_local`: Local UDP port to use for Chaos-over-UDP (default 42042).

  - `bridgechaos`: Chaosnet address of the Chaosnet bridge (octal).

  - `udp_local_hybrid`: Use a hybrid of "local" and "udp", where the address of 
    `servername` is treated locally, but all others are handled over UDP. 
    Requires `udp` backend and configuration of `servername` (and `hosts`).

  - `hosts`: Hosts table containing hostname to Chaosnet address
	mappings.  The format of this file is the same as for the Lisp
	Machine.

## `memory` section

  This is an experimental feature.

  - `size`: Specify the installed physical memory size in KW (kilo-words).
    (default: 2048 meaning 2MW)
    size should be a multiple of 16 and equal or less than 4096.

## `disk` section

  - `diskN` where 0 <= N <= 7
     configured as diskN = [DISK\_UNIT\_TYPE,]DISK\_PACK\_FILE
     DISK\_UNIT\_TYPE can be T-80 or T-300. If omitted, T-300 is assumed.

## `tape` section

  Tape support is under development and is not working yet.

  - `tapeN` where 0 <= N <= 7
     configured as tapeN = [LENGTH,][MODE,]TAPE\_FILE
     LENGTH is tape length in feet. If omitted, 2400 is assumed.
     MODE is ro (read-only) or rw (read-write). If omitted, rw is assumed.
     TAPE\_FILE should be created manually.

## `trace` section

  - `level`: Specify trace level.  See utrace.h for possible levels

  - `facilities`: Specify what facilities to trace.  See utrace.h for
	possible values.

## `idle` section

  - `cycles`: amount of cycles since last work to consider idle
    if set to -1 (or anything negative), idle is disabled.

  - `quantum`: cycles to wait until sleep call

  - `timeout`: timeout for each sleep cycle

idle can considerably decrease the performance. When possible, 
it is recommended to disable idle (cycles=-1). However, this will 
prevent power-saving (e.g. battery will exhaust faster) and prevent 
the system to decrease the speed of the fans (i.e. the computer 
might be noisier).

## `kbd` section

### X11 and SDL2 backends

This maps X11 keys to a corresponding key in the simulator.  The
left-hand side is an X11 key name, the right-hand side is a Lisp
Machine character name that will get inserted.

For example, to map a few host Fn-functions keys to provide access to
keys that are not available on PC keyboards:

```
[kbd]
F1 = system
F2 = network
F3 = status
F4 = terminal
F5 = help
F6 = clear
F7 = break
F10 = hold_output
F11 = abort
F12 = resume
```

To insert the alpha character when pressing a key that is mapped to
Alpha in X11:

```
Greek_alpha = alpha
```
### SDL3 backend

This maps SDL3 keycodes to space-cadet keyboard scancodes. SDL3 keycodes 
identify layout independent physical keys on a keyboard. Space-cadet keyboard 
scancodes also identify the physical keys.

The left-hand side is a SDL keycode name, the right-hand side is a space-cadet 
scancode name. For example:

```
[kbd]
F9 = top
F10 = greek
F11 = hyper
```

Any key on the keyboard can be mapped to any space-cadet key including 
function and modifier keys. Thus, SDL3 backend does not use `kbd.modifiers` 
section. 

In order to help creating a custom mapping, if `kbd` facility is enabled 
in trace at `debug` level, all SDL keycode names are printed. All space-cadet 
scancode names can be found in `sdl3_keyboard_cadet_scancodes.defs` file.

## `kbd.modifiers` section

(kbd.modifiers section is not used by SDL3 backend)

This maps X11 modifiers to the corresponding bucky key in the
simulator.  The left-hand side is an X11 modifier name (Mod1, Mod2,
...), the right-hand side is a corresponding to a Lisp Machine bucky
key.

Not all bucky keys are supported by all keyboard types.  There is also
no support currently to differentiate between left and right bucky
keys.

Available modifier masks from X11 are: Shift, Lock, Control, Mod1,
Mod2, Mod3, Mod4, Mod5.

Available bucky keys: Shift, Top, Control, Meta, ShiftLock.

Cadet specific bucky keys: ModeLock, Greek, Repeat, AltLock, Hyper, Super.

# Simulator keyboard bindings 

## X11 and SDL2 backends

All keys have been mapped so that they should work mostly as
advertised.  Not all keys have been mapped to the host; e.g., Call or
Altmode on the Knight.

X11 Modifier have been mapped as follows, depending on the type of
Lisp Machine keyboard currently in use:

   | Host         | Knight     | Cadet        |
   | ------------ | ---------- | ------------ |
   | Shift        | Shift      | Shift        |
   | Lock         | Shift Lock | Caps Lock    |
   | Control      | Control    | Control      |
   | Mod2 (Alt)   | Meta       | Meta         |
   | Mod4 (Super) | Top        | Super        |

Some additional keys that are hard to type, or do not have a
corresponding key on a normal PC keyboard have been mapped as well.

   | Host         | Knight     | Cadet        |
   | ------------ | ---------- | ------------ |
   | F1           | Top-Escape | System       |
   | F2           | Top-Break  | Network      |
   | F3           | n/a        | Status       |
   | F4           | n/a        | Terminal     |
   | F5           | Top-H      | Help         |
   | F6           | Clear      | Clear-Input  |
   | F7           | Break      | Break        |
   | ------------ | ---------- | ------------ |
   | Page Up      | Top-Call   | Abort        |
   | Page Down    |            | Resume       |
   | Home         |            | Break        |
   | End          | Top-CR     | End          |

## SDL3 backend

A default mapping is provided in `sdl3_keyboard_default_mapping.defs` file, 
and it includes:

- all alphanumeric keys (letters, numbers and punctuation marks)
- tab, backspace (rubout) and return
- left and right shift as shift
- left and right ctrl as ctrl
- left and right win/gui as super
- left and right alt as meta
- caps lock as caps\_lock
- F1 as system, F2 as network, F3 as status, F5 as terminal
- F5 as help, F6 as clear\_input, F7 as clear\_screen
- Escape as alt\_mode
- Home as break, End as end, Page Up as abort, Page Down as resume
- Left, Right, Up and Down arrow keys as hand keys.

The modifiers are mapped as follows:

   | Host         | Cadet        |
   | ------------ | ------------ |
   | Shift        | Shift        |
   | Ctrl         | Ctrl         |
   | Super        | Super        |
   | Alt          | Meta         |
   | Caps Lock    | Caps Lock    |

Some additional keys that are hard to type, or do not have a
corresponding key on a normal PC keyboard have been mapped as well.

   | Host         | Cadet        |
   | ------------ | ------------ |
   | F1           | System       |
   | F2           | Network      |
   | F3           | Status       |
   | F4           | Terminal     |
   | F5           | Help         |
   | F6           | Clear-Input  |
   | F7           | Clear-Screen |
   | ------------ | ------------ |
   | Escape       | Alt Mode     |
   | ------------ | ------------ |
   | Home         | Break        |
   | End          | End          |
   | Page Up      | Abort        |
   | Page Down    | Resume       |
   | ------------ | ------------ |
   | Left         | Hand Left    |
   | Right        | Hand Right   |
   | Up           | Hand Up      |
   | Down         | Hand Down    |

### SDL3 Special Key

When using SDL3 backend, A special key can be assigned with `usim:special_key`
configuration option (for example F12). This key controls various aspects of usim and the emulation.

The special key bindings are (SK represents special key assigned in usim.ini):

   | ------------ | ------------ |
   | SK-1         | change scale filter (scale mode) to linear |
   | SK-2         | change scale filter (scale mode) to nearest |
   | SK-d         | Dump state as a numbered file (usim-\<NNN\>-\<kbd\>-0.state) |
   | ------------ | ------------ |

# Color TV

SDL3 backend supports the additional Color TV. This can be enabled by
using `-a` option, e.g. `./usim -a`. The Color TV window is shown next to
the main TV window.

# diskmaker

diskmaker is a utility for managing disk packs.

A T-300 disk pack can be created using diskmaker.t300.template. The partition table 
in the template can be customized according to particular needs.

## diskmaker template format

```
# LINE COMMENTS START WITH # symbol
cylinders       <POSITIVE INTEGER NUMBER < 4096>
heads           <POSITIVE INTEGER NUMBER < 256>
blockspertrack  <POSITIVE INTEGER NUMBER < 256>
drive           <DRIVE_NAME_MAX_32_CHARACTERS>
pack            <PACK_NAME_MAX_32_CHARACTERS>
comment         <COMMENT_MAX_32_CHARACTERS>
mcr <CURRENT_MICROLOAD>
lod <CURRENT_BAND>
partitions:
<PARTITION_NAME_4_CHARACTERS>      <START>     <SIZE>
...
```

The disk parameters, cylinders, heads and blockspertrack should be given as 
integer constants in C (octal, decimal or hexadecimal). There is also an 
optional blockspercylinder parameter, this is set to heads * blockspertrack 
by default.

mcd and lod identifies the current microload and band. These should be valid 
partition names. These can also be modified later with set-microload and 
set-band diskmaker commands.

Drive name, pack name and comment are for information. These can also be 
modified later with set-drive-name, set-pack-name and set-comment diskmaker 
commands.

The partition table is given as a list of entries under "partitions:". There 
can be maximum 18 partitions, this is independent of the disk size.

The name of a partitition has to be 4 characters. Explicitly use space for 
padding.

The start of a partition is given as the number of blocks from the start of 
the disk. This can be given as integer constants in C (octal, decimal or 
hexadecimal). It can also be set to dash/`-` symbol to set it to the end of 
the previous partition.

The first partition has to start from the track number 1, the first track 
(track number 0) is reserved. This means the start block of the first partition 
has to be equal to blockspertrack. If `-` is used, this is done automatically. 
If there is a mistake, a warning message is displayed.

The size of a partition is given as the number of blocks. This can be given as 
integer constants in C (octal, decimal or hexadecimal). For the last partition, 
the size can also be set to dash/`-` for partition to span the disk till the end.

The size of a partition can also be given as number of cylinders. If it starts 
with letter `c`, this means the size is number of cylinders, e.g. c5 means 
5 cylinders. Additionally, this means the start of this partition is aligned 
with the cylinder boundary. The diskmaker.t80/t300.template files are written 
like this because they are also defined in this way in dledit.lisp.

The comment of the partition is not given in the disk template, however it can be 
modified later with set-partition-comment diskmaker command.

The template of an existing disk pack can be displayed with show-template 
diskmaker command.

# What programs are here?

  - usim: MIT CADR simulator
  - diskmaker: utility for managing disk packs

Debugging utilities:

  - readmcr: microcode disassembler

# Release History

> v0.10 - TBD

> v0.9 - Minor speedups.
>        Mac OSX (little endian) fixes.
>        Warm start support (usim.state).
>        Mouse/microcode synchronisation (thanks to Devon for the idea)

> v0.8 - Speedups and bug fixes.
>        chaosd/FILE server supports rebuilding sources from server.
>        Can now resize screen.

> v0.7 - Added raw X11 support.
>        Bjorn Victor's new keyboard configuration code.
>        Diskmaker now takes a template file and will show info on existing
>          disk images.

> v0.6 - Better network support.

# Standing on the shoulders of giants

I (Brad Parker) would like to thanks the following people for helping
me on this, er, project:

  - Tom Knight (TK)
  - Howard Shrobe
  - Richard Greenblatt (RG)
  - Daniel Weinreb
  - Al Kossow
  - George Carrette
  - Steve Krueger
  - Alastair Bridgewater
  - John Wroclawski
  - Bjorn Victor
  - Devon Sean McCullough

Without their support or encouragement I would probably not have done
this.  Certainly if Al had not sent me the PROM images I would never
have started.  And without Daniel's box-of-tapes I could never have
succeeded.  RG offered some good explanations when I was confused.  TK
and Howard were extremely supportive at the just right moment (and
answered a lot of email).  George offered many good suggestions and
answered lots of questions.  Steve helped me locate missing pages from
"memo 528".  Alastair did some amazing work on several Explorer
emulators.  Bjorn has used the code, offered many suggestions, fixes
and improvements.  And John's office is where I first saw a 3600
console and said, "what's that?".
