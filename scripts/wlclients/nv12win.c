// nv12win [SECONDS]: one 256x256 xdg toplevel titled nv12win whose buffer is a linear
// NV12 dmabuf, allocated as a GBM R8 buffer 1.5 times as tall with the chroma plane after
// the luma rows. Four quadrants (red, green, blue, gold) are encoded BT.601 limited.
// Prints FAILED REASON when the compositor refuses the buffer, CONFIGURE W H, and SHOWN
// once the first frame is up.
#define _GNU_SOURCE
#include <fcntl.h>
#include <gbm.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "linux-dmabuf-v1-client-protocol.h"

#define SIZE 256
#define FOURCC_NV12 0x3231564E

static struct wl_compositor *compositor;
static struct xdg_wm_base *wm_base;
static struct zwp_linux_dmabuf_v1 *dmabuf;
static struct wl_buffer *buffer;
static int failed;
static int configured;
static int frame_done;

static const uint8_t colors[4][3] = { { 255, 0, 0 }, { 0, 255, 0 }, { 0, 0, 255 }, { 200, 180, 40 } };

static void encode(const uint8_t *rgb, uint8_t *y, uint8_t *cb, uint8_t *cr)
{
    double r = rgb[0] / 255.0, g = rgb[1] / 255.0, b = rgb[2] / 255.0;
    double luma = 0.299 * r + 0.587 * g + 0.114 * b;
    *y = (uint8_t)(16.0 + 219.0 * luma + 0.5);
    *cb = (uint8_t)(128.0 + 224.0 * (b - luma) / 1.772 + 0.5);
    *cr = (uint8_t)(128.0 + 224.0 * (r - luma) / 1.402 + 0.5);
}

static void on_created(void *d, struct zwp_linux_buffer_params_v1 *p, struct wl_buffer *b)
{
    (void)d; (void)p;
    buffer = b;
}

static void on_failed(void *d, struct zwp_linux_buffer_params_v1 *p)
{
    (void)d; (void)p;
    failed = 1;
}

static const struct zwp_linux_buffer_params_v1_listener params_listener = { .created = on_created, .failed = on_failed };

static void wm_ping(void *d, struct xdg_wm_base *b, uint32_t serial)
{
    (void)d;
    xdg_wm_base_pong(b, serial);
}

static const struct xdg_wm_base_listener wm_listener = { .ping = wm_ping };

static void surface_configure(void *d, struct xdg_surface *s, uint32_t serial)
{
    (void)d;
    xdg_surface_ack_configure(s, serial);
    configured = 1;
}

static const struct xdg_surface_listener surface_listener = { .configure = surface_configure };

static void toplevel_configure(void *d, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *states)
{
    (void)d; (void)t; (void)states;
    printf("CONFIGURE %d %d\n", w, h);
}

static void toplevel_close(void *d, struct xdg_toplevel *t)
{
    (void)d; (void)t;
    exit(0);
}

static const struct xdg_toplevel_listener toplevel_listener = { .configure = toplevel_configure, .close = toplevel_close };

static void frame_callback(void *d, struct wl_callback *cb, uint32_t time)
{
    (void)d; (void)time;
    wl_callback_destroy(cb);
    frame_done = 1;
}

static const struct wl_callback_listener frame_listener = { .done = frame_callback };

static void on_global(void *d, struct wl_registry *r, uint32_t name, const char *interface, uint32_t version)
{
    (void)d; (void)version;
    if (!strcmp(interface, wl_compositor_interface.name))
        compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    else if (!strcmp(interface, xdg_wm_base_interface.name))
        wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, 1);
    else if (!strcmp(interface, zwp_linux_dmabuf_v1_interface.name))
        dmabuf = wl_registry_bind(r, name, &zwp_linux_dmabuf_v1_interface, 3);
}

static void on_global_remove(void *d, struct wl_registry *r, uint32_t name)
{
    (void)d; (void)r; (void)name;
}

static const struct wl_registry_listener registry_listener = { .global = on_global, .global_remove = on_global_remove };

