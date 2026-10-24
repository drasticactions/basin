#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <poll.h>
#include <time.h>
#include <wayland-client.h>
#include "river-libinput-config-v1-client-protocol.h"

/* Bind river_libinput_config_v1 and print one line per device event that a
 * test needs: DEVICE ID when a device is announced, REMOVED ID when the
 * compositor sends river_libinput_device_v1.removed. A generic dispatcher
 * stands in for a listener, so the many support, default and current events
 * need no handler each. Runs for SECONDS, then exits. */

enum { CONFIG_EVENT_LIBINPUT_DEVICE = 1, DEVICE_EVENT_REMOVED = 0 };

static struct river_libinput_config_v1 *config;

static int device_dispatch(const void *impl, void *target, uint32_t opcode, const struct wl_message *msg, union wl_argument *args)
{
    (void)impl; (void)msg; (void)args;
    if (opcode == DEVICE_EVENT_REMOVED) {
        printf("REMOVED %u\n", wl_proxy_get_id(target));
        fflush(stdout);
    }
    return 0;
}

static int config_dispatch(const void *impl, void *target, uint32_t opcode, const struct wl_message *msg, union wl_argument *args)
{
    (void)impl; (void)target; (void)msg;
    if (opcode == CONFIG_EVENT_LIBINPUT_DEVICE) {
        struct wl_proxy *device = (struct wl_proxy *)args[0].o;
        wl_proxy_add_dispatcher(device, device_dispatch, NULL, NULL);
        printf("DEVICE %u\n", wl_proxy_get_id(device));
        fflush(stdout);
    }
    return 0;
}

static void global_add(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version)
{
    (void)data;
    if (strcmp(interface, river_libinput_config_v1_interface.name) == 0 && !config) {
        config = wl_registry_bind(registry, name, &river_libinput_config_v1_interface, version < 2 ? version : 2);
        wl_proxy_add_dispatcher((struct wl_proxy *)config, config_dispatch, NULL, NULL);
    }
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener registry_listener = { global_add, global_remove };

int main(int argc, char **argv)
{
    int seconds = argc > 1 ? atoi(argv[1]) : 30;
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) { fprintf(stderr, "no display\n"); return 1; }
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    wl_display_roundtrip(display);
    if (!config) { fprintf(stderr, "river_libinput_config_v1 is not advertised\n"); return 1; }

    struct timespec start, now;
    clock_gettime(CLOCK_MONOTONIC, &start);
    struct pollfd pfd = { .fd = wl_display_get_fd(display), .events = POLLIN };
    for (;;) {
        clock_gettime(CLOCK_MONOTONIC, &now);
        if (now.tv_sec - start.tv_sec >= seconds) break;
        wl_display_flush(display);
        if (poll(&pfd, 1, 200) > 0 && wl_display_dispatch(display) < 0) break;
    }
    wl_display_disconnect(display);
    return 0;
}
