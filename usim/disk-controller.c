/* disk.c -- emulate a Trident disk
 *
 * Each disk block contains one Lisp Machine page worth of data,
 * i.e. 256. words or 1024. bytes.
 *
 * The block no is an uint32_t, thus maximum 4G. This is not a disk size
 * limitation because disk size = block no * block size
 * Thus, disk size < 4T is OK from this perspective.
 *
 * Max possible disk size is 4095 cylinders x 255 heads x 255 blocks per track 
 * = 266277375 blocks = 266277375 * 1K = 272668032000 B < 253.95 GB
 *
 * start read/read compare/write commands calls submit_xfer
 * submit_xfer creates xfer_request, and:
 * - if blocking, calls do_xfer and the disk operation is performed
 * - if nonblocking, returns and 
 *      the thread reads the xfer_request and completes the operation
 */

#include <assert.h>
#include <err.h>
#include <fcntl.h>
#include <pthread.h>
#include <sched.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <unistd.h>

#include "bus-interface.h"
#include "config.h"
#include "disk-controller.h"
#include "disk-unit.h"
#include "idle.h"
#include "machine-control.h"
#include "main-memory.h"
#include "misc.h"
#include "ucfg.h"
#include "ucode.h"
#include "utrace.h"

// implementation may support >8 disks, 
// but this is a limitation also in the disk controller protocol 
// do not change
#define SELECTED_UNIT() ((da >> 28) & 07)
#define SELECTED_UNIT_PTR() (disk_units + ((da >> 28) & 07))

// --- REGISTERS ---

// --- READ ---

// status (read-only)
// status is generated on the fly

// memory address (read-only)
// generated from the selected disk unit

// disk address (read/write)
// when reading, da is generated on the fly

// error correction register (read-only)
// no ECC errors in usim, so this always returns 0

// --- WRITE ---

// command (write-only)
// writing to a register does not initiate a transfer
// however writing the command register reset various error flags
static uint32_t cmd;

// command list pointer (write-only)
// pointing to channel command words, CCW(s)
static uint32_t clp;

// disk address (read/write)
// this register is used only for write
static uint32_t da;

// start (write-only)
// start initiates the command, it has no register
// ---

struct xfer_req_s
{
    bool ready;
    struct disk_unit_s *p;
    bool read;
    bool compare;
    uint32_t clp;
    uint32_t cylinder;
    uint32_t head;
    uint32_t block;
} xfer_req = {0};

static bool reset_condition;

#ifdef WITH_NONBLOCKING_DISKIO
static pthread_t thread;
static bool thread_run;
#endif

struct status_s
{
    bool read_compare_difference;
    bool ccw_cycle;
    bool nonexistent_memory_error;
    bool interrupt_request;
    bool not_active;
} status;

bool done_interrupt_enable;
bool attention_interrupt_enable;

static void
assert_interrupt(void)
{
    DEBUG(TRACE_DISK, "disk-controller: assert interrupt\n");
    status.interrupt_request = true;
    assert_xbus_interrupt();
}

static void
deassert_interrupt(void)
{
    DEBUG(TRACE_DISK, "disk-controller: deassert interrupt\n");
    deassert_xbus_interrupt();
}

static void
set_status_not_active()
{
    status.not_active = true;

    // "<11>    Done Interrupt Enable.  Enables not-active 
    // (bit 0 of the status register) to cause an interrupt.  The interrupt 
    // will keep happening until you clear this bit.  (This is really an 
    // idle interrupt rather than a done interrupt.)"
 
    if (done_interrupt_enable)
    {
        assert_interrupt();
    }
}

static void
set_status_active()
{
    status.not_active = false;
}

static void
reset_status(void)
{
    status.read_compare_difference = false;
    status.ccw_cycle = false;
    status.nonexistent_memory_error = false;
    status.interrupt_request = false;
    set_status_not_active();
}

static void
reset(void)
{
    done_interrupt_enable = false;
    attention_interrupt_enable = false;
    reset_status();
    cmd = 0;
    clp = 0;
    da = 0; 
}

