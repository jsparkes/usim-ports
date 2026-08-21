include $(PWD)/../Kbuild

all:
	make -C $(LINUX_DIR) M=$(PWD) src=$(PWD)/.. modules

clean:
	make -C $(LINUX_DIR) M=$(PWD) src=$(PWD)/.. clean

.PHONY: TAGS
TAGS:
	find $(PWD)/.. -name "*.[ch]" -print | etags -i$(LINUX_DIR)/TAGS -
