#define _GNU_SOURCE
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wayland-client.h>
#include "plasma-window-management-client-protocol.h"

struct win {
    struct org_kde_plasma_window *window;
    char uuid[64];
    char title[256];
    char app_id[256];
    char resource_name[256];
    char menu_service[256];
    char menu_path[256];
    uint32_t state;
    uint32_t pid;
    int32_t x, y;
    uint32_t w, h;
    int32_t cx, cy;
    uint32_t cw, ch;
    int has_parent;
    int unmapped;
};

static struct org_kde_plasma_window_management *management;
static uint32_t bound_version;
static struct win *wins[128];
static int nwins;
static int order_done;
static char *set_uuid;
static uint32_t set_flag;
static int set_value = -1;
static int watch;

static const struct { uint32_t bit; const char *name; } state_names[] = {
    { 1, "active" }, { 2, "minimized" }, { 4, "maximized" }, { 8, "fullscreen" },
    { 16, "keep_above" }, { 32, "keep_below" }, { 64, "on_all_desktops" },
    { 128, "demands_attention" }, { 256, "closeable" }, { 512, "minimizable" },
    { 1024, "maximizable" }, { 2048, "fullscreenable" }, { 4096, "skiptaskbar" },
    { 8192, "shadeable" }, { 16384, "shaded" }, { 32768, "movable" },
    { 65536, "resizable" }, { 131072, "virtual_desktop_changeable" },
    { 262144, "skipswitcher" }, { 524288, "no_border" },
    { 1048576, "can_set_no_border" }, { 2097152, "exclude_from_capture" },
};

static void print_state(uint32_t state)
{
    int first = 1;
    for (size_t i = 0; i < sizeof(state_names) / sizeof(state_names[0]); i++) {
        if (state & state_names[i].bit) {
            printf("%s%s", first ? "" : "|", state_names[i].name);
            first = 0;
        }
    }
    if (first)
        printf("none");
}

static void on_title(void *data, struct org_kde_plasma_window *w, const char *title)
{
    (void)w;
    snprintf(((struct win *)data)->title, sizeof(((struct win *)data)->title), "%s", title);
}

static void on_app_id(void *data, struct org_kde_plasma_window *w, const char *app_id)
{
    (void)w;
    snprintf(((struct win *)data)->app_id, sizeof(((struct win *)data)->app_id), "%s", app_id);
}

static void on_state(void *data, struct org_kde_plasma_window *w, uint32_t flags)
{
    (void)w;
    ((struct win *)data)->state = flags;
    if (watch) {
        printf("EVENT state %s ", ((struct win *)data)->uuid);
        print_state(flags);
        printf("\n");
        fflush(stdout);
    }
}

static void on_geometry(void *data, struct org_kde_plasma_window *w,
    int32_t x, int32_t y, uint32_t width, uint32_t height)
{
    (void)w;
    struct win *win = data;
    win->x = x; win->y = y; win->w = width; win->h = height;
}

static void on_client_geometry(void *data, struct org_kde_plasma_window *w,
    int32_t x, int32_t y, uint32_t width, uint32_t height)
{
    (void)w;
    struct win *win = data;
    win->cx = x; win->cy = y; win->cw = width; win->ch = height;
}

static void on_resource_name(void *data, struct org_kde_plasma_window *w, const char *name)
{
    (void)w;
    snprintf(((struct win *)data)->resource_name,
        sizeof(((struct win *)data)->resource_name), "%s", name);
}

static void on_application_menu(void *data, struct org_kde_plasma_window *w,
    const char *service_name, const char *object_path)
{
    (void)w;
    snprintf(((struct win *)data)->menu_service,
        sizeof(((struct win *)data)->menu_service), "%s", service_name);
    snprintf(((struct win *)data)->menu_path,
        sizeof(((struct win *)data)->menu_path), "%s", object_path);
}

static void on_pid(void *data, struct org_kde_plasma_window *w, uint32_t pid)
{
    (void)w;
    ((struct win *)data)->pid = pid;
}

static void on_parent(void *data, struct org_kde_plasma_window *w,
    struct org_kde_plasma_window *parent)
{
    (void)w;
    ((struct win *)data)->has_parent = parent != NULL;
}

static void on_unmapped(void *data, struct org_kde_plasma_window *w)
{
    (void)w;
    ((struct win *)data)->unmapped = 1;
}

static void ignore(void) { }

static const struct org_kde_plasma_window_listener window_listener = {
    .title_changed = on_title,
    .app_id_changed = on_app_id,
    .state_changed = on_state,
    .virtual_desktop_changed = (void *)ignore,
    .themed_icon_name_changed = (void *)ignore,
    .initial_state = (void *)ignore,
    .parent_window = on_parent,
    .geometry = on_geometry,
    .icon_changed = (void *)ignore,
    .pid_changed = on_pid,
    .virtual_desktop_entered = (void *)ignore,
    .virtual_desktop_left = (void *)ignore,
    .application_menu = on_application_menu,
    .activity_entered = (void *)ignore,
    .activity_left = (void *)ignore,
    .resource_name_changed = on_resource_name,
    .client_geometry = on_client_geometry,
    .unmapped = on_unmapped,
};

