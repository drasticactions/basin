/*
 * vpen: a uinput pen tablet, so a pen arrives through libinput and the
 * compositor's tablet-v2 rather than through a protocol a test owns.
 *
 * The tablet covers the whole output, so positions are fractions of it.
 * One run comes into proximity at --from, strokes to --to with the tip
 * down at --pressure, presses the first barrel button once, lifts and
 * leaves proximity. --eraser uses the eraser end instead of the tip.
 * Needs write access to /dev/uinput, so root.
 *
 * libinput fakes a proximity out when a pen that is not a Wacom sends
 * nothing for 50 ms, so --gap above that cycles proximity between steps.
 *
 * --wheel N makes the tool a tablet mouse (BTN_TOOL_MOUSE) instead. It
 * moves from --from to --to with no tip and turns its wheel N notches
 * with REL_WHEEL on the first N steps. A negative N turns it the other way.
 * The device then claims the Wacom vendor id, because libinput disables
 * the mouse tool on any other tablet.
 */
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <linux/input.h>
#include <linux/uinput.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <time.h>
#include <unistd.h>

#define AXIS_MAX 32767
#define PRESSURE_MAX 4095
#define TILT_MIN -64
#define TILT_MAX 63

static void die(const char *what)
{
    fprintf(stderr, "vpen: %s: %s\n", what, strerror(errno));
    exit(1);
}

static void emit(int fd, int type, int code, int value)
{
    struct input_event ev;
    memset(&ev, 0, sizeof ev);
    ev.type = (unsigned short)type;
    ev.code = (unsigned short)code;
    ev.value = value;
    if (write(fd, &ev, sizeof ev) != (ssize_t)sizeof ev) {
        die("write");
    }
}

static void sync_frame(int fd) { emit(fd, EV_SYN, SYN_REPORT, 0); }

static void nap(long millis)
{
    struct timespec ts = { millis / 1000, (millis % 1000) * 1000000L };
    nanosleep(&ts, NULL);
}

static void absolute(int fd, int code, int min, int max, int resolution)
{
    struct uinput_abs_setup abs;
    memset(&abs, 0, sizeof abs);
    abs.code = (unsigned short)code;
    abs.absinfo.minimum = min;
    abs.absinfo.maximum = max;
    abs.absinfo.resolution = resolution;
    if (ioctl(fd, UI_ABS_SETUP, &abs) < 0) {
        die("UI_ABS_SETUP");
    }
}

static void usage(void)
{
    fprintf(stderr,
        "usage: vpen [--settle MS] [--steps N] [--gap MS] [--from FX FY] [--to FX FY]\n"
        "            [--pressure P] [--tilt TX TY] [--eraser] [--wheel N] [--name NAME]\n"
        "  FX FY are fractions of the output (0..1), P is 0..1, TX TY are degrees\n");
    exit(2);
}

