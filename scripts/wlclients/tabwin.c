/* tabwin: a plain shm toplevel for tab-wm's placement and close rows.
 * --app-id ID, --title TEXT: set on the toplevel.
 * --child: after the first window maps, map a second one with xdg_toplevel.set_parent.
 * --refuse-close: ignore xdg_toplevel.close, so a polite close never finishes.
 * --spawn CMD: after the first window maps, run CMD with sh -c (its windows then trace by PID).
 * --fullscreen: ask for fullscreen before the first commit.
 * --ask-on-close: answer the first close with a child "Save changes?" window and stay open,
 *   the way an editor with an unsaved document does.
 * --move-on-press: answer each left press with xdg_toplevel.move, the way a client-side
 *   headerbar does, and print BUTTON PRESS or BUTTON RELEASE for each left button event.
 * Prints MAPPED <title> for each window that maps and CLOSE for each close request. */
#define _GNU_SOURCE
#include <poll.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"

struct window {
    struct wl_surface *surface;
    struct xdg_surface *xdg;
    struct xdg_toplevel *toplevel;
    const char *title;
    uint32_t colour;
    int width, height;
    int mapped;
};

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct window main_window = { .title = "tabwin", .colour = 0xFF3C7A9E, .width = 320, .height = 200 };
static struct window child_window = { .title = "tabwin child", .colour = 0xFF9E3C5A, .width = 200, .height = 140 };
static const char *app_id = "tabwin";
static const char *spawn_command;
static int want_child;
static int refuse_close;
static int fullscreen;
static int ask_on_close;
static int move_on_press;
static struct wl_seat *seat;
static struct wl_pointer *pointer;
static struct window dialog_window = { .title = "Save changes?", .colour = 0xFFE0C040, .width = 240, .height = 120 };
static int running = 1;

static struct wl_buffer *make_buffer(int w, int h, uint32_t colour)
{
    int stride = w * 4;
    int size = stride * h;
    int fd = memfd_create("tabwin", 0);
    if (fd < 0 || ftruncate(fd, size) < 0) return NULL;
    uint32_t *data = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (data == MAP_FAILED) { close(fd); return NULL; }
    for (int i = 0; i < w * h; i++) data[i] = colour;
    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buffer = wl_shm_pool_create_buffer(pool, 0, w, h, stride, WL_SHM_FORMAT_XRGB8888);
    wl_shm_pool_destroy(pool);
    munmap(data, size);
    close(fd);
    return buffer;
}

static void create_window(struct window *window, struct window *parent);

static void surface_configure(void *data, struct xdg_surface *xdg, uint32_t serial)
{
    struct window *window = data;
    xdg_surface_ack_configure(xdg, serial);
    struct wl_buffer *buffer = make_buffer(window->width, window->height, window->colour);
    if (!buffer) return;
    wl_surface_attach(window->surface, buffer, 0, 0);
    wl_surface_damage_buffer(window->surface, 0, 0, window->width, window->height);
    wl_surface_commit(window->surface);
    if (!window->mapped) {
        window->mapped = 1;
        printf("MAPPED %s\n", window->title);
        fflush(stdout);
        if (window == &main_window && want_child) create_window(&child_window, &main_window);
        if (window == &main_window && spawn_command && fork() == 0) {
            execl("/bin/sh", "sh", "-c", spawn_command, (char *)NULL);
            _exit(127);
        }
    }
}

static const struct xdg_surface_listener surface_listener = { surface_configure };

static void toplevel_configure(void *data, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *states)
{
    struct window *window = data;
    (void)t; (void)states;
    if (w > 0) window->width = w;
    if (h > 0) window->height = h;
}

static void toplevel_close(void *data, struct xdg_toplevel *t)
{
    (void)data; (void)t;
    printf("CLOSE\n");
    fflush(stdout);
    if (ask_on_close) {
        if (!dialog_window.surface) create_window(&dialog_window, &main_window);
        return;
    }
    if (!refuse_close) running = 0;
}

static void toplevel_bounds(void *data, struct xdg_toplevel *t, int32_t w, int32_t h) { (void)data; (void)t; (void)w; (void)h; }
static void toplevel_caps(void *data, struct xdg_toplevel *t, struct wl_array *caps) { (void)data; (void)t; (void)caps; }

static const struct xdg_toplevel_listener toplevel_listener = { toplevel_configure, toplevel_close, toplevel_bounds, toplevel_caps };

