#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <poll.h>
#include <time.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "wlr-layer-shell-unstable-v1-client-protocol.h"

/* Map one solid layer-shell panel on an edge with an exclusive zone, so a
 * test can see how the compositor lays windows out around a dock or a bar.
 * --blink N unmaps the panel with a null-buffer commit every N seconds and
 * maps it again with a commit without a buffer, on the same layer surface. */

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct zwlr_layer_shell_v1 *layer_shell;
static struct wl_surface *surface;
static struct wl_seat *seat;
static int running = 1;
static uint32_t colour = 0xFF202020;
static int mapped;

static struct wl_buffer *make_buffer(int w, int h)
{
    int stride = w * 4;
    int size = stride * h;
    int fd = memfd_create("panel", 0);
    if (fd < 0 || ftruncate(fd, size) < 0) return NULL;
    uint32_t *data = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (data == MAP_FAILED) { close(fd); return NULL; }
    for (int i = 0; i < w * h; i++) data[i] = colour;
    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buf = wl_shm_pool_create_buffer(pool, 0, w, h, stride, WL_SHM_FORMAT_XRGB8888);
    wl_shm_pool_destroy(pool);
    munmap(data, size);
    close(fd);
    return buf;
}

static void layer_configure(void *data, struct zwlr_layer_surface_v1 *layer, uint32_t serial, uint32_t w, uint32_t h)
{
    (void)data;
    zwlr_layer_surface_v1_ack_configure(layer, serial);
    struct wl_buffer *buf = make_buffer((int)w, (int)h);
    if (!buf) return;
    wl_surface_attach(surface, buf, 0, 0);
    wl_surface_damage_buffer(surface, 0, 0, (int)w, (int)h);
    wl_surface_commit(surface);
    mapped = 1;
    printf("PANEL %ux%u\n", w, h);
    fflush(stdout);
}

static void layer_closed(void *data, struct zwlr_layer_surface_v1 *layer)
{
    (void)data; (void)layer;
    running = 0;
}

static const struct zwlr_layer_surface_v1_listener layer_listener = { layer_configure, layer_closed };

static void kb_keymap(void *d, struct wl_keyboard *k, uint32_t format, int32_t fd, uint32_t size)
{
    (void)d; (void)k; (void)format; (void)size;
    close(fd);
}

static void kb_enter(void *d, struct wl_keyboard *k, uint32_t serial, struct wl_surface *s, struct wl_array *keys)
{
    (void)d; (void)k; (void)serial; (void)s; (void)keys;
    printf("KEYBOARD enter\n");
    fflush(stdout);
}

static void kb_leave(void *d, struct wl_keyboard *k, uint32_t serial, struct wl_surface *s)
{
    (void)d; (void)k; (void)serial; (void)s;
    printf("KEYBOARD leave\n");
    fflush(stdout);
}

static void kb_key(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t time, uint32_t key, uint32_t state)
{
    (void)d; (void)k; (void)serial; (void)time;
    printf("KEY %u %u\n", key, state);
    fflush(stdout);
}

static void kb_modifiers(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t dep, uint32_t lat, uint32_t lock, uint32_t group)
{
    (void)d; (void)k; (void)serial; (void)dep; (void)lat; (void)lock; (void)group;
}

static void kb_repeat(void *d, struct wl_keyboard *k, int32_t rate, int32_t delay)
{
    (void)d; (void)k; (void)rate; (void)delay;
}

