#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "wlr-layer-shell-unstable-v1-client-protocol.h"

/* Map one solid layer-shell panel on an edge with an exclusive zone, so a
 * test can see how the compositor lays windows out around a dock or a bar. */

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct zwlr_layer_shell_v1 *layer_shell;
static struct wl_surface *surface;
static int running = 1;
static uint32_t colour = 0xFF202020;

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
    printf("PANEL %ux%u\n", w, h);
    fflush(stdout);
}

static void layer_closed(void *data, struct zwlr_layer_surface_v1 *layer)
{
    (void)data; (void)layer;
    running = 0;
}

static const struct zwlr_layer_surface_v1_listener layer_listener = { layer_configure, layer_closed };

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data; (void)version;
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(registry, name, &wl_compositor_interface, 4);
    else if (strcmp(interface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(registry, name, &wl_shm_interface, 1);
    else if (strcmp(interface, zwlr_layer_shell_v1_interface.name) == 0)
        layer_shell = wl_registry_bind(registry, name, &zwlr_layer_shell_v1_interface, 1);
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

int main(int argc, char **argv)
{
    if (argc < 3) {
        fprintf(stderr, "usage: panel top|bottom|left|right SIZE\n");
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
    wl_surface_commit(surface);

    while (running && wl_display_dispatch(display) != -1) {
    }

    return 0;
}
