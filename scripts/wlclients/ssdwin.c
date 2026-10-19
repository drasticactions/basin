#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "xdg-decoration-unstable-v1-client-protocol.h"
#include "fractional-scale-v1-client-protocol.h"
#include "viewporter-client-protocol.h"
#include "xdg-dialog-v1-client-protocol.h"

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct zxdg_decoration_manager_v1 *decoration_manager;
static struct wl_seat *seat;
static struct wl_pointer *pointer;
static struct wl_keyboard *keyboard;
static double pointer_x, pointer_y;
static struct wl_surface *surface;
static struct xdg_surface *xdg_surface;
static struct xdg_toplevel *toplevel;
static struct zxdg_toplevel_decoration_v1 *decoration;
static int width = 256, height = 256;
static int acked;
static int running = 1;
static int move_on_press;
static uint32_t colour = 0xFF3C7A9E;
static uint32_t wanted = ZXDG_TOPLEVEL_DECORATION_V1_MODE_SERVER_SIDE;
static struct wp_fractional_scale_manager_v1 *fractional_manager;
static struct wp_viewporter *viewporter;
static struct wp_viewport *viewport;
static struct xdg_wm_dialog_v1 *dialog_manager;
/* pixel mode: the buffer stays 256 pixels and the logical size follows the preferred scale, ignoring configure sizes, like a client that sizes its window in pixels. */
static int pixel_mode;
static uint32_t pixel_scale = 120;

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
    int buffer_width = pixel_mode ? 256 : width;
    int buffer_height = pixel_mode ? 256 : height;
    struct wl_buffer *buf = make_buffer(buffer_width, buffer_height);
    if (!buf) return;
    if (pixel_mode && viewport) {
        width = (int)(256 * 120 / pixel_scale);
        height = width;
        wp_viewport_set_destination(viewport, width, height);
        printf("SIZE %d %d\n", width, height);
        fflush(stdout);
    }
    wl_surface_attach(surface, buf, 0, 0);
    wl_surface_damage_buffer(surface, 0, 0, buffer_width, buffer_height);
    wl_surface_commit(surface);
}

static void preferred_scale(void *data, struct wp_fractional_scale_v1 *f, uint32_t scale)
{
    (void)data; (void)f;
    if (scale == pixel_scale) return;
    pixel_scale = scale;
    /* a preferred scale can arrive before the first configure, and xdg-shell refuses a buffer until that is acked */
    if (acked) draw();
}

static const struct wp_fractional_scale_v1_listener fractional_listener = { preferred_scale };

static void xdg_surface_configure(void *data, struct xdg_surface *s, uint32_t serial)
{
    (void)data;
    xdg_surface_ack_configure(s, serial);
    acked = 1;
    draw();
}

static const struct xdg_surface_listener xdg_surface_listener = { xdg_surface_configure };

static struct wl_surface *child_surface;

static void child_configure(void *data, struct xdg_surface *s, uint32_t serial)
{
    (void)data;
    xdg_surface_ack_configure(s, serial);
    struct wl_buffer *buf = make_buffer(160, 100);
    if (!buf) return;
    wl_surface_attach(child_surface, buf, 0, 0);
    wl_surface_damage_buffer(child_surface, 0, 0, 160, 100);
    wl_surface_commit(child_surface);
}

static const struct xdg_surface_listener child_listener = { child_configure };

static int suspended = -1;