static struct win *wire_window(const char *uuid)
{
    if (nwins >= 128)
        return NULL;
    struct win *win = calloc(1, sizeof(*win));
    snprintf(win->uuid, sizeof(win->uuid), "%s", uuid);
    win->window = org_kde_plasma_window_management_get_window_by_uuid(management, uuid);
    org_kde_plasma_window_add_listener(win->window, &window_listener, win);
    wins[nwins++] = win;
    return win;
}

static void on_window_with_uuid(void *data, struct org_kde_plasma_window_management *m,
    uint32_t id, const char *uuid)
{
    (void)data; (void)m; (void)id;
    wire_window(uuid);
}

static void on_stacking_changed_2(void *data, struct org_kde_plasma_window_management *m)
{
    (void)data; (void)m;
    if (watch) {
        printf("EVENT stacking_order_changed_2\n");
        fflush(stdout);
    }
}

static void on_stacking_uuids(void *data, struct org_kde_plasma_window_management *m,
    const char *uuids)
{
    (void)data; (void)m;
    printf("EVENT stacking_order_uuid_changed %s\n", uuids);
    fflush(stdout);
}

static const struct org_kde_plasma_window_management_listener management_listener = {
    .show_desktop_changed = (void *)ignore,
    .window = (void *)ignore,
    .stacking_order_changed = (void *)ignore,
    .stacking_order_uuid_changed = on_stacking_uuids,
    .window_with_uuid = on_window_with_uuid,
    .stacking_order_changed_2 = on_stacking_changed_2,
};

static void on_order_window(void *data, struct org_kde_plasma_stacking_order *o, const char *uuid)
{
    (void)data; (void)o;
    printf("STACK %s\n", uuid);
}

static void on_order_done(void *data, struct org_kde_plasma_stacking_order *o)
{
    (void)data; (void)o;
    order_done = 1;
}

static const struct org_kde_plasma_stacking_order_listener order_listener = {
    .window = on_order_window,
    .done = on_order_done,
};

static void on_global(void *data, struct wl_registry *registry, uint32_t name,
    const char *interface, uint32_t version)
{
    (void)data;
    if (strcmp(interface, org_kde_plasma_window_management_interface.name) == 0) {
        bound_version = version < 20 ? version : 20;
        management = wl_registry_bind(
            registry, name, &org_kde_plasma_window_management_interface, bound_version);
        org_kde_plasma_window_management_add_listener(management, &management_listener, NULL);
    }
}

static void on_global_remove(void *data, struct wl_registry *registry, uint32_t name)
{
    (void)data; (void)registry; (void)name;
}

static const struct wl_registry_listener registry_listener = { on_global, on_global_remove };

int main(int argc, char **argv)
{
    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--watch") == 0) {
            watch = 1;
        } else if (strcmp(argv[i], "--set-state") == 0 && i + 3 < argc) {
            set_uuid = argv[++i];
            const char *flag = argv[++i];
            if (strcmp(flag, "no-border") == 0)
                set_flag = 524288;
            else if (strcmp(flag, "exclude-from-capture") == 0)
                set_flag = 2097152;
            else if (strcmp(flag, "minimized") == 0)
                set_flag = 2;
            else {
                fprintf(stderr, "unknown flag %s\n", flag);
                return 1;
            }
            set_value = atoi(argv[++i]);
        } else {
            fprintf(stderr,
                "usage: plasmawins [--watch] [--set-state UUID no-border|exclude-from-capture|minimized 0|1]\n");
            return 1;
        }
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "cannot connect to a wayland display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!management) {
        fprintf(stderr, "org_kde_plasma_window_management is not advertised (privileged?)\n");
        return 1;
    }

    printf("BOUND org_kde_plasma_window_management version %u\n", bound_version);
    wl_display_roundtrip(display);
    wl_display_roundtrip(display);

    for (int i = 0; i < nwins; i++) {
        struct win *w = wins[i];
        if (w->unmapped)
            continue;
        printf("WINDOW %s\n", w->uuid);
        printf("  title \"%s\" app_id \"%s\"\n", w->title, w->app_id);
        printf("  state ");
        print_state(w->state);
        printf("\n");
        printf("  geometry %d,%d %ux%u client %d,%d %ux%u\n",
            w->x, w->y, w->w, w->h, w->cx, w->cy, w->cw, w->ch);
        printf("  resource_name \"%s\" pid %u parent %s\n",
            w->resource_name, w->pid, w->has_parent ? "yes" : "no");
        printf("  appmenu \"%s\" \"%s\"\n", w->menu_service, w->menu_path);
    }

    if (bound_version >= 17) {
        struct org_kde_plasma_stacking_order *order =
            org_kde_plasma_window_management_get_stacking_order(management);
        org_kde_plasma_stacking_order_add_listener(order, &order_listener, NULL);
        while (!order_done && wl_display_dispatch(display) != -1)
            ;
        org_kde_plasma_stacking_order_destroy(order);
    }

    if (set_uuid && set_value >= 0) {
        for (int i = 0; i < nwins; i++) {
            if (strcmp(wins[i]->uuid, set_uuid) == 0) {
                org_kde_plasma_window_set_state(
                    wins[i]->window, set_flag, set_value ? set_flag : 0);
                printf("SET %s %u -> %d\n", set_uuid, set_flag, set_value);
                break;
            }
        }
        wl_display_roundtrip(display);
    }

    fflush(stdout);
    while (watch && wl_display_dispatch(display) != -1)
        ;

    wl_display_disconnect(display);
    return 0;
}
