SHELL := /bin/sh

# disable suffix rules
SUFFIXES:
# disable implicit rule
%.o: %.c

OBJDIR := .objs-di
DEPDIR := .deps-di

.DEFAULT_GOAL := all
.PHONY: all clean

all: di

clean:
	rm -rf di $(DEPDIR) $(OBJDIR) di.dSYM

CFLAGS = -std=gnu99 -Wall -Wextra -I. -O3 -ggdb3
DEPFLAGS = -MT $@ -MMD -MP -MF $(DEPDIR)/$*.d

$(OBJDIR): ; mkdir -p $@
$(DEPDIR): ; mkdir -p $@

# implicit rule to compile objects
$(OBJDIR)/%.o: %.c $(DEPDIR)/%.d | $(OBJDIR) $(DEPDIR)
	$(CC) $(CFLAGS) $(DEPFLAGS) -c -o $@ $<

SRCS := di.c misc.c unfasl.c usym.c
OBJS := $(SRCS:%.c=$(OBJDIR)/%.o)
DEPS := $(SRCS:%.c=$(DEPDIR)/%.d) 

di: $(OBJS)
	$(CC) $(CFLAGS) -o $@ $^ $(LDFLAGS)

# dependency files has no dependency
$(DEPS):

include $(wildcard $(DEPS))