static void toplevel_configure(void *data, struct xdg_toplevel *t, int32_t w, int32_t h, struct wl_array *states)
{
    (void)data; (void)t;
    int now = 0;
    uint32_t *state;
    wl_array_for_each(state, states)
        if (*state == XDG_TOPLEVEL_STATE_SUSPENDED) now = 1;
    if (now != suspended && (suspended >= 0 || now)) {
        printf("SUSPENDED %d\n", now);
        fflush(stdout);
    }
    suspended = now;
    if (w > 0 && h > 0 && (w != width || h != height)) {
        printf("CONFIGURE %d %d\n", w, h);
        fflush(stdout);
    }
    if (pixel_mode) return;
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

static void pointer_enter(void *data, struct wl_pointer *p, uint32_t serial,
                          struct wl_surface *s, wl_fixed_t x, wl_fixed_t y)
{
    (void)data; (void)p; (void)serial; (void)s;
    pointer_x = wl_fixed_to_double(x);
    pointer_y = wl_fixed_to_double(y);
}

static void pointer_leave(void *data, struct wl_pointer *p, uint32_t serial, struct wl_surface *s)
{
    (void)data; (void)p; (void)serial; (void)s;
}

static void pointer_motion(void *data, struct wl_pointer *p, uint32_t t, wl_fixed_t x, wl_fixed_t y)
{
    (void)data; (void)p; (void)t;
    pointer_x = wl_fixed_to_double(x);
    pointer_y = wl_fixed_to_double(y);
}

/* Print where a press landed in surface coordinates, so a test can check the compositor's hit mapping.
   With "move", a press also asks for an interactive move, the way a client lets its content drag the window. */
static void pointer_button(void *data, struct wl_pointer *p, uint32_t serial,
                           uint32_t t, uint32_t button, uint32_t state)
{
    (void)data; (void)p; (void)t;
    if (state == WL_POINTER_BUTTON_STATE_PRESSED) {
        printf("BUTTON %u %.2f %.2f\n", button, pointer_x, pointer_y);
        fflush(stdout);
        if (move_on_press && toplevel && seat) {
            xdg_toplevel_move(toplevel, seat, serial);
            printf("MOVE requested\n");
            fflush(stdout);
        }
    }
}

static void pointer_axis(void *data, struct wl_pointer *p, uint32_t t, uint32_t a, wl_fixed_t v)
{
    (void)data; (void)p; (void)t;
    printf("AXIS %s %.2f\n", a == WL_POINTER_AXIS_HORIZONTAL_SCROLL ? "horizontal" : "vertical",
           wl_fixed_to_double(v));
    fflush(stdout);
}

static const struct wl_pointer_listener pointer_listener = {
    .enter = pointer_enter, .leave = pointer_leave, .motion = pointer_motion,
    .button = pointer_button, .axis = pointer_axis,
};

static void kb_keymap(void *d, struct wl_keyboard *k, uint32_t format, int32_t fd, uint32_t size)
{
    (void)d; (void)k; (void)format; (void)size;
    close(fd);
}

static void kb_enter(void *d, struct wl_keyboard *k, uint32_t serial, struct wl_surface *s, struct wl_array *keys)
{
    (void)d; (void)k; (void)serial; (void)s; (void)keys;
}

static void kb_leave(void *d, struct wl_keyboard *k, uint32_t serial, struct wl_surface *s)
{
    (void)d; (void)k; (void)serial; (void)s;
}

/* Print every key the client is given, so a test can check a key the compositor kept for itself never arrives. */
static void kb_key(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t time, uint32_t key, uint32_t state)
{
    (void)d; (void)k; (void)serial; (void)time;
    printf("KEY %u %u\n", key, state);
    fflush(stdout);
}

/* Print the depressed modifier mask, so a test can see a chord sent as modifier state rather than as key presses. */
static void kb_modifiers(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t dep, uint32_t lat, uint32_t lock, uint32_t group)
{
    (void)d; (void)k; (void)serial; (void)lat; (void)lock; (void)group;
    printf("MODIFIERS %u\n", dep);
    fflush(stdout);
}

static void kb_repeat(void *d, struct wl_keyboard *k, int32_t rate, int32_t delay)
{
    (void)d; (void)k; (void)rate; (void)delay;
}

static const struct wl_keyboard_listener keyboard_listener = { kb_keymap, kb_enter, kb_leave, kb_key, kb_modifiers, kb_repeat };

static void seat_capabilities(void *data, struct wl_seat *s, uint32_t caps)
{
    (void)data;
    if ((caps & WL_SEAT_CAPABILITY_POINTER) && !pointer) {
        pointer = wl_seat_get_pointer(s);
        wl_pointer_add_listener(pointer, &pointer_listener, NULL);
    }

    if ((caps & WL_SEAT_CAPABILITY_KEYBOARD) && !keyboard) {
        keyboard = wl_seat_get_keyboard(s);
        wl_keyboard_add_listener(keyboard, &keyboard_listener, NULL);
    }
}

static void seat_name(void *data, struct wl_seat *s, const char *name) { (void)data; (void)s; (void)name; }

static const struct wl_seat_listener seat_listener = { seat_capabilities, seat_name };

static void wm_ping(void *data, struct xdg_wm_base *base, uint32_t serial)
{
    (void)data;
    xdg_wm_base_pong(base, serial);
}

static const struct xdg_wm_base_listener wm_listener = { wm_ping };

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data;
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(registry, name, &wl_compositor_interface, 4);
    else if (strcmp(interface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(registry, name, &wl_shm_interface, 1);
    else if (strcmp(interface, xdg_wm_base_interface.name) == 0)
        wm_base = wl_registry_bind(registry, name, &xdg_wm_base_interface, version < 6 ? version : 6);
    else if (strcmp(interface, zxdg_decoration_manager_v1_interface.name) == 0)
        decoration_manager = wl_registry_bind(registry, name, &zxdg_decoration_manager_v1_interface, 1);
    else if (strcmp(interface, wp_fractional_scale_manager_v1_interface.name) == 0)
        fractional_manager = wl_registry_bind(registry, name, &wp_fractional_scale_manager_v1_interface, 1);
    else if (strcmp(interface, wp_viewporter_interface.name) == 0)
        viewporter = wl_registry_bind(registry, name, &wp_viewporter_interface, 1);
    else if (strcmp(interface, xdg_wm_dialog_v1_interface.name) == 0)
        dialog_manager = wl_registry_bind(registry, name, &xdg_wm_dialog_v1_interface, 1);
    else if (strcmp(interface, wl_seat_interface.name) == 0 && !seat) {
        seat = wl_registry_bind(registry, name, &wl_seat_interface, 1);
        wl_seat_add_listener(seat, &seat_listener, NULL);
    }
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

int main(int argc, char **argv)
{
    if (argc > 1 && strcmp(argv[1], "client") == 0) wanted = ZXDG_TOPLEVEL_DECORATION_V1_MODE_CLIENT_SIDE;
    if (argc > 1 && strcmp(argv[1], "none") == 0) wanted = 0;
    int fullscreen = 0;
    for (int i = 1; i < argc; i++) if (strcmp(argv[i], "fullscreen") == 0) fullscreen = 1;
    for (int i = 1; i < argc; i++) if (strcmp(argv[i], "pixel") == 0) pixel_mode = 1;
    for (int i = 1; i < argc; i++) if (strcmp(argv[i], "move") == 0) move_on_press = 1;

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
    if (pixel_mode && fractional_manager && viewporter) {
        viewport = wp_viewporter_get_viewport(viewporter, surface);
        struct wp_fractional_scale_v1 *fractional = wp_fractional_scale_manager_v1_get_fractional_scale(fractional_manager, surface);
        wp_fractional_scale_v1_add_listener(fractional, &fractional_listener, NULL);
    }
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

    /* modal: a 160x100 fixed-size child titled dialog, made modal through xdg-dialog once the parent maps. */
    int modal = 0;
    for (int i = 1; i < argc; i++) if (strcmp(argv[i], "modal") == 0) modal = 1;
    if (modal) {
        wl_display_roundtrip(display);
        wl_display_roundtrip(display);
        struct wl_surface *child = wl_compositor_create_surface(compositor);
        child_surface = child;
        struct xdg_surface *child_xdg = xdg_wm_base_get_xdg_surface(wm_base, child);
        xdg_surface_add_listener(child_xdg, &child_listener, NULL);
        struct xdg_toplevel *child_top = xdg_surface_get_toplevel(child_xdg);
        xdg_toplevel_set_parent(child_top, toplevel);
        xdg_toplevel_set_title(child_top, "dialog");
        xdg_toplevel_set_app_id(child_top, "ssdwin");
        xdg_toplevel_set_min_size(child_top, 160, 100);
        xdg_toplevel_set_max_size(child_top, 160, 100);
        if (dialog_manager) {
            struct xdg_dialog_v1 *dialog = xdg_wm_dialog_v1_get_xdg_dialog(dialog_manager, child_top);
            xdg_dialog_v1_set_modal(dialog);
        }
        wl_surface_commit(child);
        printf("MODAL %s\n", dialog_manager ? "yes" : "unavailable");
        fflush(stdout);
    }

    while (running && wl_display_dispatch(display) != -1) {
    }

    return 0;
}
