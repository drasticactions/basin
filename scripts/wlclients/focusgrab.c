#define _GNU_SOURCE
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "hyprland-focus-grab-v1-client-protocol.h"

#define WIDTH 200
#define HEIGHT 150

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct wl_seat *seat;
static struct hyprland_focus_grab_manager_v1 *manager;
static int running = 1;
static int configured;

static void wm_base_ping(void *d, struct xdg_wm_base *b, uint32_t serial)
{
    (void)d;
    xdg_wm_base_pong(b, serial);
}

static const struct xdg_wm_base_listener wm_base_listener = { .ping = wm_base_ping };

static void surface_configure(void *d, struct xdg_surface *s, uint32_t serial)
{
    (void)d;
    xdg_surface_ack_configure(s, serial);
    configured = 1;
}

static const struct xdg_surface_listener surface_listener = { .configure = surface_configure };

static void toplevel_configure(void *d, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *states)
{ (void)d; (void)t; (void)w; (void)h; (void)states; }

static void toplevel_close(void *d, struct xdg_toplevel *t)
{
    (void)d; (void)t;
    running = 0;
}

static const struct xdg_toplevel_listener toplevel_listener = {
    .configure = toplevel_configure, .close = toplevel_close,
};

static void keyboard_keymap(void *d, struct wl_keyboard *k, uint32_t f, int32_t fd, uint32_t s)
{ (void)d; (void)k; (void)f; (void)s; close(fd); }

static void keyboard_enter(void *d, struct wl_keyboard *k, uint32_t serial, struct wl_surface *s, struct wl_array *keys)
{
    (void)d; (void)k; (void)serial; (void)s; (void)keys;
    printf("focusgrab: keyboard enter\n");
    fflush(stdout);
}

static void keyboard_leave(void *d, struct wl_keyboard *k, uint32_t serial, struct wl_surface *s)
{
    (void)d; (void)k; (void)serial; (void)s;
    printf("focusgrab: keyboard leave\n");
    fflush(stdout);
}

static void keyboard_key(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t t, uint32_t key, uint32_t state)
{
    (void)d; (void)k; (void)serial; (void)t;
    printf("focusgrab: key %u state %u\n", key, state);
    fflush(stdout);
}

static void keyboard_modifiers(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t a, uint32_t b, uint32_t c, uint32_t g)
{ (void)d; (void)k; (void)serial; (void)a; (void)b; (void)c; (void)g; }

static const struct wl_keyboard_listener keyboard_listener = {
    .keymap = keyboard_keymap, .enter = keyboard_enter, .leave = keyboard_leave,
    .key = keyboard_key, .modifiers = keyboard_modifiers,
};

static void pointer_enter(void *d, struct wl_pointer *p, uint32_t serial, struct wl_surface *s, wl_fixed_t x, wl_fixed_t y)
{ (void)d; (void)p; (void)serial; (void)s; (void)x; (void)y; }

static void pointer_leave(void *d, struct wl_pointer *p, uint32_t serial, struct wl_surface *s)
{ (void)d; (void)p; (void)serial; (void)s; }

static void pointer_motion(void *d, struct wl_pointer *p, uint32_t t, wl_fixed_t x, wl_fixed_t y)
{ (void)d; (void)p; (void)t; (void)x; (void)y; }

static void pointer_button(void *d, struct wl_pointer *p, uint32_t serial, uint32_t t, uint32_t b, uint32_t state)
{
    (void)d; (void)p; (void)serial; (void)t;
    printf("focusgrab: button %u state %u\n", b, state);
    fflush(stdout);
}

static void pointer_axis(void *d, struct wl_pointer *p, uint32_t t, uint32_t a, wl_fixed_t v)
{ (void)d; (void)p; (void)t; (void)a; (void)v; }

static const struct wl_pointer_listener pointer_listener = {
    .enter = pointer_enter, .leave = pointer_leave, .motion = pointer_motion,
    .button = pointer_button, .axis = pointer_axis,
};

static void grab_cleared(void *d, struct hyprland_focus_grab_v1 *g)
{
    (void)d; (void)g;
    printf("focusgrab: cleared\n");
    fflush(stdout);
}

static const struct hyprland_focus_grab_v1_listener grab_listener = { .cleared = grab_cleared };