int main(int argc, char **argv)
{
    long settle = 1500;
    long gap = 30;
    int steps = 20;
    double from_x = 0.3, from_y = 0.3, to_x = 0.6, to_y = 0.5;
    double pressure = 0.5;
    int tilt_x = 20, tilt_y = -10;
    int eraser = 0;
    int wheel = 0;
    const char *name = "basin virtual pen";

    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--settle") && i + 1 < argc) settle = strtol(argv[++i], NULL, 10);
        else if (!strcmp(argv[i], "--gap") && i + 1 < argc) gap = strtol(argv[++i], NULL, 10);
        else if (!strcmp(argv[i], "--steps") && i + 1 < argc) steps = (int)strtol(argv[++i], NULL, 10);
        else if (!strcmp(argv[i], "--from") && i + 2 < argc) { from_x = atof(argv[++i]); from_y = atof(argv[++i]); }
        else if (!strcmp(argv[i], "--to") && i + 2 < argc) { to_x = atof(argv[++i]); to_y = atof(argv[++i]); }
        else if (!strcmp(argv[i], "--pressure") && i + 1 < argc) pressure = atof(argv[++i]);
        else if (!strcmp(argv[i], "--tilt") && i + 2 < argc) { tilt_x = atoi(argv[++i]); tilt_y = atoi(argv[++i]); }
        else if (!strcmp(argv[i], "--eraser")) eraser = 1;
        else if (!strcmp(argv[i], "--wheel") && i + 1 < argc) wheel = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--name") && i + 1 < argc) name = argv[++i];
        else usage();
    }

    if (steps < 1 || (wheel != 0 && abs(wheel) > steps)) {
        usage();
    }

    int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
    if (fd < 0) {
        die("open /dev/uinput (needs write access, normally root)");
    }

    if (ioctl(fd, UI_SET_EVBIT, EV_SYN) < 0 || ioctl(fd, UI_SET_EVBIT, EV_KEY) < 0 ||
        ioctl(fd, UI_SET_EVBIT, EV_ABS) < 0 || ioctl(fd, UI_SET_EVBIT, EV_MSC) < 0 ||
        ioctl(fd, UI_SET_EVBIT, EV_REL) < 0) {
        die("UI_SET_EVBIT");
    }

    if (ioctl(fd, UI_SET_RELBIT, REL_WHEEL) < 0) {
        die("UI_SET_RELBIT");
    }

    int keys[] = { BTN_TOOL_PEN, BTN_TOOL_RUBBER, BTN_TOOL_MOUSE, BTN_TOUCH, BTN_STYLUS, BTN_STYLUS2,
                   BTN_LEFT, BTN_RIGHT, BTN_MIDDLE };
    for (size_t i = 0; i < sizeof keys / sizeof keys[0]; i++) {
        if (ioctl(fd, UI_SET_KEYBIT, keys[i]) < 0) {
            die("UI_SET_KEYBIT");
        }
    }

    if (ioctl(fd, UI_SET_MSCBIT, MSC_SERIAL) < 0) {
        die("UI_SET_MSCBIT");
    }

    int axes[] = { ABS_X, ABS_Y, ABS_PRESSURE, ABS_TILT_X, ABS_TILT_Y };
    for (size_t i = 0; i < sizeof axes / sizeof axes[0]; i++) {
        if (ioctl(fd, UI_SET_ABSBIT, axes[i]) < 0) {
            die("UI_SET_ABSBIT");
        }
    }

    /* libinput refuses a tablet whose x and y carry no resolution */
    absolute(fd, ABS_X, 0, AXIS_MAX, 100);
    absolute(fd, ABS_Y, 0, AXIS_MAX, 100);
    absolute(fd, ABS_PRESSURE, 0, PRESSURE_MAX, 0);
    absolute(fd, ABS_TILT_X, TILT_MIN, TILT_MAX, 57);
    absolute(fd, ABS_TILT_Y, TILT_MIN, TILT_MAX, 57);

    struct uinput_setup setup;
    memset(&setup, 0, sizeof setup);
    setup.id.bustype = BUS_USB;
    /* libinput disables the mouse and lens tools on a tablet that is not a Wacom */
    setup.id.vendor = wheel != 0 ? 0x056a : 0x1d6b;
    setup.id.product = 0x0107;
    setup.id.version = 1;
    snprintf(setup.name, sizeof setup.name, "%s", name);
    if (ioctl(fd, UI_DEV_SETUP, &setup) < 0) {
        die("UI_DEV_SETUP");
    }

    if (ioctl(fd, UI_DEV_CREATE) < 0) {
        die("UI_DEV_CREATE");
    }

    nap(settle);
    if (wheel != 0) {
        int x = (int)(from_x * AXIS_MAX);
        int y = (int)(from_y * AXIS_MAX);
        emit(fd, EV_ABS, ABS_X, x);
        emit(fd, EV_ABS, ABS_Y, y);
        emit(fd, EV_MSC, MSC_SERIAL, 0x5678);
        emit(fd, EV_KEY, BTN_TOOL_MOUSE, 1);
        sync_frame(fd);
        printf("IN %.3f %.3f mouse\n", from_x, from_y);
        fflush(stdout);
        nap(gap);
        int turned = 0;
        for (int i = 1; i <= steps; i++) {
            x = (int)((from_x + (to_x - from_x) * i / steps) * AXIS_MAX);
            y = (int)((from_y + (to_y - from_y) * i / steps) * AXIS_MAX);
            emit(fd, EV_ABS, ABS_X, x);
            emit(fd, EV_ABS, ABS_Y, y);
            if (turned < abs(wheel)) {
                emit(fd, EV_REL, REL_WHEEL, wheel > 0 ? 1 : -1);
                turned++;
            }
            sync_frame(fd);
            nap(gap);
        }
        emit(fd, EV_KEY, BTN_TOOL_MOUSE, 0);
        sync_frame(fd);
        printf("OUT %.3f %.3f wheel %d\n", to_x, to_y, wheel);
        fflush(stdout);
        nap(500);
        ioctl(fd, UI_DEV_DESTROY);
        close(fd);
        return 0;
    }

    int tool = eraser ? BTN_TOOL_RUBBER : BTN_TOOL_PEN;
    int x = (int)(from_x * AXIS_MAX);
    int y = (int)(from_y * AXIS_MAX);
    emit(fd, EV_ABS, ABS_X, x);
    emit(fd, EV_ABS, ABS_Y, y);
    emit(fd, EV_ABS, ABS_TILT_X, tilt_x);
    emit(fd, EV_ABS, ABS_TILT_Y, tilt_y);
    emit(fd, EV_MSC, MSC_SERIAL, 0x1234);
    emit(fd, EV_KEY, tool, 1);
    sync_frame(fd);
    printf("IN %.3f %.3f %s\n", from_x, from_y, eraser ? "eraser" : "pen");
    fflush(stdout);
    nap(gap);

    emit(fd, EV_ABS, ABS_PRESSURE, (int)(pressure * PRESSURE_MAX));
    emit(fd, EV_KEY, BTN_TOUCH, 1);
    sync_frame(fd);
    nap(gap);
    for (int i = 1; i <= steps; i++) {
        x = (int)((from_x + (to_x - from_x) * i / steps) * AXIS_MAX);
        y = (int)((from_y + (to_y - from_y) * i / steps) * AXIS_MAX);
        emit(fd, EV_ABS, ABS_X, x);
        emit(fd, EV_ABS, ABS_Y, y);
        sync_frame(fd);
        nap(gap);
    }

    emit(fd, EV_ABS, ABS_PRESSURE, 0);
    emit(fd, EV_KEY, BTN_TOUCH, 0);
    sync_frame(fd);
    nap(gap);
    emit(fd, EV_KEY, BTN_STYLUS, 1);
    sync_frame(fd);
    nap(gap);
    emit(fd, EV_KEY, BTN_STYLUS, 0);
    sync_frame(fd);
    nap(gap);
    emit(fd, EV_KEY, tool, 0);
    sync_frame(fd);
    printf("OUT %.3f %.3f\n", to_x, to_y);
    fflush(stdout);
    nap(500);
    ioctl(fd, UI_DEV_DESTROY);
    close(fd);
    return 0;
}
