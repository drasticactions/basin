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

static void die(const char *what)
{
    fprintf(stderr, "vmouse: %s: %s\n", what, strerror(errno));
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

static void nap(long millis)
{
    struct timespec ts = { millis / 1000, (millis % 1000) * 1000000L };
    nanosleep(&ts, NULL);
}

static void usage(void)
{
    fprintf(stderr, "usage: vmouse [--settle MS] [--gap MS] [--name NAME] [--horizontal] STEPS\n");
    fprintf(stderr, "  turns the wheel of a uinput mouse STEPS notches, negative for the other way\n");
    exit(2);
}

int main(int argc, char **argv)
{
    long settle = 1200;
    long gap = 60;
    const char *name = "basin virtual mouse";
    int horizontal = 0;
    int steps = 0;
    int have_steps = 0;

    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--settle") == 0 && i + 1 < argc) {
            settle = strtol(argv[++i], NULL, 10);
        } else if (strcmp(argv[i], "--gap") == 0 && i + 1 < argc) {
            gap = strtol(argv[++i], NULL, 10);
        } else if (strcmp(argv[i], "--name") == 0 && i + 1 < argc) {
            name = argv[++i];
        } else if (strcmp(argv[i], "--horizontal") == 0) {
            horizontal = 1;
        } else if (!have_steps) {
            steps = (int)strtol(argv[i], NULL, 10);
            have_steps = 1;
        } else {
            usage();
        }
    }

    if (!have_steps || steps == 0) {
        usage();
    }

    int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
    if (fd < 0) {
        die("open /dev/uinput (needs write access, normally root)");
    }

    if (ioctl(fd, UI_SET_EVBIT, EV_SYN) < 0 || ioctl(fd, UI_SET_EVBIT, EV_KEY) < 0 ||
        ioctl(fd, UI_SET_EVBIT, EV_REL) < 0) {
        die("UI_SET_EVBIT");
    }

    if (ioctl(fd, UI_SET_KEYBIT, BTN_LEFT) < 0 || ioctl(fd, UI_SET_KEYBIT, BTN_RIGHT) < 0 ||
        ioctl(fd, UI_SET_KEYBIT, BTN_MIDDLE) < 0) {
        die("UI_SET_KEYBIT");
    }

    if (ioctl(fd, UI_SET_RELBIT, REL_X) < 0 || ioctl(fd, UI_SET_RELBIT, REL_Y) < 0 ||
        ioctl(fd, UI_SET_RELBIT, REL_WHEEL) < 0 || ioctl(fd, UI_SET_RELBIT, REL_HWHEEL) < 0 ||
        ioctl(fd, UI_SET_RELBIT, REL_WHEEL_HI_RES) < 0 || ioctl(fd, UI_SET_RELBIT, REL_HWHEEL_HI_RES) < 0) {
        die("UI_SET_RELBIT");
    }

    struct uinput_setup setup;
    memset(&setup, 0, sizeof setup);
    setup.id.bustype = BUS_USB;
    setup.id.vendor = 0x1d6b;
    setup.id.product = 0x0106;
    setup.id.version = 1;
    snprintf(setup.name, sizeof setup.name, "%s", name);
    if (ioctl(fd, UI_DEV_SETUP, &setup) < 0) {
        die("UI_DEV_SETUP");
    }

    if (ioctl(fd, UI_DEV_CREATE) < 0) {
        die("UI_DEV_CREATE");
    }

    nap(settle);

    int count = steps < 0 ? -steps : steps;
    int notch = steps < 0 ? -1 : 1;
    for (int i = 0; i < count; i++) {
        emit(fd, EV_REL, horizontal ? REL_HWHEEL : REL_WHEEL, notch);
        emit(fd, EV_REL, horizontal ? REL_HWHEEL_HI_RES : REL_WHEEL_HI_RES, notch * 120);
        emit(fd, EV_SYN, SYN_REPORT, 0);
        nap(gap);
    }

    fprintf(stderr, "vmouse: turned %d notch(es)\n", steps);
    nap(200);
    ioctl(fd, UI_DEV_DESTROY);
    close(fd);
    return 0;
}