static void create_window(struct window *window, struct window *parent)
{
    window->surface = wl_compositor_create_surface(compositor);
    window->xdg = xdg_wm_base_get_xdg_surface(wm_base, window->surface);
    xdg_surface_add_listener(window->xdg, &surface_listener, window);
    window->toplevel = xdg_surface_get_toplevel(window->xdg);
    xdg_toplevel_add_listener(window->toplevel, &toplevel_listener, window);
    xdg_toplevel_set_title(window->toplevel, window->title);
    xdg_toplevel_set_app_id(window->toplevel, app_id);
    if (parent) xdg_toplevel_set_parent(window->toplevel, parent->toplevel);
    if (!parent && fullscreen) xdg_toplevel_set_fullscreen(window->toplevel, NULL);
    wl_surface_commit(window->surface);
}

static void pointer_enter(void *data, struct wl_pointer *p, uint32_t serial, struct wl_surface *s, wl_fixed_t x, wl_fixed_t y) { (void)data; (void)p; (void)serial; (void)s; (void)x; (void)y; }
static void pointer_leave(void *data, struct wl_pointer *p, uint32_t serial, struct wl_surface *s) { (void)data; (void)p; (void)serial; (void)s; }
static void pointer_motion(void *data, struct wl_pointer *p, uint32_t time, wl_fixed_t x, wl_fixed_t y) { (void)data; (void)p; (void)time; (void)x; (void)y; }

static void pointer_button(void *data, struct wl_pointer *p, uint32_t serial, uint32_t time, uint32_t button, uint32_t state)
{
    (void)data; (void)p; (void)time;
    if (button != 0x110) return;
    int pressed = state == WL_POINTER_BUTTON_STATE_PRESSED;
    printf("BUTTON %s\n", pressed ? "PRESS" : "RELEASE");
    fflush(stdout);
    if (pressed) xdg_toplevel_move(main_window.toplevel, seat, serial);
}

static void pointer_axis(void *data, struct wl_pointer *p, uint32_t time, uint32_t axis, wl_fixed_t value) { (void)data; (void)p; (void)time; (void)axis; (void)value; }
static const struct wl_pointer_listener pointer_listener = {
    .enter = pointer_enter, .leave = pointer_leave, .motion = pointer_motion, .button = pointer_button, .axis = pointer_axis,
};

static void ping(void *data, struct xdg_wm_base *base, uint32_t serial) { (void)data; xdg_wm_base_pong(base, serial); }
static const struct xdg_wm_base_listener wm_base_listener = { ping };

static void global(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data; (void)version;
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(registry, name, &wl_compositor_interface, 4);
    else if (strcmp(interface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(registry, name, &wl_shm_interface, 1);
    else if (strcmp(interface, xdg_wm_base_interface.name) == 0) {
        wm_base = wl_registry_bind(registry, name, &xdg_wm_base_interface, 1);
        xdg_wm_base_add_listener(wm_base, &wm_base_listener, NULL);
    } else if (strcmp(interface, wl_seat_interface.name) == 0 && move_on_press && !seat) {
        seat = wl_registry_bind(registry, name, &wl_seat_interface, 1);
        pointer = wl_seat_get_pointer(seat);
        wl_pointer_add_listener(pointer, &pointer_listener, NULL);
    }
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }
static const struct wl_registry_listener registry_listener = { global, global_remove };

int main(int argc, char **argv)
{
    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--child") == 0) want_child = 1;
        else if (strcmp(argv[i], "--refuse-close") == 0) refuse_close = 1;
        else if (strcmp(argv[i], "--fullscreen") == 0) fullscreen = 1;
        else if (strcmp(argv[i], "--ask-on-close") == 0) ask_on_close = 1;
        else if (strcmp(argv[i], "--move-on-press") == 0) move_on_press = 1;
        else if (strcmp(argv[i], "--app-id") == 0 && i + 1 < argc) app_id = argv[++i];
        else if (strcmp(argv[i], "--title") == 0 && i + 1 < argc) main_window.title = argv[++i];
        else if (strcmp(argv[i], "--spawn") == 0 && i + 1 < argc) spawn_command = argv[++i];
        else { fprintf(stderr, "usage: tabwin [--child] [--refuse-close] [--app-id ID] [--title TEXT] [--spawn CMD] [--fullscreen] [--ask-on-close] [--move-on-press]\n"); return 2; }
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "tabwin: no wayland display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base) { fprintf(stderr, "tabwin: missing globals\n"); return 1; }
    create_window(&main_window, NULL);
    while (running && wl_display_dispatch(display) != -1) {
    }

    wl_display_disconnect(display);
    return 0;
}
