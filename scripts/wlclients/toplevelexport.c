#define _GNU_SOURCE
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>
#include <wayland-client.h>
#include "ext-foreign-toplevel-list-v1-client-protocol.h"
#include "hyprland-toplevel-mapping-v1-client-protocol.h"
#include "hyprland-toplevel-export-v1-client-protocol.h"

static struct wl_shm *shm;
static struct ext_foreign_toplevel_list_v1 *list;
static struct hyprland_toplevel_mapping_manager_v1 *mapping;
static struct hyprland_toplevel_export_manager_v1 *export_manager;
static struct ext_foreign_toplevel_handle_v1 *wanted;
static char wanted_app[256];
static const char *match_app;
static uint64_t address;
static int have_address = -1;
static uint32_t buf_width, buf_height, buf_stride, buf_format;
static int buffer_done, frame_ready, frame_failed;
static uint32_t flags;

static void handle_app_id(void *d, struct ext_foreign_toplevel_handle_v1 *h, const char *app_id)
{
    (void)d;
    if (!wanted && (!match_app || !strcmp(app_id, match_app))) {
        wanted = h;
        snprintf(wanted_app, sizeof wanted_app, "%s", app_id);
    }
}

static void handle_noop_str(void *d, struct ext_foreign_toplevel_handle_v1 *h, const char *s) { (void)d; (void)h; (void)s; }
static void handle_noop(void *d, struct ext_foreign_toplevel_handle_v1 *h) { (void)d; (void)h; }

static const struct ext_foreign_toplevel_handle_v1_listener handle_listener = {
    .closed = handle_noop, .done = handle_noop, .title = handle_noop_str,
    .app_id = handle_app_id, .identifier = handle_noop_str,
};

static void list_toplevel(void *d, struct ext_foreign_toplevel_list_v1 *l, struct ext_foreign_toplevel_handle_v1 *h)
{
    (void)d; (void)l;
    ext_foreign_toplevel_handle_v1_add_listener(h, &handle_listener, NULL);
}

static void list_finished(void *d, struct ext_foreign_toplevel_list_v1 *l) { (void)d; (void)l; }

static const struct ext_foreign_toplevel_list_v1_listener list_listener = {
    .toplevel = list_toplevel, .finished = list_finished,
};

static void map_address(void *d, struct hyprland_toplevel_window_mapping_handle_v1 *h, uint32_t hi, uint32_t lo)
{
    (void)d; (void)h;
    address = ((uint64_t)hi << 32) | lo;
    have_address = 1;
}

static void map_failed(void *d, struct hyprland_toplevel_window_mapping_handle_v1 *h)
{
    (void)d; (void)h;
    have_address = 0;
}

static const struct hyprland_toplevel_window_mapping_handle_v1_listener map_listener = {
    .window_address = map_address, .failed = map_failed,
};

static void frame_buffer(void *d, struct hyprland_toplevel_export_frame_v1 *f, uint32_t format, uint32_t w, uint32_t h, uint32_t stride)
{
    (void)d; (void)f;
    buf_format = format; buf_width = w; buf_height = h; buf_stride = stride;
}

static void frame_damage(void *d, struct hyprland_toplevel_export_frame_v1 *f, uint32_t x, uint32_t y, uint32_t w, uint32_t h)
{
    (void)d; (void)f;
    printf("toplevelexport: damage %u,%u %ux%u\n", x, y, w, h);
}

static void frame_flags(void *d, struct hyprland_toplevel_export_frame_v1 *f, uint32_t value)
{ (void)d; (void)f; flags = value; }

static void frame_ready_cb(void *d, struct hyprland_toplevel_export_frame_v1 *f, uint32_t hi, uint32_t lo, uint32_t ns)
{ (void)d; (void)f; (void)hi; (void)lo; (void)ns; frame_ready = 1; }

static void frame_failed_cb(void *d, struct hyprland_toplevel_export_frame_v1 *f)
{ (void)d; (void)f; frame_failed = 1; }

static void frame_dmabuf(void *d, struct hyprland_toplevel_export_frame_v1 *f, uint32_t format, uint32_t w, uint32_t h)
{
    (void)d; (void)f;
    printf("toplevelexport: linux_dmabuf offered format %08x %ux%u\n", format, w, h);
}

static void frame_buffer_done(void *d, struct hyprland_toplevel_export_frame_v1 *f)
{ (void)d; (void)f; buffer_done = 1; }

static const struct hyprland_toplevel_export_frame_v1_listener frame_listener = {
    .buffer = frame_buffer, .damage = frame_damage, .flags = frame_flags, .ready = frame_ready_cb,
    .failed = frame_failed_cb, .linux_dmabuf = frame_dmabuf, .buffer_done = frame_buffer_done,
};