static uint32_t
encode_status()
{
    uint32_t v = 0;

    // internal parity error never happens in usim
    // memory parity error never happens in usim
    // header compare error never happens in usim
    // header ecc error never happens in usim
    // ecc hard never happens in usim
    // ecc soft never happens in usim
    // read overrun never happens in usim
    // write overrun never happens in usim
    // start block error never happens in usim
    // timeout error never happens in usim
    // how multiple units selected, happens ???

    if (status.read_compare_difference) v |= (1 << 22);
    if (status.ccw_cycle) v |= (1 << 21);
    if (status.nonexistent_memory_error) v |= (1 << 20);

    struct disk_unit_s *p = SELECTED_UNIT_PTR();

    // no block-counter in usim

    if (p->seek_error) v |= (1 << 10);
    if (!p->online) v |= (1 << 9);
    // usim disk units are always on cylinder
    if (p->read_only) v |= (1 << 7);
    if (p->has_fault) v |= (1 << 6);
    if (p->attention) v |= (1 << 2);

    if (status.interrupt_request) v |= (1 << 3);

    // any attention
    for (size_t i = 0; i < NUMBER_OF_DISK_UNITS; i++)
    {
        if (disk_units[i].attention)
        {
            v |= (1 << 1);
            break;
        }
    }

    if (status.not_active) v |= (1 << 0);

    return v;
}

static void
decode_da(uint32_t da, uint32_t *unit, uint32_t *cylinder, uint32_t *head, uint32_t *block)
{
    *unit = (da >> 28) & 07;
    *cylinder = (da >> 16) & 07777;
    *head = (da >> 8) & 0377;
    *block = da & 0377;
}

//
// NOTES ON perform_xfer IMPLEMENTATION
//
// In the disk xfer performing method below, main_memory_read/write_page 
// is a usim optimization.
//
// In the hardware, memory is naturally read word by word.
// However, disk xfer operations are always done page by page, since CLW 
// specifies only the page number (not a full address).
//
// Thus, memory can be read/write page by page with this method.
//
// A side effect is, MA should point to the last (full) memory address
// accessed (for reading or writing). Thus, during a page read/write from
// memory, MA cannot be set to individual addresses. Because of this,
// it is set to the start address before calling main_memory_read/write_page
// and then, if the method returns true, it is set to to start+255.
//
// This is not ideal but should be correct, since there cannot be a memory
// error in the middle of a page, in usim.
//
// This implementation passes cc:dcheck
//
static bool
perform_xfer(struct disk_unit_s* p, bool read, bool compare, uint32_t clp)
{
    status.read_compare_difference = false;
    status.ccw_cycle = false;
    status.nonexistent_memory_error = false;

    uint32_t buffer[256];
    uint32_t buffer_compare[256];

    // "Only bits <15:0> of the CLP can count, so if you try to carry across 
    // this boundary your command list will wrap around."
    uint16_t clp_offset = 0;
    while (true)
    {
        uint32_t ccw;
        uint32_t current_clp = clp + clp_offset;
        p->last_memory_address = current_clp;
        status.ccw_cycle = true;
        if (!main_memory_read(current_clp, &ccw)) 
        {
            status.nonexistent_memory_error = true;
            return false;
        }
        status.ccw_cycle = false;

        const uint32_t paddr = (ccw & 0x00FFFF00);

        DEBUG(TRACE_DISK, "disk-controller: mem[clp=%o] -> ccw %08o, paddr: %08o\n", 
                current_clp, ccw, paddr);

        if (read)
        {
            // read a block from disk
            if (disk_unit_read(p, buffer))
            {
                if (compare)
                {
                    // read a block from memory
                    p->last_memory_address = paddr;
                    if (main_memory_read_page(paddr, buffer_compare))
                    {
                        p->last_memory_address = paddr + 255;

                        if (memcmp(buffer, buffer_compare, 1024) != 0)
                        {
                            // "This error does not stop the transfer."
                            status.read_compare_difference = true;
                        }
                    }
                    else
                    {
                        WARNING(TRACE_DISK, "disk-controller: compare, main_memory_read_page failed\n");
                        status.nonexistent_memory_error = true;
                        return false;
                    }

                }
                else
                {
                    // write the block to memory
                    p->last_memory_address = paddr;
                    if (main_memory_write_page(paddr, buffer))
                    {
                        // success, read completed
                        p->last_memory_address = paddr + 255;
                    }
                    else
                    {
                        WARNING(TRACE_DISK, "disk-controller: read, main_memory_write_page failed\n");
                        status.nonexistent_memory_error = true;
                        return false;
                    }
                }
            }
            else
            {
                WARNING(TRACE_DISK, "disk-controller: %s, disk_unit_read failed\n",
                        compare ? "compare" : "read");
                // disk error
                p->has_fault = true;
                return false;
            }
        }
        else
        {
            // read a block from memory
            p->last_memory_address = paddr;
            if (main_memory_read_page(paddr, buffer))
            {
                p->last_memory_address = paddr + 255;

                // write to disk
                if (disk_unit_write(p, buffer))
                {
                    // success, write completed
                }
                else
                {
                    WARNING(TRACE_DISK, "disk-controller: write, disk_unit_write failed\n");
                    // disk error
                    p->has_fault = true;
                    return false;
                }
            }
            else
            {
                WARNING(TRACE_DISK, "disk-controller: write, main_memory_read_page failed\n");
                status.nonexistent_memory_error = true;
                return false;
            }
        }
        // is it last ccw ?
        if ((ccw & 1) == 0) 
        {
            break;
        }
        else
        {
            if (!disk_unit_seek_next_lba(p))
            {
                return false;
            }

            clp_offset++;
        }
    }

    return true;
}

