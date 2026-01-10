Chaosnet for Unix

Chaosnet was an early (pre-ethernet) computer network developed at
MIT.  It was an early "common bus" packet network, i.e. all the
machines shared a single "wire" and sent packets back and forth.

Chaosnet was used to connect many machines at MIT, most notably the
PDP-10's running ITS and the Lisp Machines.

The CADR simulator [usim](https://tumbleweed.nu/r/usim/) can be a
client and uses this code to talk to the FILE server ("FILE" is the
file serving protocol used on Chaosnet).

This code was initially written by Brad Parker <brad@heeltoe.com>

# Release History

> 07/11/06 - Fixed FILE server to force everything to lower case (still
>             a bug in close - don't open multiple files).
>           Some macos fixes, doesn't work yet in big endian.
>			Works well enough to support (si:recompile-world).

> 12/13/05 - Original release.
