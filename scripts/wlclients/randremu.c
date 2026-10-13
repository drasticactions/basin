#define _GNU_SOURCE
#include <poll.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <xcb/randr.h>
#include <xcb/xcb.h>

static xcb_atom_t intern(xcb_connection_t *c, const char *name)
{
    xcb_intern_atom_reply_t *reply = xcb_intern_atom_reply(c, xcb_intern_atom(c, 0, strlen(name), name), NULL);
    xcb_atom_t atom = reply ? reply->atom : XCB_ATOM_NONE;
    free(reply);
    return atom;
}

static void paint(xcb_connection_t *c, xcb_window_t window, xcb_gcontext_t light, xcb_gcontext_t dark, int w, int h)
{
    for (int y = 0; y < h; y += 64) {
        for (int x = 0; x < w; x += 64) {
            xcb_rectangle_t cell = { (int16_t)x, (int16_t)y, 64, 64 };
            xcb_poly_fill_rectangle(c, window, ((x / 64) + (y / 64)) % 2 ? dark : light, 1, &cell);
        }
    }
    xcb_flush(c);
}

int main(int argc, char **argv)
{
    int want_w = 1280, want_h = 720, seconds = 30;
    if (argc > 1 && sscanf(argv[1], "%dx%d", &want_w, &want_h) != 2) {
        fprintf(stderr, "usage: randremu WxH [SECONDS]\n");
        return 2;
    }
    if (argc > 2) seconds = atoi(argv[2]);

    int screen_number;
    xcb_connection_t *c = xcb_connect(NULL, &screen_number);
    if (xcb_connection_has_error(c)) {
        fprintf(stderr, "randremu: no X server\n");
        return 1;
    }

    xcb_screen_t *screen = xcb_setup_roots_iterator(xcb_get_setup(c)).data;
    xcb_randr_get_screen_resources_reply_t *res =
        xcb_randr_get_screen_resources_reply(c, xcb_randr_get_screen_resources(c, screen->root), NULL);
    if (!res || xcb_randr_get_screen_resources_crtcs_length(res) < 1) {
        fprintf(stderr, "randremu: no CRTC\n");
        return 1;
    }

    xcb_randr_crtc_t crtc = xcb_randr_get_screen_resources_crtcs(res)[0];
    xcb_randr_get_crtc_info_reply_t *info =
        xcb_randr_get_crtc_info_reply(c, xcb_randr_get_crtc_info(c, crtc, res->config_timestamp), NULL);
    if (!info || xcb_randr_get_crtc_info_outputs_length(info) < 1) {
        fprintf(stderr, "randremu: the CRTC drives no output\n");
        return 1;
    }

    xcb_randr_mode_t mode = 0;
    xcb_randr_mode_info_t *modes = xcb_randr_get_screen_resources_modes(res);
    for (int i = 0; i < xcb_randr_get_screen_resources_modes_length(res); i++)
        if (modes[i].width == want_w && modes[i].height == want_h) { mode = modes[i].id; break; }
    if (!mode) {
        fprintf(stderr, "randremu: no %dx%d mode offered\n", want_w, want_h);
        return 1;
    }

    xcb_window_t window = xcb_generate_id(c);
    uint32_t values[] = { screen->black_pixel, XCB_EVENT_MASK_EXPOSURE | XCB_EVENT_MASK_STRUCTURE_NOTIFY };
    xcb_create_window(c, XCB_COPY_FROM_PARENT, window, screen->root, info->x, info->y, want_w, want_h, 0,
                      XCB_WINDOW_CLASS_INPUT_OUTPUT, screen->root_visual, XCB_CW_BACK_PIXEL | XCB_CW_EVENT_MASK, values);
    xcb_change_property(c, XCB_PROP_MODE_REPLACE, window, XCB_ATOM_WM_NAME, XCB_ATOM_STRING, 8, 8, "randremu");
    xcb_change_property(c, XCB_PROP_MODE_REPLACE, window, XCB_ATOM_WM_CLASS, XCB_ATOM_STRING, 8, 18, "randremu\0randremu");
    xcb_atom_t state = intern(c, "_NET_WM_STATE");
    xcb_atom_t fullscreen = intern(c, "_NET_WM_STATE_FULLSCREEN");
    xcb_change_property(c, XCB_PROP_MODE_REPLACE, window, state, XCB_ATOM_ATOM, 32, 1, &fullscreen);

    xcb_randr_output_t *outputs = xcb_randr_get_crtc_info_outputs(info);
    xcb_randr_set_crtc_config_reply_t *set = xcb_randr_set_crtc_config_reply(c,
        xcb_randr_set_crtc_config(c, crtc, XCB_CURRENT_TIME, res->config_timestamp, info->x, info->y, mode,
                                  info->rotation, xcb_randr_get_crtc_info_outputs_length(info), outputs), NULL);
    if (!set || set->status != XCB_RANDR_SET_CONFIG_SUCCESS) {
        fprintf(stderr, "randremu: set_crtc_config refused\n");
        return 1;
    }
    printf("EMULATED %d %d\n", want_w, want_h);
    fflush(stdout);

    xcb_gcontext_t light = xcb_generate_id(c), dark = xcb_generate_id(c);
    uint32_t light_value = 0xFFE0E0E0, dark_value = 0xFF2050C0;
    xcb_create_gc(c, light, window, XCB_GC_FOREGROUND, &light_value);
    xcb_create_gc(c, dark, window, XCB_GC_FOREGROUND, &dark_value);
    xcb_map_window(c, window);
    xcb_flush(c);
    printf("WINDOW 0x%x\n", window);
    fflush(stdout);

    int width = want_w, height = want_h;
    struct timespec start;
    clock_gettime(CLOCK_MONOTONIC, &start);
    struct pollfd pfd = { xcb_get_file_descriptor(c), POLLIN, 0 };
    for (;;) {
        struct timespec now;
        clock_gettime(CLOCK_MONOTONIC, &now);
        long elapsed = (now.tv_sec - start.tv_sec) * 1000 + (now.tv_nsec - start.tv_nsec) / 1000000;
        if (seconds > 0 && elapsed >= seconds * 1000L) break;
        poll(&pfd, 1, 100);
        xcb_generic_event_t *event;
        while ((event = xcb_poll_for_event(c))) {
            switch (event->response_type & 0x7f) {
            case XCB_EXPOSE:
                paint(c, window, light, dark, width, height);
                break;
            case XCB_MAP_NOTIFY:
                printf("MAPPED\n");
                fflush(stdout);
                break;
            case XCB_CONFIGURE_NOTIFY: {
                xcb_configure_notify_event_t *e = (xcb_configure_notify_event_t *)event;
                if (e->window == window) {
                    width = e->width;
                    height = e->height;
                    printf("CONFIGURE %d %d\n", width, height);
                    fflush(stdout);
                    paint(c, window, light, dark, width, height);
                }
                break;
            }
            }
            free(event);
        }
        if (xcb_connection_has_error(c)) break;
    }

    xcb_randr_set_crtc_config_reply_t *restore = xcb_randr_set_crtc_config_reply(c,
        xcb_randr_set_crtc_config(c, crtc, XCB_CURRENT_TIME, res->config_timestamp, info->x, info->y, info->mode,
                                  info->rotation, xcb_randr_get_crtc_info_outputs_length(info), outputs), NULL);
    free(restore);
    printf("RESTORED\n");
    fflush(stdout);
    struct timespec until;
    clock_gettime(CLOCK_MONOTONIC, &until);
    for (int round = 0; round < 10; round++) {
        poll(&pfd, 1, 100);
        xcb_generic_event_t *event;
        while ((event = xcb_poll_for_event(c))) {
            if ((event->response_type & 0x7f) == XCB_CONFIGURE_NOTIFY) {
                xcb_configure_notify_event_t *e = (xcb_configure_notify_event_t *)event;
                if (e->window == window) {
                    printf("CONFIGURE %d %d\n", e->width, e->height);
                    fflush(stdout);
                }
            }
            free(event);
        }
    }
    free(set);
    free(info);
    free(res);
    xcb_disconnect(c);
    return 0;
}