static void registry_global(void *d, struct wl_registry *r, uint32_t name, const char *iface, uint32_t version)
{
    (void)d;
    if (!strcmp(iface, wl_shm_interface.name)) {
        shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
    } else if (!strcmp(iface, ext_foreign_toplevel_list_v1_interface.name)) {
        list = wl_registry_bind(r, name, &ext_foreign_toplevel_list_v1_interface, 1);
        ext_foreign_toplevel_list_v1_add_listener(list, &list_listener, NULL);
    } else if (!strcmp(iface, hyprland_toplevel_mapping_manager_v1_interface.name)) {
        mapping = wl_registry_bind(r, name, &hyprland_toplevel_mapping_manager_v1_interface, 1);
    } else if (!strcmp(iface, hyprland_toplevel_export_manager_v1_interface.name)) {
        export_manager = wl_registry_bind(r, name, &hyprland_toplevel_export_manager_v1_interface, version < 2 ? version : 2);
    }
}

static void registry_remove(void *d, struct wl_registry *r, uint32_t name) { (void)d; (void)r; (void)name; }

static const struct wl_registry_listener registry_listener = { .global = registry_global, .global_remove = registry_remove };

int main(int argc, char **argv)
{
    const char *out = argc > 1 ? argv[1] : "export.ppm";
    match_app = argc > 2 ? argv[2] : NULL;

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "toplevelexport: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!shm || !list || !mapping || !export_manager) {
        fprintf(stderr, "toplevelexport: missing wl_shm, ext_foreign_toplevel_list_v1, mapping or export\n");
        return 1;
    }

    wl_display_roundtrip(display);
    wl_display_roundtrip(display);
    if (!wanted) {
        fprintf(stderr, "toplevelexport: no toplevel%s%s\n", match_app ? " with app id " : "", match_app ? match_app : "");
        return 1;
    }

    struct hyprland_toplevel_window_mapping_handle_v1 *map =
        hyprland_toplevel_mapping_manager_v1_get_window_for_toplevel(mapping, wanted);
    hyprland_toplevel_window_mapping_handle_v1_add_listener(map, &map_listener, NULL);
    while (have_address < 0 && wl_display_dispatch(display) != -1) {
    }

    if (have_address != 1) {
        fprintf(stderr, "toplevelexport: mapping failed for %s\n", wanted_app);
        return 1;
    }

    printf("toplevelexport: %s is window %lx\n", wanted_app, (unsigned long)address);

    struct hyprland_toplevel_export_frame_v1 *frame =
        hyprland_toplevel_export_manager_v1_capture_toplevel(export_manager, 0, (uint32_t)address);
    hyprland_toplevel_export_frame_v1_add_listener(frame, &frame_listener, NULL);
    while (!buffer_done && !frame_failed && wl_display_dispatch(display) != -1) {
    }

    if (frame_failed) {
        fprintf(stderr, "toplevelexport: frame failed before buffer_done\n");
        return 1;
    }

    printf("toplevelexport: buffer %ux%u stride %u format %u\n", buf_width, buf_height, buf_stride, buf_format);
    int size = (int)(buf_stride * buf_height);
    int fd = memfd_create("export", MFD_CLOEXEC);
    if (fd < 0 || ftruncate(fd, size) < 0) {
        return 1;
    }

    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buffer = wl_shm_pool_create_buffer(pool, 0, (int)buf_width, (int)buf_height, (int)buf_stride, buf_format);
    wl_shm_pool_destroy(pool);
    hyprland_toplevel_export_frame_v1_copy(frame, buffer, 1);
    while (!frame_ready && !frame_failed && wl_display_dispatch(display) != -1) {
    }

    if (frame_failed) {
        fprintf(stderr, "toplevelexport: copy failed\n");
        return 1;
    }

    uint32_t *pixels = mmap(NULL, (size_t)size, PROT_READ, MAP_SHARED, fd, 0);
    FILE *ppm = fopen(out, "wb");
    if (!ppm || pixels == MAP_FAILED) {
        return 1;
    }

    fprintf(ppm, "P6\n%u %u\n255\n", buf_width, buf_height);
    for (uint32_t y = 0; y < buf_height; y++) {
        for (uint32_t x = 0; x < buf_width; x++) {
            uint32_t p = pixels[(y * buf_stride / 4) + x];
            unsigned char rgb[3] = { (p >> 16) & 0xff, (p >> 8) & 0xff, p & 0xff };
            fwrite(rgb, 1, 3, ppm);
        }
    }

    fclose(ppm);
    printf("toplevelexport: ready flags=%u wrote %s\n", flags, out);
    return 0;
}
