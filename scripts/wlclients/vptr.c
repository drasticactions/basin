#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <wayland-client.h>
#include "wlr-virtual-pointer-unstable-v1-client-protocol.h"

static struct zwlr_virtual_pointer_manager_v1 *manager;
static struct wl_seat *seat;

static void registry_global(void *d, struct wl_registry *r, uint32_t name,
                            const char *iface, uint32_t version)
{
    (void)d; (void)version;
    if (!strcmp(iface, zwlr_virtual_pointer_manager_v1_interface.name))
        manager = wl_registry_bind(r, name, &zwlr_virtual_pointer_manager_v1_interface, 1);
    else if (!strcmp(iface, wl_seat_interface.name) && !seat)
        seat = wl_registry_bind(r, name, &wl_seat_interface, 1);
}

static void registry_remove(void *d, struct wl_registry *r, uint32_t name)
{ (void)d; (void)r; (void)name; }

static const struct wl_registry_listener registry_listener = {
    .global = registry_global, .global_remove = registry_remove,
};

int main(int argc, char **argv)
{
    int drag = argc > 1 && strcmp(argv[1], "--drag") == 0;
    int scroll = argc > 1 && strcmp(argv[1], "--scroll") == 0;
    int rel = argc > 1 && strcmp(argv[1], "--rel") == 0;
    if ((drag && argc < 8) || (scroll && argc < 7) || (rel && argc < 9) || (!drag && !scroll && !rel && argc < 5)) {
        fprintf(stderr, "usage: vptr X Y WIDTH HEIGHT [CLICKS] [BUTTON]\n"
                        "       vptr --drag X1 Y1 X2 Y2 WIDTH HEIGHT [BUTTON]\n"
                        "       vptr --scroll X Y WIDTH HEIGHT STEPS [horizontal]\n"
                        "       vptr --rel X Y WIDTH HEIGHT DX DY STEPS\n");
        return 1;
    }
    uint32_t x, y, x2 = 0, y2 = 0, w, h, btn = 0x110;
    int clicks = 1, steps = 0;
    uint32_t axis = WL_POINTER_AXIS_VERTICAL_SCROLL;
    double rdx = 0, rdy = 0;
    if (rel) {
        /* absolute move to X Y, then STEPS relative motions of DX DY, the
         * request a locked pointer still turns into relative_motion */
        x = (uint32_t)atoi(argv[2]); y = (uint32_t)atoi(argv[3]);
        w = (uint32_t)atoi(argv[4]); h = (uint32_t)atoi(argv[5]);
        rdx = atof(argv[6]); rdy = atof(argv[7]);
        steps = atoi(argv[8]);
    } else if (scroll) {
        x = (uint32_t)atoi(argv[2]); y = (uint32_t)atoi(argv[3]);
        w = (uint32_t)atoi(argv[4]); h = (uint32_t)atoi(argv[5]);
        steps = atoi(argv[6]);
        if (argc > 7 && strcmp(argv[7], "horizontal") == 0)
            axis = WL_POINTER_AXIS_HORIZONTAL_SCROLL;
    } else if (drag) {
        x = (uint32_t)atoi(argv[2]); y = (uint32_t)atoi(argv[3]);
        x2 = (uint32_t)atoi(argv[4]); y2 = (uint32_t)atoi(argv[5]);
        w = (uint32_t)atoi(argv[6]); h = (uint32_t)atoi(argv[7]);
        btn = argc > 8 ? (uint32_t)strtoul(argv[8], NULL, 0) : 0x110;
    } else {
        x = (uint32_t)atoi(argv[1]); y = (uint32_t)atoi(argv[2]);
        w = (uint32_t)atoi(argv[3]); h = (uint32_t)atoi(argv[4]);
        clicks = argc > 5 ? atoi(argv[5]) : 1;
        btn = argc > 6 ? (uint32_t)strtoul(argv[6], NULL, 0) : 0x110;
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "vptr: no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!manager) { fprintf(stderr, "vptr: no virtual pointer manager\n"); return 1; }

    struct zwlr_virtual_pointer_v1 *p =
        zwlr_virtual_pointer_manager_v1_create_virtual_pointer(manager, seat);

    uint32_t t = 1;
    zwlr_virtual_pointer_v1_motion_absolute(p, t, x, y, w, h);
    zwlr_virtual_pointer_v1_frame(p);
    wl_display_roundtrip(display);
    usleep(200000);

    if (rel) {
        for (int i = 0; i < steps; i++) {
            zwlr_virtual_pointer_v1_motion(p, t += 10, wl_fixed_from_double(rdx), wl_fixed_from_double(rdy));
            zwlr_virtual_pointer_v1_frame(p);
            wl_display_roundtrip(display);
            usleep(20000);
        }
        fprintf(stderr, "vptr: moved %d step(s) of %.1f,%.1f from %u,%u\n", steps, rdx, rdy, x, y);
    } else if (scroll) {
        int count = steps < 0 ? -steps : steps;
        double value = steps < 0 ? -10.0 : 10.0;
        for (int i = 0; i < count; i++) {
            zwlr_virtual_pointer_v1_axis_source(p, WL_POINTER_AXIS_SOURCE_WHEEL);
            zwlr_virtual_pointer_v1_axis(p, t += 10, axis, wl_fixed_from_double(value));
            zwlr_virtual_pointer_v1_frame(p);
            wl_display_roundtrip(display);
            usleep(30000);
        }
        fprintf(stderr, "vptr: scrolled %d step(s) at %u,%u\n", steps, x, y);
    } else if (drag) {
        zwlr_virtual_pointer_v1_button(p, t += 10, btn, WL_POINTER_BUTTON_STATE_PRESSED);
        zwlr_virtual_pointer_v1_frame(p);
        wl_display_roundtrip(display);
        usleep(100000);
        const int steps = 16;
        for (int i = 1; i <= steps; i++) {
            uint32_t mx = x + (uint32_t)(((int64_t)x2 - (int64_t)x) * i / steps);
            uint32_t my = y + (uint32_t)(((int64_t)y2 - (int64_t)y) * i / steps);
            zwlr_virtual_pointer_v1_motion_absolute(p, t += 10, mx, my, w, h);
            zwlr_virtual_pointer_v1_frame(p);
            wl_display_roundtrip(display);
            usleep(30000);
        }
        usleep(100000);
        zwlr_virtual_pointer_v1_button(p, t += 10, btn, WL_POINTER_BUTTON_STATE_RELEASED);
        zwlr_virtual_pointer_v1_frame(p);
        wl_display_roundtrip(display);
        fprintf(stderr, "vptr: dragged from %u,%u to %u,%u\n", x, y, x2, y2);
    } else {
        for (int i = 0; i < clicks; i++) {
            zwlr_virtual_pointer_v1_button(p, t += 10, btn, WL_POINTER_BUTTON_STATE_PRESSED);
            zwlr_virtual_pointer_v1_frame(p);
            wl_display_roundtrip(display);
            usleep(30000);
            zwlr_virtual_pointer_v1_button(p, t += 10, btn, WL_POINTER_BUTTON_STATE_RELEASED);
            zwlr_virtual_pointer_v1_frame(p);
            wl_display_roundtrip(display);
            usleep(60000);
        }

        fprintf(stderr, "vptr: clicked %d time(s) at %u,%u\n", clicks, x, y);
    }
    zwlr_virtual_pointer_v1_destroy(p);
    wl_display_roundtrip(display);
    wl_display_disconnect(display);
    return 0;
}