static void registry_global(void *d, struct wl_registry *r, uint32_t name, const char *iface, uint32_t version)
{
    (void)d; (void)version;
    if (!strcmp(iface, wl_compositor_interface.name)) {
        compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    } else if (!strcmp(iface, wl_shm_interface.name)) {
        shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
    } else if (!strcmp(iface, xdg_wm_base_interface.name)) {
        wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, 1);
        xdg_wm_base_add_listener(wm_base, &wm_base_listener, NULL);
    } else if (!strcmp(iface, wl_seat_interface.name) && !seat) {
        seat = wl_registry_bind(r, name, &wl_seat_interface, 1);
    } else if (!strcmp(iface, hyprland_focus_grab_manager_v1_interface.name)) {
        manager = wl_registry_bind(r, name, &hyprland_focus_grab_manager_v1_interface, 1);
    }
}

static void registry_remove(void *d, struct wl_registry *r, uint32_t name)
{ (void)d; (void)r; (void)name; }

static const struct wl_registry_listener registry_listener = {
    .global = registry_global, .global_remove = registry_remove,
};

static struct wl_buffer *make_buffer(void)
{
    int stride = WIDTH * 4;
    int size = stride * HEIGHT;
    int fd = memfd_create("focusgrab", MFD_CLOEXEC);
    if (fd < 0 || ftruncate(fd, size) < 0) {
        return NULL;
    }

    uint32_t *pixels = mmap(NULL, (size_t)size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (pixels == MAP_FAILED) {
        close(fd);
        return NULL;
    }

    for (int i = 0; i < WIDTH * HEIGHT; i++) {
        pixels[i] = 0xff2050dd;
    }

    munmap(pixels, (size_t)size);
    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buffer = wl_shm_pool_create_buffer(pool, 0, WIDTH, HEIGHT, stride, WL_SHM_FORMAT_ARGB8888);
    wl_shm_pool_destroy(pool);
    close(fd);
    return buffer;
}

int main(int argc, char **argv)
{
    long seconds = argc > 1 ? atol(argv[1]) : 0;

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "focusgrab: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base || !seat) {
        fprintf(stderr, "focusgrab: the compositor is missing wl_compositor, wl_shm, xdg_wm_base or wl_seat\n");
        return 1;
    }

    if (!manager) {
        fprintf(stderr, "focusgrab: no hyprland_focus_grab_manager_v1\n");
        return 1;
    }

    struct wl_surface *surface = wl_compositor_create_surface(compositor);
    struct xdg_surface *xdg = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xdg, &surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xdg);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "focusgrab");
    xdg_toplevel_set_app_id(toplevel, "basin.focusgrab");
    wl_surface_commit(surface);
    while (!configured && wl_display_dispatch(display) != -1) {
    }

    struct wl_buffer *buffer = make_buffer();
    if (!buffer) {
        fprintf(stderr, "focusgrab: cannot make a buffer\n");
        return 1;
    }

    wl_surface_attach(surface, buffer, 0, 0);
    wl_surface_damage(surface, 0, 0, WIDTH, HEIGHT);
    wl_surface_commit(surface);
    wl_display_roundtrip(display);

    struct wl_keyboard *keyboard = wl_seat_get_keyboard(seat);
    wl_keyboard_add_listener(keyboard, &keyboard_listener, NULL);
    struct wl_pointer *pointer = wl_seat_get_pointer(seat);
    wl_pointer_add_listener(pointer, &pointer_listener, NULL);

    struct hyprland_focus_grab_v1 *grab = hyprland_focus_grab_manager_v1_create_grab(manager);
    hyprland_focus_grab_v1_add_listener(grab, &grab_listener, NULL);
    hyprland_focus_grab_v1_add_surface(grab, surface);
    hyprland_focus_grab_v1_commit(grab);
    wl_display_roundtrip(display);
    printf("focusgrab: grab committed\n");
    fflush(stdout);

    time_t deadline = seconds > 0 ? time(NULL) + seconds : 0;
    while (running && wl_display_dispatch(display) != -1) {
        if (deadline && time(NULL) >= deadline) {
            break;
        }
    }

    hyprland_focus_grab_v1_destroy(grab);
    printf("focusgrab: done\n");
    return 0;
}
