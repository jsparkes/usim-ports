#pragma once

extern int mouse_x;
extern int mouse_y;
extern int mouse_rawx;
extern int mouse_rawy;

extern int mouse_head;
extern int mouse_middle;
extern int mouse_tail;

extern int is_mouse_warp;
extern int mouse_warp_x;
extern int mouse_warp_y;

extern void mouse_init(void);
extern void mouse_event(int, int, int);
extern void mouse_poll(void);
