/*
 * Map one 256x256 xdg toplevel and constrain the pointer on it: "lock" (the default)
 * asks zwp_pointer_constraints_v1 for a persistent lock, "confine" for a persistent
 * confinement to the square 64,64 128x128. It binds zwp_relative_pointer_manager_v1
 * too, and prints LOCKED, UNLOCKED, CONFINED, UNCONFINED, ENTER X Y, LEAVE,
 * MOTION X Y and RELATIVE DX DY as they arrive. SECONDS ends the run (0 runs until
 * the window is closed). "hidden" sets a null cursor on every enter, the way a game
 * hides its pointer.
 *
 *   pointerlock [lock|confine] [hidden] [SECONDS]
 */
#define _GNU_SOURCE
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "pointer-constraints-unstable-v1-client-protocol.h"
#include "relative-pointer-unstable-v1-client-protocol.h"

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct wl_seat *seat;
static struct wl_pointer *pointer;
static struct zwp_pointer_constraints_v1 *constraints;
static struct zwp_relative_pointer_manager_v1 *relative_manager;
static struct wl_surface *surface;
static int confine;
static int hidden;
static int running = 1;
static int constrained;

__attribute__((format(printf, 1, 2)))
static void say(const char *format, ...)
{
    va_list args;
    va_start(args, format);
    vprintf(format, args);
    va_end(args);
    putchar('\n');
    fflush(stdout);
}

static struct wl_buffer *make_buffer(int w, int h)
{
    int stride = w * 4;
    int size = stride * h;
    int fd = memfd_create("pointerlock", 0);
    if (fd < 0 || ftruncate(fd, size) < 0) return NULL;
    uint32_t *data = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (data == MAP_FAILED) { close(fd); return NULL; }
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            data[y * w + x] = (confine && x >= 64 && x < 192 && y >= 64 && y < 192) ? 0xFF9E3C3C : 0xFF3C9E5A;
    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buf = wl_shm_pool_create_buffer(pool, 0, w, h, stride, WL_SHM_FORMAT_XRGB8888);
    wl_shm_pool_destroy(pool);
    munmap(data, size);
    close(fd);
    return buf;
}

static void locked(void *d, struct zwp_locked_pointer_v1 *l) { (void)d; (void)l; say("LOCKED"); }
static void unlocked(void *d, struct zwp_locked_pointer_v1 *l) { (void)d; (void)l; say("UNLOCKED"); }
static const struct zwp_locked_pointer_v1_listener locked_listener = { locked, unlocked };

static void confined(void *d, struct zwp_confined_pointer_v1 *c) { (void)d; (void)c; say("CONFINED"); }
static void unconfined(void *d, struct zwp_confined_pointer_v1 *c) { (void)d; (void)c; say("UNCONFINED"); }
static const struct zwp_confined_pointer_v1_listener confined_listener = { confined, unconfined };

static void relative_motion(void *d, struct zwp_relative_pointer_v1 *r, uint32_t hi, uint32_t lo,
                            wl_fixed_t dx, wl_fixed_t dy, wl_fixed_t udx, wl_fixed_t udy)
{
    (void)d; (void)r; (void)hi; (void)lo; (void)udx; (void)udy;
    say("RELATIVE %.2f %.2f", wl_fixed_to_double(dx), wl_fixed_to_double(dy));
}
static const struct zwp_relative_pointer_v1_listener relative_listener = { relative_motion };

static void constrain(void)
{
    if (constrained || !constraints) return;
    constrained = 1;
    if (confine) {
        struct wl_region *region = wl_compositor_create_region(compositor);
        wl_region_add(region, 64, 64, 128, 128);
        struct zwp_confined_pointer_v1 *c = zwp_pointer_constraints_v1_confine_pointer(
            constraints, surface, pointer, region, ZWP_POINTER_CONSTRAINTS_V1_LIFETIME_PERSISTENT);
        wl_region_destroy(region);
        zwp_confined_pointer_v1_add_listener(c, &confined_listener, NULL);
    } else {
        struct zwp_locked_pointer_v1 *l = zwp_pointer_constraints_v1_lock_pointer(
            constraints, surface, pointer, NULL, ZWP_POINTER_CONSTRAINTS_V1_LIFETIME_PERSISTENT);
        zwp_locked_pointer_v1_add_listener(l, &locked_listener, NULL);
    }
}

static void pointer_enter(void *d, struct wl_pointer *p, uint32_t serial, struct wl_surface *s, wl_fixed_t x, wl_fixed_t y)
{
    (void)d; (void)p; (void)serial; (void)s;
    say("ENTER %.2f %.2f", wl_fixed_to_double(x), wl_fixed_to_double(y));
    if (hidden)
        wl_pointer_set_cursor(p, serial, NULL, 0, 0);
}
static void pointer_leave(void *d, struct wl_pointer *p, uint32_t serial, struct wl_surface *s) { (void)d; (void)p; (void)serial; (void)s; say("LEAVE"); }
static void pointer_motion(void *d, struct wl_pointer *p, uint32_t time, wl_fixed_t x, wl_fixed_t y)
{
    (void)d; (void)p; (void)time;
    say("MOTION %.2f %.2f", wl_fixed_to_double(x), wl_fixed_to_double(y));
}
static void pointer_button(void *d, struct wl_pointer *p, uint32_t serial, uint32_t time, uint32_t button, uint32_t state) { (void)d; (void)p; (void)serial; (void)time; (void)button; (void)state; }
static void pointer_axis(void *d, struct wl_pointer *p, uint32_t time, uint32_t axis, wl_fixed_t value) { (void)d; (void)p; (void)time; (void)axis; (void)value; }
static void pointer_frame(void *d, struct wl_pointer *p) { (void)d; (void)p; }
static void pointer_axis_source(void *d, struct wl_pointer *p, uint32_t source) { (void)d; (void)p; (void)source; }
static void pointer_axis_stop(void *d, struct wl_pointer *p, uint32_t time, uint32_t axis) { (void)d; (void)p; (void)time; (void)axis; }
static void pointer_axis_discrete(void *d, struct wl_pointer *p, uint32_t axis, int32_t discrete) { (void)d; (void)p; (void)axis; (void)discrete; }
static const struct wl_pointer_listener pointer_listener = {
    .enter = pointer_enter, .leave = pointer_leave, .motion = pointer_motion, .button = pointer_button,
    .axis = pointer_axis, .frame = pointer_frame, .axis_source = pointer_axis_source,
    .axis_stop = pointer_axis_stop, .axis_discrete = pointer_axis_discrete,
};

