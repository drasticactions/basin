/*
 * tabletprobe: a 256x256 window that prints every tablet-v2 tool event it
 * receives, one line each, so a test can read what a pen did to a client.
 *
 *   TOOL pen|eraser|...   TABLET name   IN tool probe|other   MOTION x y   PRESSURE p
 *   TILT tx ty   DOWN   UP   BUTTON code pressed|released   OUT tool
 *
 * Pressure is printed as the 0..65535 the protocol carries. The optional
 * argument is how many seconds to run.
 */
#define _GNU_SOURCE
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "tablet-v2-client-protocol.h"

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct wl_seat *seat;
static struct zwp_tablet_manager_v2 *tablets;
static struct wl_surface *surface;
static int running = 1;

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
    int fd = memfd_create("tabletprobe", 0);
    if (fd < 0 || ftruncate(fd, size) < 0) return NULL;
    uint32_t *data = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (data == MAP_FAILED) { close(fd); return NULL; }
    for (int i = 0; i < w * h; i++) data[i] = 0xFF5A3C9E;
    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buffer = wl_shm_pool_create_buffer(pool, 0, w, h, stride, WL_SHM_FORMAT_XRGB8888);
    wl_shm_pool_destroy(pool);
    munmap(data, size);
    close(fd);
    return buffer;
}

static const char *tool_name(uint32_t type)
{
    switch (type) {
    case ZWP_TABLET_TOOL_V2_TYPE_PEN: return "pen";
    case ZWP_TABLET_TOOL_V2_TYPE_ERASER: return "eraser";
    case ZWP_TABLET_TOOL_V2_TYPE_BRUSH: return "brush";
    case ZWP_TABLET_TOOL_V2_TYPE_PENCIL: return "pencil";
    case ZWP_TABLET_TOOL_V2_TYPE_AIRBRUSH: return "airbrush";
    case ZWP_TABLET_TOOL_V2_TYPE_FINGER: return "finger";
    case ZWP_TABLET_TOOL_V2_TYPE_MOUSE: return "mouse";
    case ZWP_TABLET_TOOL_V2_TYPE_LENS: return "lens";
    default: return "unknown";
    }
}

static const char *tool_names[8];
static struct zwp_tablet_tool_v2 *tool_ids[8];

static const char *name_of(struct zwp_tablet_tool_v2 *t)
{
    for (int i = 0; i < 8; i++)
        if (tool_ids[i] == t) return tool_names[i];
    return "unknown";
}

static void tool_type(void *d, struct zwp_tablet_tool_v2 *t, uint32_t type)
{
    (void)d;
    for (int i = 0; i < 8; i++)
        if (!tool_ids[i]) { tool_ids[i] = t; tool_names[i] = tool_name(type); break; }
    say("TOOL %s", tool_name(type));
}
static void tool_serial(void *d, struct zwp_tablet_tool_v2 *t, uint32_t hi, uint32_t lo) { (void)d; (void)t; (void)hi; (void)lo; }
static void tool_wacom(void *d, struct zwp_tablet_tool_v2 *t, uint32_t hi, uint32_t lo) { (void)d; (void)t; (void)hi; (void)lo; }
static void tool_capability(void *d, struct zwp_tablet_tool_v2 *t, uint32_t c) { (void)d; (void)t; (void)c; }
static void tool_done(void *d, struct zwp_tablet_tool_v2 *t) { (void)d; (void)t; }
static void tool_removed(void *d, struct zwp_tablet_tool_v2 *t)
{
    (void)d;
    for (int i = 0; i < 8; i++)
        if (tool_ids[i] == t) tool_ids[i] = NULL;
    zwp_tablet_tool_v2_destroy(t);
}
static void tool_in(void *d, struct zwp_tablet_tool_v2 *t, uint32_t serial, struct zwp_tablet_v2 *tablet, struct wl_surface *s)
{
    (void)d; (void)serial; (void)tablet;
    say("IN %s %s", name_of(t), s == surface ? "probe" : "other");
}
static void tool_out(void *d, struct zwp_tablet_tool_v2 *t) { (void)d; say("OUT %s", name_of(t)); }
static void tool_down(void *d, struct zwp_tablet_tool_v2 *t, uint32_t serial) { (void)d; (void)t; (void)serial; say("DOWN"); }
static void tool_up(void *d, struct zwp_tablet_tool_v2 *t) { (void)d; (void)t; say("UP"); }
static void tool_motion(void *d, struct zwp_tablet_tool_v2 *t, wl_fixed_t x, wl_fixed_t y)
{
    (void)d; (void)t;
    say("MOTION %.1f %.1f", wl_fixed_to_double(x), wl_fixed_to_double(y));
}
static void tool_pressure(void *d, struct zwp_tablet_tool_v2 *t, uint32_t p) { (void)d; (void)t; say("PRESSURE %u", p); }
static void tool_distance(void *d, struct zwp_tablet_tool_v2 *t, uint32_t v) { (void)d; (void)t; (void)v; }
static void tool_tilt(void *d, struct zwp_tablet_tool_v2 *t, wl_fixed_t x, wl_fixed_t y)
{
    (void)d; (void)t;
    say("TILT %.1f %.1f", wl_fixed_to_double(x), wl_fixed_to_double(y));
}
static void tool_rotation(void *d, struct zwp_tablet_tool_v2 *t, wl_fixed_t r) { (void)d; (void)t; (void)r; }
static void tool_slider(void *d, struct zwp_tablet_tool_v2 *t, int32_t s) { (void)d; (void)t; (void)s; }
static void tool_wheel(void *d, struct zwp_tablet_tool_v2 *t, wl_fixed_t w, int32_t c) { (void)d; (void)t; (void)w; (void)c; }
static void tool_button(void *d, struct zwp_tablet_tool_v2 *t, uint32_t serial, uint32_t button, uint32_t state)
{
    (void)d; (void)t; (void)serial;
    say("BUTTON 0x%x %s", button, state ? "pressed" : "released");
}
static void tool_frame(void *d, struct zwp_tablet_tool_v2 *t, uint32_t time) { (void)d; (void)t; (void)time; }
static const struct zwp_tablet_tool_v2_listener tool_listener = {
    .type = tool_type, .hardware_serial = tool_serial, .hardware_id_wacom = tool_wacom,
    .capability = tool_capability, .done = tool_done, .removed = tool_removed,
    .proximity_in = tool_in, .proximity_out = tool_out, .down = tool_down, .up = tool_up,
    .motion = tool_motion, .pressure = tool_pressure, .distance = tool_distance, .tilt = tool_tilt,
    .rotation = tool_rotation, .slider = tool_slider, .wheel = tool_wheel, .button = tool_button,
    .frame = tool_frame,
};

