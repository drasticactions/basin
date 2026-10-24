// syncsub [--one-timeline] [--late-acquire] [--count N] [--timeout-ms MS] [--size N]
//         [--settle N] [--hold-ms MS] [--recommit-shown]: one xdg toplevel titled syncsub,
// twice --size on each side, with a synchronized subsurface of --size. The subsurface
// commits N linear ARGB8888 GBM dmabufs, each with a wp_linux_drm_syncobj acquire and
// release point, and the parent does not commit between them. Each buffer has its own
// release timeline unless --one-timeline is given, the way Mesa WSI gives each swapchain
// image its own. --late-acquire commits with unsignaled acquire points and signals them
// all after the last commit, so the commits park and land in one pass.
//
// --settle N first shows one buffer for N frames, so a compositor can lift the subsurface
// onto a plane, prints SETTLED and waits --hold-ms. --recommit-shown then commits that
// shown buffer again into the cache before the N buffers. Its point must stay held while
// the buffer is on screen and signal once a later parent commit takes it off.
//
// Prints RELEASE I signaled|timeout for each point after the cached commits, then commits
// the parent and one more buffer and prints the same for the last cached buffer. A
// compositor that signals a superseded buffer at replace time prints RESULT ok.
// --destroy-cached then caches one more buffer, destroys the subsurface before any
// parent commit, and prints DISCARDED signaled|timeout for that point.
#define _GNU_SOURCE
#include <fcntl.h>
#include <gbm.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>
#include <wayland-client.h>
#include <xf86drm.h>
#include "xdg-shell-client-protocol.h"
#include "linux-dmabuf-v1-client-protocol.h"
#include "linux-drm-syncobj-v1-client-protocol.h"

#define MAX_BUFFERS 16
#define FOURCC_ARGB8888 0x34325241

static struct wl_compositor *compositor;
static struct wl_subcompositor *subcompositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct zwp_linux_dmabuf_v1 *dmabuf;
static struct wp_linux_drm_syncobj_manager_v1 *syncobj_manager;
static int configured;
static int drm_fd = -1;
static int CHILD = 100;
static int PARENT = 200;
static int frame_done;