static void
do_xfer(void)
{
    if (xfer_req.ready)
    {
        DEBUG(TRACE_DISK, "disk-controller: do_xfer %s %d/%d/%d %011o\n", 
                xfer_req.read ? (xfer_req.compare ? "compare" : "read") : "write",
                xfer_req.cylinder,
                xfer_req.head,
                xfer_req.block,
                xfer_req.clp);

        if (disk_unit_seek(
                    xfer_req.p,
                    xfer_req.cylinder, 
                    xfer_req.head, 
                    xfer_req.block))
        {
            perform_xfer(
                    xfer_req.p, 
                    xfer_req.read, 
                    xfer_req.compare, 
                    xfer_req.clp);

            da = disk_unit_da(xfer_req.p);
        }
        else
        {
            WARNING(TRACE_DISK, "disk-unit %d: seek error\n", xfer_req.p->unit);
        }

        xfer_req.ready = false;

        set_status_not_active();
    }
}

static void
submit_xfer(bool read, bool compare)
{
    DEBUG(TRACE_DISK, "disk-controller: new xfer request\n");

    set_status_active();

    uint32_t unit;
    decode_da(da, &unit, &(xfer_req.cylinder), &(xfer_req.head), &(xfer_req.block));
    xfer_req.p = disk_units + unit;
    xfer_req.read = read;
    xfer_req.compare = compare;
    xfer_req.clp = clp;
    xfer_req.ready = true;

#ifndef WITH_NONBLOCKING_DISKIO
    do_xfer();
#endif
}

static void
start_read(void)
{
    submit_xfer(true, false);
}

static void
start_read_compare(void)
{
    submit_xfer(true, true);
}

static void
start_write()
{
    struct disk_unit_s* p = SELECTED_UNIT_PTR();

    // "Writing while the disk is read-only causes a fault."
    if (p->read_only)
    {
        p->has_fault = true;
    }
    else
    {
        submit_xfer(false, false);
    }
}

static void
start_seek()
{
    // "0004_Seek.  Initiates a seek to the cylinder specified in the disk
    // address register.  An attention will occur when the seek
    // completes.  Note that this command is not logically necessary;
    // the controller always initiates a seek if necessary at
    // the start of a data transfer command.  The read, read-compare,
    // and write commands also will seek in the middle of a transfer
    // when necessary.  The seek command is provided so you can
    // overlap seeks on multiple units."
    
    set_status_active();
    uint32_t unit, cylinder, head, block;
    decode_da(da, &unit, &cylinder, &head, &block);
    struct disk_unit_s *p = disk_units + unit;
    disk_unit_seek(p, cylinder, head, block);
    disk_unit_raise_attention(p);
    set_status_not_active();
}

