/* textinput: one 256x256 toplevel with a text field that speaks zwp_text_input_v3.
   It enables text input on every enter, with the purpose and hints named in argv,
   keeps its own text and caret at the end, and reports what the compositor sends:
   COMMIT TEXT, PREEDIT TEXT CURSOR, DELETE BEFORE AFTER, TEXT [whole field], KEY CODE STATE,
   ENTER, LEAVE and CONFIGURE W H.
   caret=Y puts the cursor rectangle at Y in surface coordinates (default 10), and step=N moves it
   N pixels further down for each character in the field, so a test can watch a pan follow it.
   mark paints the field's band dark and the caret rectangle black, so a screenshot shows where they are.
   usage: textinput [normal|alpha|digits|number|phone|url|email|name|password|pin|date|time|datetime|terminal]
                    [completion|spellcheck|auto_capitalization|lowercase|uppercase|titlecase|hidden_text|sensitive_data|latin|multiline...]
                    [caret=Y] [step=N] [mark] */
#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/mman.h>
#include <wayland-client.h>
#include "xdg-shell-client-protocol.h"
#include "text-input-unstable-v3-client-protocol.h"

static struct wl_compositor *compositor;
static struct wl_shm *shm;
static struct xdg_wm_base *wm_base;
static struct wl_seat *seat;
static struct zwp_text_input_manager_v3 *ti_manager;
static struct zwp_text_input_v3 *text_input;
static struct wl_surface *surface;
static struct wl_keyboard *keyboard;
static int width = 256, height = 256;
static int running = 1;
static uint32_t purpose;
static uint32_t hints;
static int caret_y = 10;
static int caret_step;
static int mark;

static char text[4096];
static char *pending_commit;
static char *pending_preedit;
static int pending_preedit_cursor;
static uint32_t pending_before, pending_after;
static int pending_delete;

static void print_text(void)
{
    printf("TEXT %s\n", text);
    fflush(stdout);
}

static void send_state(void)
{
    if (!text_input) return;
    size_t len = strlen(text);
    zwp_text_input_v3_set_surrounding_text(text_input, text, (int32_t)len, (int32_t)len);
    zwp_text_input_v3_set_content_type(text_input, hints, purpose);
    zwp_text_input_v3_set_cursor_rectangle(text_input, 10, caret_y + caret_step * (int32_t)len, 1, 20);
    zwp_text_input_v3_commit(text_input);
}

static void delete_last_char(void)
{
    size_t len = strlen(text);
    while (len > 0) {
        len--;
        if ((text[len] & 0xC0) != 0x80) break;
    }
    text[len] = 0;
}

