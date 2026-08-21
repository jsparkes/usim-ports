VERSION = 0.9-ams

SHELL := /bin/sh

.DEFAULT_GOAL := sdl3

ifdef WITH_X11
BACKEND = x11
else ifdef WITH_SDL2
BACKEND = sdl2
else ifdef WITH_SDL3
BACKEND = sdl3
endif

.PHONY: help
help:
	@echo "make all: make a release build for $(BACKEND)"
	@echo "make debug: make a debug build for $(BACKEND)"
	@echo "make tools: make tools (readmcr, showmcr, diskmaker, lod, di)"
	@echo "make clean: clean build artifacts"
	@echo "make help: show this help"
	@echo "make {x11,sdl2,sdl3}: make a {x11,sdl2,sdl3} release build"
	@echo "make {x11,sdl2,sdl3}-debug: make a {x11,sdl2,sdl3} debug build"
	@echo "make all-variants: make a debug and a release build for x11, sdl2 and sdl3"
	@echo "make usim.app: make a macOS app file from sdl3 release binary"

ifdef BACKEND

.PHONY: all
all: tools
	$(MAKE) -f Makefile.usim USIM_BACKEND=$(BACKEND) USIM_BUILD_TYPE=release

.PHONY: debug
debug: tools
	$(MAKE) -f Makefile.usim USIM_BACKEND=$(BACKEND) USIM_BUILD_TYPE=debug

else

.PHONY: all
all:
	$(error "one of WITH_X11, WITH_SDL2 or WITH_SDL3 must be set to 1.")

.PHONY: debug
debug:
	$(error "one of WITH_X11, WITH_SDL2 or WITH_SDL3 must be set to 1.")

endif

.PHONY: all-variants
all-variants:
	make x11
	mv usim usim-x11-release
	make x11-debug
	mv usim usim-x11-debug
	make sdl2
	mv usim usim-sdl2-release
	make sdl2-debug
	mv usim usim-sdl2-debug
	make sdl3
	mv usim usim-sdl3-release
	make sdl3-debug
	mv usim usim-sdl3-debug

icon.h: icon.bmp
	xxd -i icon.bmp

.PHONY: tools
tools:
	$(MAKE) -f Makefile.readmcr
	$(MAKE) -f Makefile.showmcr
	$(MAKE) -f Makefile.diskmaker
	$(MAKE) -f Makefile.lod
	$(MAKE) -f Makefile.di

.PHONY: x11
x11:
	$(MAKE) -f Makefile.usim USIM_BACKEND=x11 USIM_BUILD_TYPE=release

.PHONY: sdl2
sdl2:
	$(MAKE) -f Makefile.usim USIM_BACKEND=sdl2 USIM_BUILD_TYPE=release

.PHONY: sdl3
sdl3:
	$(MAKE) -f Makefile.usim USIM_BACKEND=sdl3 USIM_BUILD_TYPE=release

.PHONY: x11-debug
x11-debug:
	$(MAKE) -f Makefile.usim USIM_BACKEND=x11 USIM_BUILD_TYPE=debug

.PHONY: sdl2-debug
sdl2-debug:
	$(MAKE) -f Makefile.usim USIM_BACKEND=sdl2 USIM_BUILD_TYPE=debug

.PHONY: sdl3-debug
sdl3-debug:
	$(MAKE) -f Makefile.usim USIM_BACKEND=sdl3 USIM_BUILD_TYPE=debug

.PHONY: clean
clean:
	$(MAKE) -f Makefile.usim clean
	$(MAKE) -f Makefile.readmcr clean
	$(MAKE) -f Makefile.showmcr clean
	$(MAKE) -f Makefile.diskmaker clean
	$(MAKE) -f Makefile.lod clean
	$(MAKE) -f Makefile.di clean
	rm -rf usim-x11-release usim-x11-debug
	rm -rf usim-sdl2-release usim-sdl2-debug
	rm -rf usim-sdl3-release usim-sdl3-debug
	rm -rf usim.app
	rm -f core
	rm -f *.pbm *.tga
	rm -f *.state
	rm -f *.i *.s *.bc

.PHONY: usim.app
usim.app: sdl3
	mkdir -p usim.app/Contents/MacOS
	cp usim usim.app/Contents/MacOS
	cp Info.plist usim.app/Contents

tape.img:
	dd if=/dev/zero of=tape.img count=45000 bs=1024

FORCE:

.PHONY: TAGS
TAGS:
	find . -type f -iname "*.[ch]" | etags -