static void
start_recalibrate()
{
    // "1005_Recalibrate.  Seek to cylinder 0, without assuming the
    // current position of the heads is correct.  This is used to correct
    // a seek error, and as part of error recovery.  Recalibrate resets
    // some error conditions in the drive, and causes an attention when complete."
 
    set_status_active();
    struct disk_unit_s* p = SELECTED_UNIT_PTR();
    disk_unit_seek(p, 0, 0, 0);
    p->has_fault = false;
    p->seek_error = false;
    disk_unit_raise_attention(p);
    set_status_not_active();
}

static void
start_fault_clear()
{
    // "0405_Fault clear.  Resets most error conditions in the drive."
    
    set_status_active();
    struct disk_unit_s* p = SELECTED_UNIT_PTR();
    p->has_fault = false;
    set_status_not_active();
}

static void
start_at_ease()
{
    // "0005_At ease.  Resets attention on the selected unit."

    set_status_active();
    struct disk_unit_s* p = SELECTED_UNIT_PTR();
    p->attention = false;
    set_status_not_active();
}

static void
start_offset_clear()
{
    // "0006_Offset clear.  Take the heads out of the offset state.
    // This does not wait for completion, but the next command will." 
    
    set_status_active();
    // there is no concept of servo offset in usim
    // offset_clear is simply a nop
    set_status_not_active();
}

static void
start(void)
{
    idle_disk_activity();

    struct disk_unit_s* p = SELECTED_UNIT_PTR();

    if (!p->online)
    {
        NOTICE(TRACE_DISK, "disk controller 1: start, but disk unit %d not online\n", p->unit);
    }
    else
    {
        switch (cmd & 017) 
        {
            // 0 000
            case 000:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) read\n", cmd);
                start_read();
                break;

            // 1 000
            case 010:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) read compare\n", cmd);
                start_read_compare();
                break;

            // 1 001
            case 011:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) write\n", cmd);
                start_write();
                break;

            // 0 010
            case 002:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) read all\n", cmd);
                errx(1, "read all not implemented");
                break;

            // 1 011
            case 013:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) write all\n", cmd);
                errx(1, "write all not implemented");
                break;

            // 0 100
            case 004:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) seek\n", cmd);
                start_seek();
                break;

            // 0 101
            case 005:
                {
                    // "0005_At ease.  Resets attention on the selected unit."
                    DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) at ease\n", cmd);
                    start_at_ease();

                    // "1405_This probably does both a Recalibrate and a Fault Clear."

                    // "<9>     Recalibrate.  In combination with command 5, causes the
                    // disk to return the heads to cylinder 0."
                    if ((cmd & 01000) != 0)
                    {
                        DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) recalibrate\n", cmd);
                        start_recalibrate();
                    }

                    //  0405_Fault clear.  Resets most error conditions in the drive.
                    if ((cmd & 00400) != 0)
                    {
                        DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) fault clear\n", cmd);
                        start_fault_clear();
                    }
                }
                break;

            // 0 110
            case 006:
                DEBUG(TRACE_DISK, "disk-controller: start, cmd (%04o) offset clear\n", cmd);
                start_offset_clear();
                break;

            default:
                errx(1, "disk-controller: start, cmd (%o) unknown", cmd);
        }
    }
}

// this is not used at the moment but if a disk unit is for example changes
// state while idle, this can be used
void
disk_controller_get_attention(__attribute__((unused)) struct disk_unit_s *p)
{
    if (status.not_active && attention_interrupt_enable)
    {
        assert_interrupt();
    }
}

#ifdef WITH_NONBLOCKING_DISKIO
static void*
disk_controller_thread_run(__attribute__((unused)) void* arg)
{
    thread_run = true;

    DEBUG(TRACE_USIM, "disk-controller: thread starting...\n");

	while (thread_run)
	{
        do_xfer();
        for (size_t i  = 0; i < NUMBER_OF_DISK_UNITS; i++) 
        {
            if (disk_units[i].online) disk_unit_rotate(disk_units + i);
        }
        sched_yield();
	}

	DEBUG(TRACE_USIM, "disk-controller: thread quitting...\n");

	return NULL;
}
#endif

