// x11focus MODE [SECONDS]: one X11 window shaped the way a focus or class test needs.
// Prints WINDOW 0xID, MAPPED, FOCUS in|out MODE DETAIL, CONFIGURE X Y W H,
// MOVERESIZE sent and KEY CODE for each key press as they happen.
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <poll.h>
#include <time.h>
#include <xcb/xcb.h>

static xcb_connection_t *conn;
static xcb_screen_t *screen;

static xcb_atom_t atom(const char *name)
{
    xcb_intern_atom_reply_t *reply = xcb_intern_atom_reply(conn, xcb_intern_atom(conn, 0, strlen(name), name), NULL);
    xcb_atom_t value = reply ? reply->atom : XCB_ATOM_NONE;
    free(reply);
    return value;
}

static double now(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return ts.tv_sec + ts.tv_nsec / 1e9;
}

static void usage(void)
{
    fprintf(stderr,
        "usage: x11focus MODE [SECONDS]\n"
        "  normal      a plain managed 200x150 window\n"
        "  orflip      created override-redirect, flag cleared before the map\n"
        "  tiny        a 1x1 window\n"
        "  noinput     WM_HINTS input = false and no WM_TAKE_FOCUS\n"
        "  disabled    _WINE_HWND_STYLE with WS_DISABLED\n"
        "  noactivate  _NET_WM_STATE_SKIP_TASKBAR and _NET_WM_STATE_SKIP_PAGER\n"
        "  steal       a normal window that takes the X focus itself after 0.5 s (X11FOCUS_STEAL_MS)\n"
        "  moveresize  a normal window that sends _NET_WM_MOVERESIZE (move) on a press\n"
        "  moveresizenow  the same message, sent once right after the map\n"
        "  stub        an override-redirect 1x1 window parked off-screen\n"
        "  fixedpopup  WM_NORMAL_HINTS with PPosition, PWinGravity and StaticGravity\n"
        "  keepstate   _NET_WM_STATE_ABOVE and _NET_WM_STATE_STICKY set before the map\n");
    exit(2);
}

static void send_moveresize(xcb_window_t window, int16_t x, int16_t y, uint32_t button)
{
    xcb_client_message_event_t message = { 0 };
    message.response_type = XCB_CLIENT_MESSAGE;
    message.format = 32;
    message.window = window;
    message.type = atom("_NET_WM_MOVERESIZE");
    message.data.data32[0] = (uint32_t)x;
    message.data.data32[1] = (uint32_t)y;
    message.data.data32[2] = 8;
    message.data.data32[3] = button;
    message.data.data32[4] = 1;
    xcb_send_event(conn, 0, screen->root,
        XCB_EVENT_MASK_SUBSTRUCTURE_REDIRECT | XCB_EVENT_MASK_SUBSTRUCTURE_NOTIFY, (const char *)&message);
    printf("MOVERESIZE sent\n");
}

static const char *detail_name(uint8_t detail)
{
    static const char *names[] = { "ancestor", "virtual", "inferior", "nonlinear", "nonlinearvirtual", "pointer", "pointerroot", "none" };
    return detail < 8 ? names[detail] : "?";
}

static const char *mode_name(uint8_t mode)
{
    static const char *names[] = { "normal", "grab", "ungrab", "whilegrabbed" };
    return mode < 4 ? names[mode] : "?";
}