struct timeline {
    uint32_t handle;
    struct wp_linux_drm_syncobj_timeline_v1 *proxy;
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

static void top_configure(void *d, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *s)
{
    (void)d; (void)t; (void)w; (void)h; (void)s;
}

static void top_close(void *d, struct xdg_toplevel *t)
{
    (void)d; (void)t;
}

static const struct xdg_toplevel_listener top_listener = { .configure = top_configure, .close = top_close };

static void on_frame(void *d, struct wl_callback *cb, uint32_t time)
{
    (void)d; (void)time;
    wl_callback_destroy(cb);
    frame_done = 1;
}

static const struct wl_callback_listener frame_listener = { .done = on_frame };

static void global(void *d, struct wl_registry *r, uint32_t name, const char *iface, uint32_t version)
{
    (void)d; (void)version;
    if (strcmp(iface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(r, name, &wl_compositor_interface, 4);
    else if (strcmp(iface, wl_subcompositor_interface.name) == 0)
        subcompositor = wl_registry_bind(r, name, &wl_subcompositor_interface, 1);
    else if (strcmp(iface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(r, name, &wl_shm_interface, 1);
    else if (strcmp(iface, xdg_wm_base_interface.name) == 0) {
        wm_base = wl_registry_bind(r, name, &xdg_wm_base_interface, 1);
        xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);
    } else if (strcmp(iface, zwp_linux_dmabuf_v1_interface.name) == 0)
        dmabuf = wl_registry_bind(r, name, &zwp_linux_dmabuf_v1_interface, 3);
    else if (strcmp(iface, wp_linux_drm_syncobj_manager_v1_interface.name) == 0)
        syncobj_manager = wl_registry_bind(r, name, &wp_linux_drm_syncobj_manager_v1_interface, 1);
}

static void global_remove(void *d, struct wl_registry *r, uint32_t name)
{
    (void)d; (void)r; (void)name;
}

static const struct wl_registry_listener registry_listener = { .global = global, .global_remove = global_remove };

static struct wl_buffer *parent_buffer(void)
{
    int stride = PARENT * 4, size = stride * PARENT;
    int fd = memfd_create("syncsub", MFD_CLOEXEC);
    if (fd < 0 || ftruncate(fd, size) < 0)
        return NULL;
    uint32_t *pixels = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    for (int i = 0; i < PARENT * PARENT; i++)
        pixels[i] = 0xFF303030;
    munmap(pixels, size);
    struct wl_shm_pool *pool = wl_shm_create_pool(shm, fd, size);
    struct wl_buffer *buffer = wl_shm_pool_create_buffer(pool, 0, PARENT, PARENT, stride, WL_SHM_FORMAT_ARGB8888);
    wl_shm_pool_destroy(pool);
    close(fd);
    return buffer;
}

static struct wl_buffer *child_buffer(struct gbm_device *gbm, uint32_t color)
{
    const uint64_t linear = 0;
    struct gbm_bo *bo = gbm_bo_create_with_modifiers2(gbm, CHILD, CHILD, GBM_FORMAT_ARGB8888, &linear, 1,
        GBM_BO_USE_RENDERING | GBM_BO_USE_SCANOUT);
    if (!bo)
        return NULL;
    uint32_t stride = 0;
    void *map_data = NULL;
    uint32_t *pixels = gbm_bo_map(bo, 0, 0, CHILD, CHILD, GBM_BO_TRANSFER_WRITE, &stride, &map_data);
    if (pixels) {
        for (int y = 0; y < CHILD; y++)
            for (int x = 0; x < CHILD; x++)
                pixels[y * (stride / 4) + x] = color;
        gbm_bo_unmap(bo, map_data);
    }
    int fd = gbm_bo_get_fd(bo);
    uint64_t modifier = gbm_bo_get_modifier(bo);
    struct zwp_linux_buffer_params_v1 *params = zwp_linux_dmabuf_v1_create_params(dmabuf);
    zwp_linux_buffer_params_v1_add(params, fd, 0, gbm_bo_get_offset(bo, 0), gbm_bo_get_stride(bo),
        (uint32_t)(modifier >> 32), (uint32_t)modifier);
    struct wl_buffer *buffer = zwp_linux_buffer_params_v1_create_immed(params, CHILD, CHILD, FOURCC_ARGB8888, 0);
    zwp_linux_buffer_params_v1_destroy(params);
    close(fd);
    return buffer;
}

static struct timeline make_timeline(void)
{
    struct timeline t = { 0 };
    int fd = -1;
    if (drmSyncobjCreate(drm_fd, 0, &t.handle) != 0 || drmSyncobjHandleToFD(drm_fd, t.handle, &fd) != 0) {
        fprintf(stderr, "syncsub: cannot create a timeline syncobj\n");
        exit(1);
    }
    t.proxy = wp_linux_drm_syncobj_manager_v1_import_timeline(syncobj_manager, fd);
    close(fd);
    return t;
}

static void signal_point(struct timeline *t, uint64_t point)
{
    drmSyncobjTimelineSignal(drm_fd, &t->handle, &point, 1);
}

static int wait_point(struct timeline *t, uint64_t point, int timeout_ms)
{
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    int64_t deadline = (int64_t)now.tv_sec * 1000000000LL + now.tv_nsec + (int64_t)timeout_ms * 1000000LL;
    return drmSyncobjTimelineWait(drm_fd, &t->handle, &point, 1, deadline,
        DRM_SYNCOBJ_WAIT_FLAGS_WAIT_FOR_SUBMIT, NULL) == 0;
}

static void commit_child(struct wl_surface *child, struct wp_linux_drm_syncobj_surface_v1 *sync, struct wl_buffer *buffer,
    struct timeline *acquire, uint64_t acquire_point, struct timeline *release, uint64_t release_point)
{
    wp_linux_drm_syncobj_surface_v1_set_acquire_point(sync, acquire->proxy, (uint32_t)(acquire_point >> 32), (uint32_t)acquire_point);
    wp_linux_drm_syncobj_surface_v1_set_release_point(sync, release->proxy, (uint32_t)(release_point >> 32), (uint32_t)release_point);
    wl_surface_attach(child, buffer, 0, 0);
    wl_surface_damage_buffer(child, 0, 0, CHILD, CHILD);
    wl_surface_commit(child);
}

int main(int argc, char **argv)
{
    int one_timeline = 0, late_acquire = 0, count = 3, timeout_ms = 1000;
    int settle = 0, hold_ms = 0, recommit_shown = 0, destroy_cached = 0;
    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--one-timeline") == 0)
            one_timeline = 1;
        else if (strcmp(argv[i], "--late-acquire") == 0)
            late_acquire = 1;
        else if (strcmp(argv[i], "--count") == 0 && i + 1 < argc)
            count = atoi(argv[++i]);
        else if (strcmp(argv[i], "--timeout-ms") == 0 && i + 1 < argc)
            timeout_ms = atoi(argv[++i]);
        else if (strcmp(argv[i], "--size") == 0 && i + 1 < argc) {
            CHILD = atoi(argv[++i]);
            PARENT = CHILD * 2;
        } else if (strcmp(argv[i], "--settle") == 0 && i + 1 < argc)
            settle = atoi(argv[++i]);
        else if (strcmp(argv[i], "--hold-ms") == 0 && i + 1 < argc)
            hold_ms = atoi(argv[++i]);
        else if (strcmp(argv[i], "--recommit-shown") == 0)
            recommit_shown = 1;
        else if (strcmp(argv[i], "--destroy-cached") == 0)
            destroy_cached = 1;
        else {
            fprintf(stderr, "usage: syncsub [--one-timeline] [--late-acquire] [--count N] [--timeout-ms MS] [--size N]\n"
                "               [--settle N] [--hold-ms MS] [--recommit-shown] [--destroy-cached]\n");
            return 2;
        }
    }
    if (recommit_shown && settle < 1) {
        fprintf(stderr, "syncsub: --recommit-shown needs --settle\n");
        return 2;
    }
    if (count < 2 || count >= MAX_BUFFERS) {
        fprintf(stderr, "syncsub: --count must be 2 to %d\n", MAX_BUFFERS - 1);
        return 2;
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "syncsub: no Wayland display\n");
        return 1;
    }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !subcompositor || !shm || !wm_base || !dmabuf) {
        fprintf(stderr, "syncsub: the compositor lacks a needed global\n");
        return 1;
    }
    if (!syncobj_manager) {
        printf("FAILED no wp_linux_drm_syncobj_manager_v1\n");
        return 1;
    }

    drm_fd = open("/dev/dri/renderD128", O_RDWR | O_CLOEXEC);
    struct gbm_device *gbm = drm_fd >= 0 ? gbm_create_device(drm_fd) : NULL;
    if (!gbm) {
        fprintf(stderr, "syncsub: cannot open /dev/dri/renderD128 through GBM\n");
        return 1;
    }

    struct wl_surface *parent = wl_compositor_create_surface(compositor);
    struct xdg_surface *xdg = xdg_wm_base_get_xdg_surface(wm_base, parent);
    xdg_surface_add_listener(xdg, &surface_listener, NULL);
    struct xdg_toplevel *top = xdg_surface_get_toplevel(xdg);
    xdg_toplevel_add_listener(top, &top_listener, NULL);
    xdg_toplevel_set_title(top, "syncsub");
    xdg_toplevel_set_app_id(top, "syncsub");
    wl_surface_commit(parent);
    while (!configured && wl_display_dispatch(display) >= 0) {
    }

    struct wl_surface *child = wl_compositor_create_surface(compositor);
    struct wl_subsurface *sub = wl_subcompositor_get_subsurface(subcompositor, child, parent);
    wl_subsurface_set_position(sub, CHILD / 2, CHILD / 2);
    struct wp_linux_drm_syncobj_surface_v1 *sync = wp_linux_drm_syncobj_manager_v1_get_surface(syncobj_manager, child);

    wl_surface_attach(parent, parent_buffer(), 0, 0);
    wl_surface_damage_buffer(parent, 0, 0, PARENT, PARENT);
    wl_surface_commit(parent);
    wl_display_roundtrip(display);

    static const uint32_t colors[] = { 0xFFCC0000, 0xFF00CC00, 0xFF0000CC, 0xFFCCCC00 };
    struct wl_buffer *buffers[MAX_BUFFERS];
    struct timeline acquire = make_timeline();
    struct timeline releases[MAX_BUFFERS];
    uint64_t points[MAX_BUFFERS];
    for (int i = 0; i <= count; i++) {
        buffers[i] = child_buffer(gbm, colors[i % 4]);
        if (!buffers[i]) {
            fprintf(stderr, "syncsub: GBM would not allocate a linear ARGB8888 buffer\n");
            return 1;
        }
        releases[i] = one_timeline && i > 0 ? releases[0] : make_timeline();
        points[i] = one_timeline ? (uint64_t)i + 1 : 1;
    }
    wl_display_roundtrip(display);

    uint64_t next_acquire = 1;
    struct wl_buffer *shown = NULL;
    struct timeline shown_release = { 0 };
    if (settle > 0) {
        shown = child_buffer(gbm, 0xFFFFFFFF);
        struct timeline settle_release = make_timeline();
        shown_release = make_timeline();
        for (int k = 0; k < settle; k++) {
            signal_point(&acquire, next_acquire);
            commit_child(child, sync, shown, &acquire, next_acquire, &settle_release, (uint64_t)k + 1);
            next_acquire++;
            frame_done = 0;
            wl_callback_add_listener(wl_surface_frame(parent), &frame_listener, NULL);
            wl_surface_damage_buffer(parent, 0, 0, 1, 1);
            wl_surface_commit(parent);
            while (!frame_done && wl_display_dispatch(display) >= 0) {
            }
        }
        printf("SETTLED %d\n", settle);
        fflush(stdout);
        if (hold_ms > 0)
            usleep((useconds_t)hold_ms * 1000);
        if (recommit_shown) {
            signal_point(&acquire, next_acquire);
            commit_child(child, sync, shown, &acquire, next_acquire, &shown_release, 1);
            next_acquire++;
        }
    }

    uint64_t first = next_acquire;
    for (int i = 0; i < count; i++) {
        if (!late_acquire)
            signal_point(&acquire, first + (uint64_t)i);
        commit_child(child, sync, buffers[i], &acquire, first + (uint64_t)i, &releases[i], points[i]);
    }
    wl_display_roundtrip(display);
    if (late_acquire) {
        signal_point(&acquire, first + (uint64_t)count - 1);
        wl_display_roundtrip(display);
    }

    int ok = 1;
    if (recommit_shown) {
        int early = wait_point(&shown_release, 1, 200);
        printf("SHOWN-RECOMMIT %s\n", early ? "signaled-early" : "held");
        ok &= !early;
    }
    for (int i = 0; i < count - 1; i++) {
        int signaled = wait_point(&releases[i], points[i], timeout_ms);
        printf("RELEASE %d %s\n", i + 1, signaled ? "signaled" : "timeout");
        ok &= signaled;
    }
    int early = wait_point(&releases[count - 1], points[count - 1], 50);
    printf("CACHED %d %s\n", count, early ? "signaled-early" : "held");
    ok &= !early;
    fflush(stdout);

    wl_surface_commit(parent);
    wl_display_roundtrip(display);
    signal_point(&acquire, first + (uint64_t)count);
    commit_child(child, sync, buffers[count], &acquire, first + (uint64_t)count, &releases[count], points[count]);
    wl_surface_commit(parent);
    wl_display_roundtrip(display);
    int replaced = wait_point(&releases[count - 1], points[count - 1], timeout_ms);
    printf("REPLACED %d %s\n", count, replaced ? "signaled" : "timeout");
    ok &= replaced;
    if (recommit_shown) {
        int released = wait_point(&shown_release, 1, timeout_ms);
        printf("SHOWN-RECOMMIT %s\n", released ? "released" : "timeout");
        ok &= released;
    }

    if (destroy_cached) {
        struct wl_buffer *last = child_buffer(gbm, 0xFF808080);
        struct timeline discarded = make_timeline();
        signal_point(&acquire, first + (uint64_t)count + 1);
        commit_child(child, sync, last, &acquire, first + (uint64_t)count + 1, &discarded, 1);
        wl_display_roundtrip(display);
        wp_linux_drm_syncobj_surface_v1_destroy(sync);
        wl_subsurface_destroy(sub);
        wl_surface_destroy(child);
        wl_display_roundtrip(display);
        int signaled = wait_point(&discarded, 1, timeout_ms);
        printf("DISCARDED %s\n", signaled ? "signaled" : "timeout");
        ok &= signaled;
    }

    printf("RESULT %s\n", ok ? "ok" : "stuck");
    fflush(stdout);
    wl_display_disconnect(display);
    return ok ? 0 : 3;
}
