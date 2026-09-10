#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "xdg-decoration-unstable-v1-client-protocol.h"

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct zxdg_decoration_manager_v1 *decoration_manager;
static struct wl_surface *surface;
static struct xdg_surface *xdg_surface;
static struct xdg_toplevel *toplevel;
static struct zxdg_toplevel_decoration_v1 *decoration;
static int width = 256, height = 256;
static int running = 1;
static uint32_t colour = 0xFF3C7A9E;
static uint32_t wanted = ZXDG_TOPLEVEL_DECORATION_V1_MODE_SERVER_SIDE;

static struct wl_buffer *make_buffer(int w, int h)
{
    int stride = w * 4;
    int size = stride * h;
    int fd = memfd_create("ssdwin", 0);
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

static void draw(void)
{
    struct wl_buffer *buf = make_buffer(width, height);
    if (!buf) return;
    wl_surface_attach(surface, buf, 0, 0);
    wl_surface_damage_buffer(surface, 0, 0, width, height);
    wl_surface_commit(surface);
}

static void xdg_surface_configure(void *data, struct xdg_surface *s, uint32_t serial)
{
    (void)data;
    xdg_surface_ack_configure(s, serial);
    draw();
}

static const struct xdg_surface_listener xdg_surface_listener = { xdg_surface_configure };

static void toplevel_configure(void *data, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *states)
{
    (void)data; (void)t; (void)states;
    if (w > 0) width = w;
    if (h > 0) height = h;
}

static void toplevel_close(void *data, struct xdg_toplevel *t)
{
    (void)data; (void)t;
    running = 0;
}

static void toplevel_bounds(void *data, struct xdg_toplevel *t, int32_t w, int32_t h) { (void)data; (void)t; (void)w; (void)h; }
static void toplevel_caps(void *data, struct xdg_toplevel *t, struct wl_array *caps) { (void)data; (void)t; (void)caps; }

static const struct xdg_toplevel_listener toplevel_listener = {
    toplevel_configure, toplevel_close, toplevel_bounds, toplevel_caps
};

static void decoration_configure(void *data, struct zxdg_toplevel_decoration_v1 *d, uint32_t mode)
{
    (void)data; (void)d;
    printf("DECORATION %s\n", mode == ZXDG_TOPLEVEL_DECORATION_V1_MODE_SERVER_SIDE ? "server" : "client");
    fflush(stdout);
}

static const struct zxdg_toplevel_decoration_v1_listener decoration_listener = { decoration_configure };

static void wm_ping(void *data, struct xdg_wm_base *base, uint32_t serial)
{
    (void)data;
    xdg_wm_base_pong(base, serial);
}

static const struct xdg_wm_base_listener wm_listener = { wm_ping };

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data; (void)version;
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(registry, name, &wl_compositor_interface, 4);
    else if (strcmp(interface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(registry, name, &wl_shm_interface, 1);
    else if (strcmp(interface, xdg_wm_base_interface.name) == 0)
        wm_base = wl_registry_bind(registry, name, &xdg_wm_base_interface, 1);
    else if (strcmp(interface, zxdg_decoration_manager_v1_interface.name) == 0)
        decoration_manager = wl_registry_bind(registry, name, &zxdg_decoration_manager_v1_interface, 1);
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

int main(int argc, char **argv)
{
    if (argc > 1 && strcmp(argv[1], "client") == 0) wanted = ZXDG_TOPLEVEL_DECORATION_V1_MODE_CLIENT_SIDE;
    if (argc > 1 && strcmp(argv[1], "none") == 0) wanted = 0;
    int fullscreen = 0;
    for (int i = 1; i < argc; i++) if (strcmp(argv[i], "fullscreen") == 0) fullscreen = 1;

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base) { fprintf(stderr, "missing globals\n"); return 1; }
    xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);

    surface = wl_compositor_create_surface(compositor);
    xdg_surface = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xdg_surface, &xdg_surface_listener, NULL);
    toplevel = xdg_surface_get_toplevel(xdg_surface);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "ssdwin");
    xdg_toplevel_set_app_id(toplevel, "ssdwin");
    if (decoration_manager && wanted != 0) {
        decoration = zxdg_decoration_manager_v1_get_toplevel_decoration(decoration_manager, toplevel);
        zxdg_toplevel_decoration_v1_add_listener(decoration, &decoration_listener, NULL);
        zxdg_toplevel_decoration_v1_set_mode(decoration, wanted);
    } else if (wanted != 0) {
        printf("DECORATION unavailable\n");
        fflush(stdout);
    }
    if (fullscreen) xdg_toplevel_set_fullscreen(toplevel, NULL);
    wl_surface_commit(surface);

    while (running && wl_display_dispatch(display) != -1) {
    }

    return 0;
}
