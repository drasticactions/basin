// actionbind KEYSYM... [one_shot] [no_block] [SECONDS]: create one
// gamescope_action_binding with one keyboard trigger made of the named keysyms
// (xkb names, for example Super_L a), arm it with the flags given, and print
// READY once armed and TRIGGERED SEQ FLAGS for every trigger.
#define _GNU_SOURCE
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <wayland-client.h>
#include <xkbcommon/xkbcommon.h>
#include "gamescope-action-binding-client-protocol.h"

static struct gamescope_action_binding_manager *manager;

static void on_triggered(void *d, struct gamescope_action_binding *b, uint32_t sequence,
                         uint32_t time_lo, uint32_t time_hi, uint32_t flags)
{
    (void)d; (void)b; (void)time_lo; (void)time_hi;
    printf("TRIGGERED %u %u\n", sequence, flags);
    fflush(stdout);
}

static const struct gamescope_action_binding_listener binding_listener = { .triggered = on_triggered };

static void on_global(void *d, struct wl_registry *r, uint32_t name, const char *interface, uint32_t version)
{
    (void)d; (void)version;
    if (!strcmp(interface, gamescope_action_binding_manager_interface.name))
        manager = wl_registry_bind(r, name, &gamescope_action_binding_manager_interface, 1);
}

static void on_global_remove(void *d, struct wl_registry *r, uint32_t name)
{
    (void)d; (void)r; (void)name;
}

static const struct wl_registry_listener registry_listener = { .global = on_global, .global_remove = on_global_remove };

int main(int argc, char **argv)
{
    uint32_t flags = 0;
    double seconds = 10;
    struct wl_array keysyms;
    wl_array_init(&keysyms);
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "one_shot")) {
            flags |= GAMESCOPE_ACTION_BINDING_ARM_FLAG_ONE_SHOT;
        } else if (!strcmp(argv[i], "no_block")) {
            flags |= GAMESCOPE_ACTION_BINDING_ARM_FLAG_NO_BLOCK;
        } else {
            xkb_keysym_t sym = xkb_keysym_from_name(argv[i], XKB_KEYSYM_NO_FLAGS);
            if (sym == XKB_KEY_NoSymbol) {
                char *end;
                double value = strtod(argv[i], &end);
                if (*end == '\0') {
                    seconds = value;
                    continue;
                }

                fprintf(stderr, "actionbind: unknown keysym %s\n", argv[i]);
                return 2;
            }

            *(uint32_t *)wl_array_add(&keysyms, sizeof(uint32_t)) = sym;
        }
    }

    if (keysyms.size == 0) {
        fprintf(stderr, "usage: actionbind KEYSYM... [one_shot] [no_block] [SECONDS]\n");
        return 2;
    }

    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        fprintf(stderr, "actionbind: no display\n");
        return 1;
    }

    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!manager) {
        printf("NO gamescope_action_binding_manager\n");
        return 3;
    }

    struct gamescope_action_binding *binding = gamescope_action_binding_manager_create_action_binding(manager);
    gamescope_action_binding_add_listener(binding, &binding_listener, NULL);
    gamescope_action_binding_set_description(binding, "actionbind");
    gamescope_action_binding_add_keyboard_trigger(binding, &keysyms);
    gamescope_action_binding_arm(binding, flags);
    wl_display_roundtrip(display);
    printf("READY %u\n", flags);
    fflush(stdout);

    struct timespec start, now;
    clock_gettime(CLOCK_MONOTONIC, &start);
    for (;;) {
        clock_gettime(CLOCK_MONOTONIC, &now);
        if ((now.tv_sec - start.tv_sec) + (now.tv_nsec - start.tv_nsec) / 1e9 >= seconds)
            break;
        if (wl_display_roundtrip(display) == -1)
            break;
        struct timespec pause = { 0, 20 * 1000 * 1000 };
        nanosleep(&pause, NULL);
    }

    wl_array_release(&keysyms);
    wl_display_disconnect(display);
    return 0;
}