int main(int argc, char **argv)
{
    double seconds = argc > 1 ? atof(argv[1]) : 10;
    const char *node = getenv("NV12WIN_RENDER_NODE") ? getenv("NV12WIN_RENDER_NODE") : "/dev/dri/renderD128";
    int drm = open(node, O_RDWR | O_CLOEXEC);
    struct gbm_device *gbm = drm >= 0 ? gbm_create_device(drm) : NULL;
    struct gbm_bo *bo = gbm ? gbm_bo_create(gbm, SIZE, SIZE * 3 / 2, GBM_FORMAT_R8, GBM_BO_USE_LINEAR) : NULL;
    if (!bo) {
        printf("FAILED gbm\n");
        return 1;
    }

    uint32_t stride = 0;
    void *map_data = NULL;
    uint8_t *pixels = gbm_bo_map(bo, 0, 0, SIZE, SIZE * 3 / 2, GBM_BO_TRANSFER_WRITE, &stride, &map_data);
    if (!pixels) {
        printf("FAILED map\n");
        return 1;
    }

    for (int y = 0; y < SIZE; y++) {
        for (int x = 0; x < SIZE; x++) {
            const uint8_t *rgb = colors[(y < SIZE / 2 ? 0 : 2) + (x < SIZE / 2 ? 0 : 1)];
            uint8_t luma, cb, cr;
            encode(rgb, &luma, &cb, &cr);
            pixels[y * stride + x] = luma;
            if (x % 2 == 0 && y % 2 == 0) {
                uint8_t *uv = pixels + SIZE * stride + (y / 2) * stride + x;
                uv[0] = cb;
                uv[1] = cr;
            }
        }
    }

    gbm_bo_unmap(bo, map_data);
    int fd = gbm_bo_get_fd(bo);

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "nv12win: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !wm_base || !dmabuf) {
        printf("FAILED globals\n");
        return 2;
    }

    struct zwp_linux_buffer_params_v1 *params = zwp_linux_dmabuf_v1_create_params(dmabuf);
    zwp_linux_buffer_params_v1_add_listener(params, &params_listener, NULL);
    zwp_linux_buffer_params_v1_add(params, fd, 0, 0, stride, 0, 0);
    zwp_linux_buffer_params_v1_add(params, fd, 1, SIZE * stride, stride, 0, 0);
    zwp_linux_buffer_params_v1_create(params, SIZE, SIZE, FOURCC_NV12, 0);
    while (!buffer && !failed && wl_display_dispatch(display) != -1) {
    }

    close(fd);
    if (failed || !buffer) {
        printf("FAILED refused\n");
        return 3;
    }

    xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);
    struct wl_surface *surface = wl_compositor_create_surface(compositor);
    struct xdg_surface *xdg = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xdg, &surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xdg);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "nv12win");
    xdg_toplevel_set_app_id(toplevel, "nv12win");
    wl_surface_commit(surface);
    while (!configured && wl_display_dispatch(display) != -1) {
    }

    wl_surface_attach(surface, buffer, 0, 0);
    wl_surface_damage_buffer(surface, 0, 0, SIZE, SIZE);
    wl_callback_add_listener(wl_surface_frame(surface), &frame_listener, NULL);
    wl_surface_commit(surface);
    fflush(stdout);
    while (!frame_done && wl_display_dispatch(display) != -1) {
    }

    printf("SHOWN\n");
    fflush(stdout);
    struct timespec start, now;
    clock_gettime(CLOCK_MONOTONIC, &start);
    for (;;) {
        clock_gettime(CLOCK_MONOTONIC, &now);
        if ((now.tv_sec - start.tv_sec) + (now.tv_nsec - start.tv_nsec) / 1e9 >= seconds)
            break;
        if (wl_display_roundtrip(display) == -1)
            break;
        usleep(50000);
    }

    wl_display_disconnect(display);
    gbm_bo_destroy(bo);
    gbm_device_destroy(gbm);
    close(drm);
    return 0;
}