static const struct wl_keyboard_listener keyboard_listener = { kb_keymap, kb_enter, kb_leave, kb_key, kb_modifiers, kb_repeat };

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data; (void)version;
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(registry, name, &wl_compositor_interface, 4);
    else if (strcmp(interface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(registry, name, &wl_shm_interface, 1);
    else if (strcmp(interface, zwlr_layer_shell_v1_interface.name) == 0)
        layer_shell = wl_registry_bind(registry, name, &zwlr_layer_shell_v1_interface, 1);
    else if (strcmp(interface, wl_seat_interface.name) == 0 && !seat)
        seat = wl_registry_bind(registry, name, &wl_seat_interface, 4);
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

int main(int argc, char **argv)
{
    if (argc < 3) {
        fprintf(stderr, "usage: panel top|bottom|left|right SIZE [keyboard] [--blink SECONDS]\n");
        return 2;
    }

    uint32_t edge;
    uint32_t across;
    int horizontal;
    if (strcmp(argv[1], "top") == 0) { edge = ZWLR_LAYER_SURFACE_V1_ANCHOR_TOP; horizontal = 1; }
    else if (strcmp(argv[1], "bottom") == 0) { edge = ZWLR_LAYER_SURFACE_V1_ANCHOR_BOTTOM; horizontal = 1; }
    else if (strcmp(argv[1], "left") == 0) { edge = ZWLR_LAYER_SURFACE_V1_ANCHOR_LEFT; horizontal = 0; }
    else if (strcmp(argv[1], "right") == 0) { edge = ZWLR_LAYER_SURFACE_V1_ANCHOR_RIGHT; horizontal = 0; }
    else { fprintf(stderr, "panel: %s is not top|bottom|left|right\n", argv[1]); return 2; }
    across = horizontal
        ? ZWLR_LAYER_SURFACE_V1_ANCHOR_LEFT | ZWLR_LAYER_SURFACE_V1_ANCHOR_RIGHT
        : ZWLR_LAYER_SURFACE_V1_ANCHOR_TOP | ZWLR_LAYER_SURFACE_V1_ANCHOR_BOTTOM;
    int size = atoi(argv[2]);
    if (size <= 0) { fprintf(stderr, "panel: SIZE must be positive\n"); return 2; }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !layer_shell) { fprintf(stderr, "missing globals\n"); return 1; }

    surface = wl_compositor_create_surface(compositor);
    struct zwlr_layer_surface_v1 *layer = zwlr_layer_shell_v1_get_layer_surface(
        layer_shell, surface, NULL, ZWLR_LAYER_SHELL_V1_LAYER_TOP, "panel");
    zwlr_layer_surface_v1_add_listener(layer, &layer_listener, NULL);
    zwlr_layer_surface_v1_set_anchor(layer, edge | across);
    zwlr_layer_surface_v1_set_size(layer, horizontal ? 0 : (uint32_t)size, horizontal ? (uint32_t)size : 0);
    zwlr_layer_surface_v1_set_exclusive_zone(layer, size);
    int keyboard_wanted = 0;
    int blink = 0;
    for (int i = 3; i < argc; i++) {
        if (strcmp(argv[i], "keyboard") == 0) keyboard_wanted = 1;
        else if (strcmp(argv[i], "--blink") == 0 && i + 1 < argc) blink = atoi(argv[++i]);
        else { fprintf(stderr, "panel: unknown argument %s\n", argv[i]); return 2; }
    }
    if (keyboard_wanted) {
        zwlr_layer_surface_v1_set_keyboard_interactivity(layer, 1);
        if (seat) {
            struct wl_keyboard *keyboard = wl_seat_get_keyboard(seat);
            wl_keyboard_add_listener(keyboard, &keyboard_listener, NULL);
        }
    }
    wl_surface_commit(surface);

    struct timespec next;
    clock_gettime(CLOCK_MONOTONIC, &next);
    next.tv_sec += blink;
    struct pollfd pfd = { .fd = wl_display_get_fd(display), .events = POLLIN };
    while (running) {
        while (wl_display_prepare_read(display) != 0)
            wl_display_dispatch_pending(display);
        wl_display_flush(display);
        int timeout = -1;
        if (blink > 0) {
            struct timespec now;
            clock_gettime(CLOCK_MONOTONIC, &now);
            long ms = (next.tv_sec - now.tv_sec) * 1000 + (next.tv_nsec - now.tv_nsec) / 1000000;
            timeout = ms < 0 ? 0 : (int)ms;
        }
        int ready = poll(&pfd, 1, timeout);
        if (ready > 0) {
            if (wl_display_read_events(display) < 0) break;
            if (wl_display_dispatch_pending(display) < 0) break;
        } else {
            wl_display_cancel_read(display);
            if (ready < 0) break;
        }
        if (blink > 0) {
            struct timespec now;
            clock_gettime(CLOCK_MONOTONIC, &now);
            if (now.tv_sec > next.tv_sec || (now.tv_sec == next.tv_sec && now.tv_nsec >= next.tv_nsec)) {
                next.tv_sec += blink;
                if (mapped) {
                    wl_surface_attach(surface, NULL, 0, 0);
                    wl_surface_commit(surface);
                    mapped = 0;
                    printf("PANEL hidden\n");
                } else {
                    wl_surface_commit(surface);
                    printf("PANEL remap\n");
                }
                fflush(stdout);
            }
        }
    }

    return 0;
}
