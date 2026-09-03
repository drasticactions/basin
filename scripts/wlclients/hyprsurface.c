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
#include "hyprland-surface-v1-client-protocol.h"

#define WIDTH 200
#define HEIGHT 150

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct hyprland_surface_manager_v1 *manager;
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

static void registry_global(void *d, struct wl_registry *r, uint32_t name, const char *iface, uint32_t version)
{
    (void)d;
    if (!strcmp(iface, wl_compositor_interface.name)) {
        compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    } else if (!strcmp(iface, wl_shm_interface.name)) {
        shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
    } else if (!strcmp(iface, xdg_wm_base_interface.name)) {
        wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, 1);
        xdg_wm_base_add_listener(wm_base, &wm_base_listener, NULL);
    } else if (!strcmp(iface, hyprland_surface_manager_v1_interface.name)) {
        manager = wl_registry_bind(r, name, &hyprland_surface_manager_v1_interface, version < 2 ? version : 2);
    }
}

static void registry_remove(void *d, struct wl_registry *r, uint32_t name)
{ (void)d; (void)r; (void)name; }

static const struct wl_registry_listener registry_listener = {
    .global = registry_global, .global_remove = registry_remove,
};

static struct wl_buffer *make_buffer(uint32_t color)
{
    int stride = WIDTH * 4;
    int size = stride * HEIGHT;
    int fd = memfd_create("hyprsurface", MFD_CLOEXEC);
    if (fd < 0 || ftruncate(fd, size) < 0) {
        return NULL;
    }

    uint32_t *pixels = mmap(NULL, (size_t)size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (pixels == MAP_FAILED) {
        close(fd);
        return NULL;
    }

    for (int i = 0; i < WIDTH * HEIGHT; i++) {
        pixels[i] = color;
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
    double opacity = argc > 1 ? atof(argv[1]) : 0.5;
    long seconds = argc > 2 ? atol(argv[2]) : 0;
    int region = argc > 3 ? atoi(argv[3]) : 1;

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "hyprsurface: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base) {
        fprintf(stderr, "hyprsurface: the compositor is missing wl_compositor, wl_shm or xdg_wm_base\n");
        return 1;
    }

    if (!manager) {
        fprintf(stderr, "hyprsurface: no hyprland_surface_manager_v1\n");
        return 1;
    }

    struct wl_surface *surface = wl_compositor_create_surface(compositor);
    struct xdg_surface *xdg = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xdg, &surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xdg);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "hyprsurface");
    xdg_toplevel_set_app_id(toplevel, "basin.hyprsurface");
    wl_surface_commit(surface);
    while (!configured && wl_display_dispatch(display) != -1) {
    }

    struct wl_buffer *buffer = make_buffer(0xffdd2020);
    if (!buffer) {
        fprintf(stderr, "hyprsurface: cannot make a buffer\n");
        return 1;
    }

    struct hyprland_surface_v1 *hypr = hyprland_surface_manager_v1_get_hyprland_surface(manager, surface);
    hyprland_surface_v1_set_opacity(hypr, wl_fixed_from_double(opacity));
    if (region && hyprland_surface_manager_v1_get_version(manager) >= 2) {
        struct wl_region *visible = wl_compositor_create_region(compositor);
        wl_region_add(visible, 0, 0, WIDTH / 2, HEIGHT);
        hyprland_surface_v1_set_visible_region(hypr, visible);
        wl_region_destroy(visible);
    }

    wl_surface_attach(surface, buffer, 0, 0);
    wl_surface_damage(surface, 0, 0, WIDTH, HEIGHT);
    wl_surface_commit(surface);
    wl_display_roundtrip(display);
    printf("hyprsurface: opacity %.2f%s committed\n", opacity, region ? ", left half visible" : "");
    fflush(stdout);

    time_t deadline = seconds > 0 ? time(NULL) + seconds : 0;
    while (running && wl_display_dispatch(display) != -1) {
        if (deadline && time(NULL) >= deadline) {
            break;
        }
    }

    hyprland_surface_v1_destroy(hypr);
    printf("hyprsurface: done\n");
    return 0;
}