static struct wl_buffer *make_buffer(int w, int h)
{
    int stride = w * 4, size = stride * h;
    int fd = memfd_create("textinput", 0);
    if (fd < 0 || ftruncate(fd, size) < 0) return NULL;
    uint32_t *data = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (data == MAP_FAILED) { close(fd); return NULL; }
    for (int i = 0; i < w * h; i++) data[i] = 0xfff0f0e8;
    if (mark) {
        int top = caret_y + caret_step * (int)strlen(text);
        for (int y = top - 6; y < top + 26 && y < h; y++)
            for (int x = 0; x < w && y >= 0; x++)
                data[y * w + x] = (x >= 10 && x < 13 && y >= top && y < top + 20) ? 0xff000000 : 0xffc8c8c0;
    }
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

static void ti_enter(void *d, struct zwp_text_input_v3 *t, struct wl_surface *s)
{
    (void)d; (void)s;
    printf("ENTER\n");
    fflush(stdout);
    zwp_text_input_v3_enable(t);
    send_state();
}

static void ti_leave(void *d, struct zwp_text_input_v3 *t, struct wl_surface *s)
{
    (void)d; (void)s;
    printf("LEAVE\n");
    fflush(stdout);
    zwp_text_input_v3_disable(t);
    zwp_text_input_v3_commit(t);
}

static void ti_preedit(void *d, struct zwp_text_input_v3 *t, const char *s, int32_t begin, int32_t end)
{
    (void)d; (void)t; (void)end;
    free(pending_preedit);
    pending_preedit = s ? strdup(s) : strdup("");
    pending_preedit_cursor = begin;
}

static void ti_commit(void *d, struct zwp_text_input_v3 *t, const char *s)
{
    (void)d; (void)t;
    free(pending_commit);
    pending_commit = s ? strdup(s) : NULL;
}

static void ti_delete(void *d, struct zwp_text_input_v3 *t, uint32_t before, uint32_t after)
{
    (void)d; (void)t;
    pending_before = before;
    pending_after = after;
    pending_delete = 1;
}

static void ti_done(void *d, struct zwp_text_input_v3 *t, uint32_t serial)
{
    (void)d; (void)t; (void)serial;
    int changed = 0;
    if (pending_delete) {
        printf("DELETE %u %u\n", pending_before, pending_after);
        size_t len = strlen(text);
        size_t cut = pending_before > len ? len : pending_before;
        text[len - cut] = 0;
        pending_delete = 0;
        changed = 1;
    }
    if (pending_commit) {
        printf("COMMIT %s\n", pending_commit);
        strncat(text, pending_commit, sizeof(text) - strlen(text) - 1);
        free(pending_commit);
        pending_commit = NULL;
        changed = 1;
    }
    if (pending_preedit) {
        printf("PREEDIT %s %d\n", pending_preedit, pending_preedit_cursor);
        free(pending_preedit);
        pending_preedit = NULL;
    }
    fflush(stdout);
    if (changed) {
        print_text();
        send_state();
    }
}

static const struct zwp_text_input_v3_listener ti_listener = {
    .enter = ti_enter, .leave = ti_leave, .preedit_string = ti_preedit,
    .commit_string = ti_commit, .delete_surrounding_text = ti_delete, .done = ti_done,
};

static void kb_keymap(void *d, struct wl_keyboard *k, uint32_t f, int32_t fd, uint32_t size)
{
    (void)d; (void)k; (void)f; (void)size;
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

static void kb_key(void *d, struct wl_keyboard *k, uint32_t serial, uint32_t time, uint32_t key, uint32_t state)
{
    (void)d; (void)k; (void)serial; (void)time;
    printf("KEY %u %u\n", key, state);
    fflush(stdout);
    if (state != WL_KEYBOARD_KEY_STATE_PRESSED) return;
    if (key == 14) {
        delete_last_char();
        print_text();
        send_state();
    } else if (key == 28) {
        strncat(text, "\n", sizeof(text) - strlen(text) - 1);
        print_text();
        send_state();
    }
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

static void seat_capabilities(void *data, struct wl_seat *s, uint32_t caps)
{
    (void)data;
    if ((caps & WL_SEAT_CAPABILITY_KEYBOARD) && !keyboard) {
        keyboard = wl_seat_get_keyboard(s);
        wl_keyboard_add_listener(keyboard, &keyboard_listener, NULL);
    }
}

static void seat_name(void *data, struct wl_seat *s, const char *name) { (void)data; (void)s; (void)name; }

static const struct wl_seat_listener seat_listener = { seat_capabilities, seat_name };

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
    if (w > 0 && h > 0 && (w != width || h != height)) {
        printf("CONFIGURE %d %d\n", w, h);
        fflush(stdout);
        width = w;
        height = h;
    }
}

static void toplevel_close(void *data, struct xdg_toplevel *t) { (void)data; (void)t; running = 0; }
static void toplevel_bounds(void *data, struct xdg_toplevel *t, int32_t w, int32_t h) { (void)data; (void)t; (void)w; (void)h; }
static void toplevel_caps(void *data, struct xdg_toplevel *t, struct wl_array *caps) { (void)data; (void)t; (void)caps; }

static const struct xdg_toplevel_listener toplevel_listener = {
    toplevel_configure, toplevel_close, toplevel_bounds, toplevel_caps
};

static void wm_ping(void *data, struct xdg_wm_base *base, uint32_t serial) { (void)data; xdg_wm_base_pong(base, serial); }

static const struct xdg_wm_base_listener wm_listener = { wm_ping };

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data;
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        compositor = wl_registry_bind(registry, name, &wl_compositor_interface, 4);
    else if (strcmp(interface, wl_shm_interface.name) == 0)
        shm = wl_registry_bind(registry, name, &wl_shm_interface, 1);
    else if (strcmp(interface, xdg_wm_base_interface.name) == 0)
        wm_base = wl_registry_bind(registry, name, &xdg_wm_base_interface, version < 5 ? version : 5);
    else if (strcmp(interface, zwp_text_input_manager_v3_interface.name) == 0)
        ti_manager = wl_registry_bind(registry, name, &zwp_text_input_manager_v3_interface, 1);
    else if (strcmp(interface, wl_seat_interface.name) == 0 && !seat) {
        seat = wl_registry_bind(registry, name, &wl_seat_interface, 1);
        wl_seat_add_listener(seat, &seat_listener, NULL);
    }
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

static const char *purposes[] = {
    "normal", "alpha", "digits", "number", "phone", "url", "email", "name",
    "password", "pin", "date", "time", "datetime", "terminal", NULL
};

static const char *hint_names[] = {
    "completion", "spellcheck", "auto_capitalization", "lowercase", "uppercase",
    "titlecase", "hidden_text", "sensitive_data", "latin", "multiline", NULL
};

int main(int argc, char **argv)
{
    for (int i = 1; i < argc; i++) {
        int matched = 0;
        if (strncmp(argv[i], "caret=", 6) == 0) { caret_y = atoi(argv[i] + 6); continue; }
        if (strncmp(argv[i], "step=", 5) == 0) { caret_step = atoi(argv[i] + 5); continue; }
        if (strcmp(argv[i], "mark") == 0) { mark = 1; continue; }
        for (int p = 0; purposes[p]; p++)
            if (strcmp(argv[i], purposes[p]) == 0) { purpose = (uint32_t)p; matched = 1; }
        for (int h = 0; hint_names[h]; h++)
            if (strcmp(argv[i], hint_names[h]) == 0) { hints |= 1u << h; matched = 1; }
        if (!matched) { fprintf(stderr, "unknown purpose or hint: %s\n", argv[i]); return 2; }
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!compositor || !shm || !wm_base || !seat) { fprintf(stderr, "missing globals\n"); return 1; }
    if (!ti_manager) { printf("TEXTINPUT unavailable\n"); fflush(stdout); }
    xdg_wm_base_add_listener(wm_base, &wm_listener, NULL);

    surface = wl_compositor_create_surface(compositor);
    struct xdg_surface *xdg_surface = xdg_wm_base_get_xdg_surface(wm_base, surface);
    xdg_surface_add_listener(xdg_surface, &xdg_surface_listener, NULL);
    struct xdg_toplevel *toplevel = xdg_surface_get_toplevel(xdg_surface);
    xdg_toplevel_add_listener(toplevel, &toplevel_listener, NULL);
    xdg_toplevel_set_title(toplevel, "textinput");
    xdg_toplevel_set_app_id(toplevel, "textinput");
    if (ti_manager) {
        text_input = zwp_text_input_manager_v3_get_text_input(ti_manager, seat);
        zwp_text_input_v3_add_listener(text_input, &ti_listener, NULL);
    }
    wl_surface_commit(surface);

    while (running && wl_display_dispatch(display) != -1) {
    }

    return 0;
}
