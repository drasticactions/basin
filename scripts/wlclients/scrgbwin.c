// scrgbwin [VALUE] [SECONDS]: one 256x256 xdg toplevel titled scrgbwin, drawn in
// ABGR16161616F shm and tagged with wp_color_manager_v1.create_windows_scrgb. The left
// half is R=G=B=1.0 (80 cd/m2), the right half R=G=B=VALUE (4.0 by default, 320 cd/m2).
// Prints SCRGB offered|missing, SHM no-fp16 when wl_shm lacks the format, READY, CONFIGURE W H and SHOWN once the first frame is up.
#define _GNU_SOURCE
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "color-management-v1-client-protocol.h"

#define SIZE 256

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct wp_color_manager_v1 *manager;
static int scrgb_offered;
static int manager_done;
static int ready;
static int configured;
static int frame_done;
static int shm_fp16;

static void on_shm_format(void *d, struct wl_shm *s, uint32_t format)
{
    (void)d; (void)s;
    if (format == WL_SHM_FORMAT_ABGR16161616F)
        shm_fp16 = 1;
}

static const struct wl_shm_listener shm_listener = { .format = on_shm_format };

static uint16_t half(float value)
{
    union { float f; uint32_t u; } bits = { value };
    uint32_t sign = (bits.u >> 16) & 0x8000;
    int32_t exponent = (int32_t)((bits.u >> 23) & 0xFF) - 127 + 15;
    uint32_t mantissa = bits.u & 0x7FFFFF;
    if (exponent <= 0)
        return (uint16_t)sign;
    if (exponent >= 31)
        return (uint16_t)(sign | 0x7C00);
    return (uint16_t)(sign | ((uint32_t)exponent << 10) | (mantissa >> 13));
}

static void on_feature(void *d, struct wp_color_manager_v1 *m, uint32_t v)
{
    (void)d; (void)m;
    if (v == WP_COLOR_MANAGER_V1_FEATURE_WINDOWS_SCRGB)
        scrgb_offered = 1;
}

static void on_ignore(void *d, struct wp_color_manager_v1 *m, uint32_t v)
{
    (void)d; (void)m; (void)v;
}

static void on_manager_done(void *d, struct wp_color_manager_v1 *m)
{
    (void)d; (void)m;
    manager_done = 1;
}

static const struct wp_color_manager_v1_listener manager_listener = {
    .supported_intent = on_ignore,
    .supported_feature = on_feature,
    .supported_tf_named = on_ignore,
    .supported_primaries_named = on_ignore,
    .done = on_manager_done,
};

static void on_failed(void *d, struct wp_image_description_v1 *i, uint32_t cause, const char *msg)
{
    (void)d; (void)i;
    printf("FAILED %u %s\n", cause, msg);
    exit(1);
}

static void on_ready(void *d, struct wp_image_description_v1 *i, uint32_t identity)
{
    (void)d; (void)i; (void)identity;
    ready = 1;
}

static void on_ready2(void *d, struct wp_image_description_v1 *i, uint32_t hi, uint32_t lo)
{
    (void)d; (void)i; (void)hi; (void)lo;
    ready = 1;
}

static const struct wp_image_description_v1_listener description_listener = {
    .failed = on_failed,
    .ready = on_ready,
    .ready2 = on_ready2,
};

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

static const struct xdg_toplevel_listener toplevel_listener = {
    .configure = toplevel_configure,
    .close = toplevel_close,
};

static void frame_callback(void *d, struct wl_callback *cb, uint32_t time)
{
    (void)d; (void)time;
    wl_callback_destroy(cb);
    frame_done = 1;
}

static const struct wl_callback_listener frame_listener = { .done = frame_callback };

static void on_global(void *d, struct wl_registry *r, uint32_t name, const char *interface, uint32_t version)
{
    (void)d;
    if (!strcmp(interface, wl_compositor_interface.name))
        compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    else if (!strcmp(interface, wl_shm_interface.name))
    {
        shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
        wl_shm_add_listener(shm, &shm_listener, NULL);
    }
    else if (!strcmp(interface, xdg_wm_base_interface.name))
        wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, 1);
    else if (!strcmp(interface, wp_color_manager_v1_interface.name))
        manager = wl_registry_bind(r, name, &wp_color_manager_v1_interface, version < 2 ? version : 2);
}

static void on_global_remove(void *d, struct wl_registry *r, uint32_t name)
{
    (void)d; (void)r; (void)name;
}

static const struct wl_registry_listener registry_listener = { .global = on_global, .global_remove = on_global_remove };

int main(int argc, char **argv)
{
    float value = argc > 1 ? (float)atof(argv[1]) : 4.0f;
    double seconds = argc > 2 ? atof(argv[2]) : 10;
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "scrgbwin: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base || !manager) {
        printf("SCRGB missing\n");
        return 2;
    }

    wp_color_manager_v1_add_listener(manager, &manager_listener, NULL);
    while (!manager_done && wl_display_roundtrip(display) != -1) {
    }

    printf("SCRGB %s\n", scrgb_offered ? "offered" : "missing");
    if (!scrgb_offered)
        return 3;

    if (!shm_fp16) {
        printf("SHM no-fp16\n");
        return 4;
    }

    xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);
    struct wl_surface *surface = wl_compositor_create_surface(compositor);
    struct wp_image_description_v1 *description = wp_color_manager_v1_create_windows_scrgb(manager);
    wp_image_description_v1_add_listener(description, &description_listener, NULL);
    while (!ready && wl_display_roundtrip(display) != -1) {
    }

    printf("READY\n");
    struct wp_color_management_surface_v1 *color = wp_color_manager_v1_get_surface(manager, surface);
    wp_color_management_surface_v1_set_image_description(color, description,
        WP_COLOR_MANAGER_V1_RENDER_INTENT_PERCEPTUAL);

    struct xdg_surface *xdg = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xdg, &surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xdg);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "scrgbwin");
    xdg_toplevel_set_app_id(toplevel, "scrgbwin");
    wl_surface_commit(surface);
    while (!configured && wl_display_dispatch(display) != -1) {
    }

    int stride = SIZE * 8;
    int fd = memfd_create("scrgbwin", 0);
    if (fd < 0 || ftruncate(fd, stride * SIZE) < 0)
        return 1;
    uint16_t *pixels = mmap(NULL, stride * SIZE, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    uint16_t one = half(1.0f);
    uint16_t bright = half(value);
    for (int y = 0; y < SIZE; y++) {
        for (int x = 0; x < SIZE; x++) {
            uint16_t *p = pixels + (y * SIZE + x) * 4;
            uint16_t c = x < SIZE / 2 ? one : bright;
            p[0] = c;
            p[1] = c;
            p[2] = c;
            p[3] = one;
        }
    }

    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, stride * SIZE);
    struct wl_buffer *buffer = wl_shm_pool_create_buffer(pool, 0, SIZE, SIZE, stride, WL_SHM_FORMAT_ABGR16161616F);
    wl_shm_pool_destroy(pool);
    close(fd);
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
    return 0;
}