static void tablet_name(void *d, struct zwp_tablet_v2 *t, const char *name) { (void)d; (void)t; say("TABLET %s", name); }
static void tablet_id(void *d, struct zwp_tablet_v2 *t, uint32_t v, uint32_t p) { (void)d; (void)t; (void)v; (void)p; }
static void tablet_path(void *d, struct zwp_tablet_v2 *t, const char *path) { (void)d; (void)t; (void)path; }
static void tablet_done(void *d, struct zwp_tablet_v2 *t) { (void)d; (void)t; }
static void tablet_removed(void *d, struct zwp_tablet_v2 *t) { (void)d; zwp_tablet_v2_destroy(t); }
static const struct zwp_tablet_v2_listener tablet_listener = {
    .name = tablet_name, .id = tablet_id, .path = tablet_path, .done = tablet_done, .removed = tablet_removed,
};

static void seat_tablet(void *d, struct zwp_tablet_seat_v2 *s, struct zwp_tablet_v2 *t) { (void)d; (void)s; zwp_tablet_v2_add_listener(t, &tablet_listener, NULL); }
static void seat_tool(void *d, struct zwp_tablet_seat_v2 *s, struct zwp_tablet_tool_v2 *t) { (void)d; (void)s; zwp_tablet_tool_v2_add_listener(t, &tool_listener, NULL); }
static void seat_pad(void *d, struct zwp_tablet_seat_v2 *s, struct zwp_tablet_pad_v2 *p) { (void)d; (void)s; (void)p; }
static const struct zwp_tablet_seat_v2_listener tablet_seat_listener = {
    .tablet_added = seat_tablet, .tool_added = seat_tool, .pad_added = seat_pad,
};

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
    (void)d; (void)version;
    if (!strcmp(iface, wl_compositor_interface.name)) compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    else if (!strcmp(iface, wl_shm_interface.name)) shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
    else if (!strcmp(iface, xdg_wm_base_interface.name)) wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, 1);
    else if (!strcmp(iface, wl_seat_interface.name) && !seat) seat = wl_registry_bind(r, name, &wl_seat_interface, 1);
    else if (!strcmp(iface, zwp_tablet_manager_v2_interface.name)) tablets = wl_registry_bind(r, name, &zwp_tablet_manager_v2_interface, 1);
}
static void registry_remove(void *d, struct wl_registry *r, uint32_t name) { (void)d; (void)r; (void)name; }
static const struct wl_registry_listener registry_listener = { registry_global, registry_remove };

int main(int argc, char **argv)
{
    long seconds = argc > 1 ? atol(argv[1]) : 0;
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "tabletprobe: no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base || !seat) { fprintf(stderr, "tabletprobe: missing a core global\n"); return 1; }
    if (!tablets) { say("TABLETS missing"); return 1; }

    xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);
    struct zwp_tablet_seat_v2 *tablet_seat = zwp_tablet_manager_v2_get_tablet_seat(tablets, seat);
    zwp_tablet_seat_v2_add_listener(tablet_seat, &tablet_seat_listener, NULL);
    surface = wl_compositor_create_surface(compositor);
    struct xdg_surface *xs = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xs, &xdg_surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xs);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "tabletprobe");
    xdg_toplevel_set_app_id(toplevel, "tabletprobe");
    wl_surface_commit(surface);
    say("READY");

    time_t deadline = seconds > 0 ? time(NULL) + seconds : 0;
    while (running && wl_display_dispatch(display) != -1) {
        if (deadline && time(NULL) >= deadline) break;
    }
    return 0;
}