int main(int argc, char **argv)
{
    if (argc < 2)
    {
        usage();
    }

    const char *mode = argv[1];
    double seconds = argc > 2 ? atof(argv[2]) : 10;
    int known = !strcmp(mode, "normal") || !strcmp(mode, "orflip") || !strcmp(mode, "tiny") ||
        !strcmp(mode, "noinput") || !strcmp(mode, "disabled") || !strcmp(mode, "noactivate") ||
        !strcmp(mode, "steal") || !strcmp(mode, "moveresize") || !strcmp(mode, "moveresizenow") || !strcmp(mode, "stub") ||
        !strcmp(mode, "fixedpopup") || !strcmp(mode, "keepstate");
    if (!known)
    {
        usage();
    }

    conn = xcb_connect(NULL, NULL);
    if (xcb_connection_has_error(conn))
    {
        fprintf(stderr, "x11focus: cannot connect to the X server\n");
        return 1;
    }

    screen = xcb_setup_roots_iterator(xcb_get_setup(conn)).data;
    int stub = !strcmp(mode, "stub");
    int tiny = !strcmp(mode, "tiny") || stub;
    int16_t x = stub ? -100 : 40;
    int16_t y = stub ? -100 : 40;
    uint16_t width = tiny ? 1 : 200;
    uint16_t height = tiny ? 1 : 150;

    xcb_window_t window = xcb_generate_id(conn);
    uint32_t values[3] = {
        screen->white_pixel,
        (uint32_t)(!strcmp(mode, "orflip") || stub),
        XCB_EVENT_MASK_FOCUS_CHANGE | XCB_EVENT_MASK_STRUCTURE_NOTIFY | XCB_EVENT_MASK_BUTTON_PRESS |
            XCB_EVENT_MASK_EXPOSURE | XCB_EVENT_MASK_KEY_PRESS,
    };
    xcb_create_window(conn, XCB_COPY_FROM_PARENT, window, screen->root, x, y, width, height, 0,
        XCB_WINDOW_CLASS_INPUT_OUTPUT, screen->root_visual,
        XCB_CW_BACK_PIXEL | XCB_CW_OVERRIDE_REDIRECT | XCB_CW_EVENT_MASK, values);

    const char *title = "x11focus";
    xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, XCB_ATOM_WM_NAME, XCB_ATOM_STRING, 8, strlen(title), title);
    const char klass[] = "x11focus\0x11focus";
    xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, XCB_ATOM_WM_CLASS, XCB_ATOM_STRING, 8, sizeof klass, klass);

    if (!strcmp(mode, "noinput"))
    {
        uint32_t hints[9] = { 1, 0 };
        xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, XCB_ATOM_WM_HINTS, XCB_ATOM_WM_HINTS, 32, 9, hints);
    }
    else
    {
        xcb_atom_t protocols[1] = { atom("WM_DELETE_WINDOW") };
        xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, atom("WM_PROTOCOLS"), XCB_ATOM_ATOM, 32, 1, protocols);
    }

    if (!strcmp(mode, "disabled"))
    {
        uint32_t style = 0x08000000 | 0x10000000;
        xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, atom("_WINE_HWND_STYLE"), XCB_ATOM_CARDINAL, 32, 1, &style);
    }

    if (!strcmp(mode, "noactivate") || !strcmp(mode, "keepstate"))
    {
        xcb_atom_t state[2];
        if (!strcmp(mode, "noactivate"))
        {
            state[0] = atom("_NET_WM_STATE_SKIP_TASKBAR");
            state[1] = atom("_NET_WM_STATE_SKIP_PAGER");
        }
        else
        {
            state[0] = atom("_NET_WM_STATE_ABOVE");
            state[1] = atom("_NET_WM_STATE_STICKY");
        }

        xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, atom("_NET_WM_STATE"), XCB_ATOM_ATOM, 32, 2, state);
    }

    if (!strcmp(mode, "fixedpopup"))
    {
        uint32_t hints[18] = { 0 };
        hints[0] = (1 << 2) | (1 << 9);
        hints[1] = (uint32_t)x;
        hints[2] = (uint32_t)y;
        hints[17] = XCB_GRAVITY_STATIC;
        xcb_change_property(conn, XCB_PROP_MODE_REPLACE, window, XCB_ATOM_WM_NORMAL_HINTS, XCB_ATOM_WM_SIZE_HINTS, 32, 18, hints);
    }

    if (!strcmp(mode, "orflip"))
    {
        uint32_t managed = 0;
        xcb_change_window_attributes(conn, window, XCB_CW_OVERRIDE_REDIRECT, &managed);
    }

    xcb_map_window(conn, window);
    xcb_flush(conn);
    printf("WINDOW 0x%x\n", window);
    fflush(stdout);

    double start = now();
    const char *stealMs = getenv("X11FOCUS_STEAL_MS");
    double stealAt = !strcmp(mode, "steal") ? start + (stealMs ? atof(stealMs) / 1000.0 : 0.5) : 0;
    struct pollfd pfd = { .fd = xcb_get_file_descriptor(conn), .events = POLLIN };
    while (now() - start < seconds)
    {
        xcb_generic_event_t *event;
        while ((event = xcb_poll_for_event(conn)) != NULL)
        {
            switch (event->response_type & 0x7F)
            {
                case XCB_MAP_NOTIFY:
                    printf("MAPPED\n");
                    if (!strcmp(mode, "moveresizenow"))
                    {
                        send_moveresize(window, 50, 50, 1);
                    }

                    break;
                case XCB_CONFIGURE_NOTIFY:
                {
                    xcb_configure_notify_event_t *e = (xcb_configure_notify_event_t *)event;
                    printf("CONFIGURE %d %d %u %u\n", e->x, e->y, e->width, e->height);
                    break;
                }
                case XCB_FOCUS_IN:
                case XCB_FOCUS_OUT:
                {
                    xcb_focus_in_event_t *e = (xcb_focus_in_event_t *)event;
                    printf("FOCUS %s %s %s\n", (event->response_type & 0x7F) == XCB_FOCUS_IN ? "in" : "out",
                        mode_name(e->mode), detail_name(e->detail));
                    break;
                }
                case XCB_KEY_PRESS:
                {
                    xcb_key_press_event_t *e = (xcb_key_press_event_t *)event;
                    printf("KEY %u\n", e->detail);
                    break;
                }
                case XCB_BUTTON_PRESS:
                {
                    xcb_button_press_event_t *e = (xcb_button_press_event_t *)event;
                    printf("BUTTON %u %d %d\n", e->detail, e->event_x, e->event_y);
                    if (!strcmp(mode, "moveresize"))
                    {
                        xcb_ungrab_pointer(conn, XCB_CURRENT_TIME);
                        send_moveresize(window, e->root_x, e->root_y, e->detail);
                    }

                    break;
                }
                default:
                    break;
            }

            free(event);
        }

        fflush(stdout);
        xcb_flush(conn);
        if (xcb_connection_has_error(conn))
        {
            return 1;
        }

        if (stealAt > 0 && now() >= stealAt)
        {
            stealAt = 0;
            xcb_set_input_focus(conn, XCB_INPUT_FOCUS_POINTER_ROOT, window, XCB_CURRENT_TIME);
            xcb_flush(conn);
            printf("STOLE\n");
            fflush(stdout);
        }

        poll(&pfd, 1, 50);
    }

    xcb_disconnect(conn);
    return 0;
}
