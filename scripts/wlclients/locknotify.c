#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <wayland-client.h>
#include "hyprland-lock-notify-v1-client-protocol.h"

static struct hyprland_lock_notifier_v1 *notifier;

static void locked(void *d, struct hyprland_lock_notification_v1 *n)
{
    (void)d; (void)n;
    printf("locknotify: locked\n");
    fflush(stdout);
}

static void unlocked(void *d, struct hyprland_lock_notification_v1 *n)
{
    (void)d; (void)n;
    printf("locknotify: unlocked\n");
    fflush(stdout);
}

static const struct hyprland_lock_notification_v1_listener listener = { .locked = locked, .unlocked = unlocked };

static void registry_global(void *d, struct wl_registry *r, uint32_t name, const char *iface, uint32_t version)
{
    (void)d; (void)version;
    if (!strcmp(iface, hyprland_lock_notifier_v1_interface.name)) {
        notifier = wl_registry_bind(r, name, &hyprland_lock_notifier_v1_interface, 1);
    }
}

static void registry_remove(void *d, struct wl_registry *r, uint32_t name)
{ (void)d; (void)r; (void)name; }

static const struct wl_registry_listener registry_listener = {
    .global = registry_global, .global_remove = registry_remove,
};

int main(int argc, char **argv)
{
    long seconds = argc > 1 ? atol(argv[1]) : 0;
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "locknotify: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!notifier) {
        fprintf(stderr, "locknotify: no hyprland_lock_notifier_v1\n");
        return 1;
    }

    struct hyprland_lock_notification_v1 *notification = hyprland_lock_notifier_v1_get_lock_notification(notifier);
    hyprland_lock_notification_v1_add_listener(notification, &listener, NULL);
    wl_display_roundtrip(display);
    printf("locknotify: listening\n");
    fflush(stdout);

    time_t deadline = seconds > 0 ? time(NULL) + seconds : 0;
    while (wl_display_dispatch(display) != -1) {
        if (deadline && time(NULL) >= deadline) {
            break;
        }
    }

    printf("locknotify: done\n");
    return 0;
}