void
disk_controller_init()
{
    reset();

    bool at_least_one_disk_unit_attached = false;

    for (size_t i = 0; i < NUMBER_OF_DISK_UNITS; i++)
    {
        struct disk_unit_s* p = disk_units + i;
        if (p->configured) at_least_one_disk_unit_attached = true;
    }

    if (at_least_one_disk_unit_attached)
    {
        NOTICE(TRACE_USIM, "disk-controller: attached to disk unit(s):");
        for (size_t i = 0; i < NUMBER_OF_DISK_UNITS; i++)
        {
            struct disk_unit_s* p = disk_units + i;
            p->disk_controller_get_attention = &disk_controller_get_attention;
            if (p->configured) NOTICE(TRACE_USIM, " %d", i);
        }
        NOTICE(TRACE_USIM, "\n");
    }
    else
    {
        NOTICE(TRACE_USIM, "disk-controller: no disks attached\n");
    }

#ifdef WITH_NONBLOCKING_DISKIO
    INFO(TRACE_USIM, "disk-controller: running in non-blocking mode\n");

    pthread_attr_t attr;
    pthread_attr_init(&attr);
    pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);

    if (pthread_create(&thread, &attr, disk_controller_thread_run, NULL) != 0)
    {
        errx(1, "cannot create disk controller thread");
    }
#else
    INFO(TRACE_USIM, "disk-controller: running in blocking mode \n");
#endif
}

void
disk_controller_quit(void)
{
    for (size_t i = 0; i < NUMBER_OF_DISK_UNITS; i++)
    {
        disk_unit_quit(disk_units + i);
    }

#ifdef WITH_NONBLOCKING_DISKIO
    thread_run = false;
#endif
}

void
disk_controller_read(uint32_t offset, uint32_t *pv)
{
    if (reset_condition)
    {
        *pv = 0;
        return;
    }
	switch (offset) 
	{
		case 0:
            {
                *pv = encode_status();
                DEBUG(TRACE_DISK, "disk-controller: read status: %011o\n", *pv);
            }
			break;

		case 1:
            {
                struct disk_unit_s *p = SELECTED_UNIT_PTR();
                *pv = p->last_memory_address;
                DEBUG(TRACE_DISK, "disk-controller: read ma: %011o\n", *pv);
            }
            break;

		case 2:
            {
                *pv = da;
                DEBUG(TRACE_DISK, "disk-controller: read da: %011o\n", *pv);
            }
            break;

		case 3:
            {
                // no ECC/parity error happens in usim
                *pv = 0;
                DEBUG(TRACE_DISK, "disk-controller: read ecc: %011o\n", *pv);
            }
			break;

		default:
			errx(1, "disk-controller: unknown read %o\n", offset);
	}
}

void
disk_controller_write(uint32_t offset, uint32_t v)
{
	switch (offset) 
	{
		case 0:
            {
                // " 0016    Reset.  This stops the current transfer and resets the controller.
                // This command takes effect as soon as it is stored in the command
                // register; no store in START is required.  After storing a Reset
                // command you should store 0 in the command register to turn off
                // the reset condition.  Use of Reset while a transfer is in progress
                // isn't guaranteed not to do strange things."
                if (v == 0)
                {
                    cmd = 0;
                    reset_condition = false;
                    break;
                }
                else if (v == 016)
                {
                    reset();
                    reset_condition = true;
                    break;
                }
                if (reset_condition) break;
                cmd = v;
                done_interrupt_enable = ((v & 04000) != 0);
                attention_interrupt_enable = ((v & 02000) != 0);
                if (!done_interrupt_enable && !attention_interrupt_enable)
                {
                    deassert_interrupt();
                }
                DEBUG(TRACE_DISK, "disk-controller: write cmd: %04o\n", cmd);
            }
            break;

		case 1:
            {
                if (reset_condition) break;
                clp = v;
                DEBUG(TRACE_DISK, "disk-controller: write clp: %011o\n", clp);
            }
			break;

		case 2:
            {
                if (reset_condition) break;
                // "Storing into the Disk Address register momentarily 
                // deselects the current unit ..."
                da = v;
                DEBUG(TRACE_DISK, "disk-controller: write da:%011o\n", da);
            }
            break;

		case 3:
            {
                if (reset_condition) break;
                start();
            }
            break;

		default:
			errx(1, "disk-controller: unknown write %o\n", offset);
	}
}

void 
disk_controller_bus_reset(void)
{
}