static void seat_capabilities(void *d, struct wl_seat *s, uint32_t caps)
{
    (void)d;
    if ((caps & WL_SEAT_CAPABILITY_POINTER) && !pointer) {
        pointer = wl_seat_get_pointer(s);
        wl_pointer_add_listener(pointer, &pointer_listener, NULL);
        if (relative_manager) {
            struct zwp_relative_pointer_v1 *r = zwp_relative_pointer_manager_v1_get_relative_pointer(relative_manager, pointer);
            zwp_relative_pointer_v1_add_listener(r, &relative_listener, NULL);
        }
    }
}
static void seat_name(void *d, struct wl_seat *s, const char *name) { (void)d; (void)s; (void)name; }
static const struct wl_seat_listener seat_listener = { seat_capabilities, seat_name };

static void wm_ping(void *d, struct xdg_wm_base *b, uint32_t serial) { (void)d; xdg_wm_base_pong(b, serial); }
static const struct xdg_wm_base_listener wm_listener = { wm_ping };

static void xdg_surface_configure(void *d, struct xdg_surface *s, uint32_t serial)
{
    (void)d;
    xdg_surface_ack_configure(s, serial);
    struct wl_buffer *buf = make_buffer(256, 256);
    if (!buf) return;
    wl_surface_attach(surface, buf, 0, 0);
    wl_surface_damage_buffer(surface, 0, 0, 256, 256);
    constrain();
    wl_surface_commit(surface);
}
static const struct xdg_surface_listener xdg_surface_listener = { xdg_surface_configure };

static void toplevel_configure(void *d, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *s) { (void)d; (void)t; (void)w; (void)h; (void)s; }
static void toplevel_close(void *d, struct xdg_toplevel *t) { (void)d; (void)t; running = 0; }
static void toplevel_bounds(void *d, struct xdg_toplevel *t, int32_t w, int32_t h) { (void)d; (void)t; (void)w; (void)h; }
static void toplevel_caps(void *d, struct xdg_toplevel *t, struct wl_array *c) { (void)d; (void)t; (void)c; }
static const struct xdg_toplevel_listener toplevel_listener = { toplevel_configure, toplevel_close, toplevel_bounds, toplevel_caps };

static void registry_global(void *d, struct wl_registry *r, uint32_t name, const char *iface, uint32_t version)
{
    (void)d;
    if (!strcmp(iface, wl_compositor_interface.name)) compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    else if (!strcmp(iface, wl_shm_interface.name)) shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
    else if (!strcmp(iface, xdg_wm_base_interface.name)) wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, version < 4 ? version : 4);
    else if (!strcmp(iface, wl_seat_interface.name) && !seat) seat = wl_registry_bind(r, name, &wl_seat_interface, version < 5 ? version : 5);
    else if (!strcmp(iface, zwp_pointer_constraints_v1_interface.name)) constraints = wl_registry_bind(r, name, &zwp_pointer_constraints_v1_interface, 1);
    else if (!strcmp(iface, zwp_relative_pointer_manager_v1_interface.name)) relative_manager = wl_registry_bind(r, name, &zwp_relative_pointer_manager_v1_interface, 1);
}
static void registry_remove(void *d, struct wl_registry *r, uint32_t name) { (void)d; (void)r; (void)name; }
static const struct wl_registry_listener registry_listener = { registry_global, registry_remove };

int main(int argc, char **argv)
{
    long seconds = 0;
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "confine")) confine = 1;
        else if (!strcmp(argv[i], "lock")) confine = 0;
        else if (!strcmp(argv[i], "hidden")) hidden = 1;
        else seconds = atol(argv[i]);
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "pointerlock: no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base || !seat) { fprintf(stderr, "pointerlock: missing a core global\n"); return 1; }
    if (!constraints) { fprintf(stderr, "pointerlock: no zwp_pointer_constraints_v1\n"); return 1; }

    xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);
    wl_seat_add_listener(seat, &seat_listener, NULL);
    wl_display_roundtrip(display);
    if (!pointer) { fprintf(stderr, "pointerlock: the seat has no pointer\n"); return 1; }

    surface = wl_compositor_create_surface(compositor);
    struct xdg_surface *xs = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xs, &xdg_surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xs);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "pointerlock");
    xdg_toplevel_set_app_id(toplevel, "pointerlock");
    wl_surface_commit(surface);
    say("READY %s", confine ? "confine" : "lock");

    time_t deadline = seconds > 0 ? time(NULL) + seconds : 0;
    while (running && wl_display_dispatch(display) != -1) {
        if (deadline && time(NULL) >= deadline) break;
    }
    return 0;
}
