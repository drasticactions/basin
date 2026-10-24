#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "wlr-export-dmabuf-unstable-v1-client-protocol.h"

/* exportdmabuf: asks zwlr_export_dmabuf_manager_v1 for one frame of every output, ROUNDS times, and prints READY NAME W H OBJECTS or CANCEL NAME REASON for each. With "pixel", READY also prints the centre pixel of a linear first plane read through mmap. */

#define MAX_OUTPUTS 8

struct output {
    struct wl_output *output;
    char name[64];
    int done;
};

static struct zwlr_export_dmabuf_manager_v1 *manager;
static struct output outputs[MAX_OUTPUTS];
static int output_count;
static int pending;
static int read_pixel;

static void output_geometry(void *d, struct wl_output *o, int32_t x, int32_t y, int32_t pw, int32_t ph, int32_t sub, const char *make, const char *model, int32_t transform)
{ (void)d; (void)o; (void)x; (void)y; (void)pw; (void)ph; (void)sub; (void)make; (void)model; (void)transform; }
static void output_mode(void *d, struct wl_output *o, uint32_t flags, int32_t w, int32_t h, int32_t refresh)
{ (void)d; (void)o; (void)flags; (void)w; (void)h; (void)refresh; }
static void output_done(void *d, struct wl_output *o) { (void)d; (void)o; }
static void output_scale(void *d, struct wl_output *o, int32_t f) { (void)d; (void)o; (void)f; }
static void output_name(void *d, struct wl_output *o, const char *name)
{
    (void)o;
    struct output *out = d;
    snprintf(out->name, sizeof out->name, "%s", name);
}
static void output_description(void *d, struct wl_output *o, const char *desc) { (void)d; (void)o; (void)desc; }

static const struct wl_output_listener output_listener = {
    output_geometry, output_mode, output_done, output_scale, output_name, output_description,
};

struct frame_state {
    struct output *output;
    uint32_t width, height, objects;
    int pixel_known;
    uint32_t pixel;
};

static void frame_frame(void *d, struct zwlr_export_dmabuf_frame_v1 *f, uint32_t w, uint32_t h, uint32_t ox, uint32_t oy,
    uint32_t buffer_flags, uint32_t flags, uint32_t format, uint32_t mod_high, uint32_t mod_low, uint32_t num_objects)
{
    (void)f; (void)ox; (void)oy; (void)buffer_flags; (void)flags; (void)format; (void)mod_high; (void)mod_low;
    struct frame_state *s = d;
    s->width = w;
    s->height = h;
    s->objects = num_objects;
}

static void frame_object(void *d, struct zwlr_export_dmabuf_frame_v1 *f, uint32_t index, int32_t fd, uint32_t size,
    uint32_t offset, uint32_t stride, uint32_t plane_index)
{
    (void)f;
    struct frame_state *s = d;
    if (read_pixel && index == 0 && plane_index == 0 && s->height > 0) {
        uint8_t *map = mmap(NULL, size, PROT_READ, MAP_SHARED, fd, 0);
        if (map != MAP_FAILED) {
            size_t at = offset + (size_t)(s->height / 2) * stride + (size_t)(s->width / 2) * 4;
            if (at + 4 <= size) {
                s->pixel = *(uint32_t *)(map + at);
                s->pixel_known = 1;
            }
            munmap(map, size);
        }
    }
    close(fd);
}

static void frame_ready(void *d, struct zwlr_export_dmabuf_frame_v1 *f, uint32_t sec_hi, uint32_t sec_lo, uint32_t nsec)
{
    (void)sec_hi; (void)sec_lo; (void)nsec;
    struct frame_state *s = d;
    if (s->pixel_known)
        printf("READY %s %u %u %u %08X\n", s->output->name, s->width, s->height, s->objects, s->pixel);
    else
        printf("READY %s %u %u %u\n", s->output->name, s->width, s->height, s->objects);
    fflush(stdout);
    zwlr_export_dmabuf_frame_v1_destroy(f);
    free(s);
    pending--;
}

static void frame_cancel(void *d, struct zwlr_export_dmabuf_frame_v1 *f, uint32_t reason)
{
    struct frame_state *s = d;
    const char *why = reason == ZWLR_EXPORT_DMABUF_FRAME_V1_CANCEL_REASON_TEMPORARY ? "temporary"
        : reason == ZWLR_EXPORT_DMABUF_FRAME_V1_CANCEL_REASON_PERMANENT ? "permanent" : "resizing";
    printf("CANCEL %s %s\n", s->output->name, why);
    fflush(stdout);
    zwlr_export_dmabuf_frame_v1_destroy(f);
    free(s);
    pending--;
}

static const struct zwlr_export_dmabuf_frame_v1_listener frame_listener = {
    frame_frame, frame_object, frame_ready, frame_cancel,
};

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data;
    if (strcmp(interface, zwlr_export_dmabuf_manager_v1_interface.name) == 0)
        manager = wl_registry_bind(registry, name, &zwlr_export_dmabuf_manager_v1_interface, 1);
    else if (strcmp(interface, wl_output_interface.name) == 0 && output_count < MAX_OUTPUTS) {
        struct output *out = &outputs[output_count++];
        snprintf(out->name, sizeof out->name, "output%d", output_count - 1);
        out->output = wl_registry_bind(registry, name, &wl_output_interface, version < 4 ? version : 4);
        wl_output_add_listener(out->output, &output_listener, out);
    }
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

int main(int argc, char **argv)
{
    int rounds = 1;
    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "pixel") == 0) read_pixel = 1;
        else rounds = atoi(argv[i]);
    }
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    wl_display_roundtrip(display);
    if (!manager) { printf("UNAVAILABLE\n"); return 1; }
    for (int r = 0; r < rounds; r++) {
        for (int i = 0; i < output_count; i++) {
            struct frame_state *s = calloc(1, sizeof *s);
            s->output = &outputs[i];
            struct zwlr_export_dmabuf_frame_v1 *frame = zwlr_export_dmabuf_manager_v1_capture_output(manager, 0, outputs[i].output);
            zwlr_export_dmabuf_frame_v1_add_listener(frame, &frame_listener, s);
            pending++;
        }
        while (pending > 0 && wl_display_dispatch(display) != -1) {
        }
    }
    return 0;
}
