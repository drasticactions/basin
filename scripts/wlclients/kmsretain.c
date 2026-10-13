// Lights the first connected connector with color bars and a frame counter,
// then leaves the framebuffer behind in one of three ways, to measure what the
// kernel shows after a DRM master exits. See docs/handoff.md.
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <linux/kd.h>
#include <drm_fourcc.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>
#include <xf86drm.h>
#include <xf86drmMode.h>
#include <libseat.h>

static void usage(void) {
    fprintf(stderr,
        "kmsretain [--card PATH] [--seconds N] [--kd-text VT] [--seat] MODE\n"
        "kmsretain [--card PATH] open SECONDS\n"
        "\n"
        "  MODE is rmfb, closefb or closefb-hold N.\n"
        "  --seconds N   animate the counter for N seconds before the exit, default 2\n"
        "  --kd-text VT  after the release, set /dev/ttyVT to KD_TEXT and clear it,\n"
        "                the way greetd prepares a VT for the next session\n"
        "  --seat        open the card through libseat rather than open(2)\n"
        "  open SECONDS  open the card without master and hold it, a second opener\n");
}

static double now(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return ts.tv_sec + ts.tv_nsec / 1e9;
}

static void stamp(const char *what) {
    printf("%.3f %s\n", now(), what);
    fflush(stdout);
}

static const uint32_t bars[] = {
    0xffffffff, 0xffffff00, 0xff00ffff, 0xff00ff00,
    0xffff00ff, 0xffff0000, 0xff0000ff, 0xff000000,
};

static void paint(uint32_t *px, uint32_t w, uint32_t h, uint32_t stride, unsigned frame) {
    uint32_t band = h / 8;
    for (uint32_t y = 0; y < h; y++) {
        uint32_t *row = (uint32_t *)((uint8_t *)px + (size_t)y * stride);
        for (uint32_t x = 0; x < w; x++)
            row[x] = bars[(x * 8) / w];
        if (y >= h - band) {
            uint32_t cell = w / 16;
            for (uint32_t x = 0; x < w; x++) {
                unsigned bit = 15 - x / cell;
                row[x] = (bit < 16 && (frame >> bit) & 1) ? 0xffffffff : 0xff202020;
            }
        }
    }
}

static struct libseat *seat;
static int seat_ready;

static void seat_enable(struct libseat *s, void *data) {
    (void)s;
    (void)data;
    seat_ready = 1;
}

static void seat_disable(struct libseat *s, void *data) {
    (void)data;
    libseat_disable_seat(s);
}

static struct libseat_seat_listener seat_listener = {
    .enable_seat = seat_enable,
    .disable_seat = seat_disable,
};

static int open_card(const char *path, int use_seat, int *device_id) {
    if (!use_seat)
        return open(path, O_RDWR | O_CLOEXEC);
    seat = libseat_open_seat(&seat_listener, NULL);
    if (!seat) {
        perror("libseat_open_seat");
        return -1;
    }
    while (!seat_ready)
        if (libseat_dispatch(seat, -1) < 0)
            return -1;
    int fd = -1;
    *device_id = libseat_open_device(seat, path, &fd);
    return *device_id < 0 ? -1 : fd;
}

static void kd_text(int vt) {
    char tty[32];
    snprintf(tty, sizeof tty, "/dev/tty%d", vt);
    int fd = open(tty, O_RDWR | O_NOCTTY | O_CLOEXEC);
    if (fd < 0) {
        perror(tty);
        return;
    }
    if (ioctl(fd, KDSETMODE, KD_TEXT) < 0)
        perror("KDSETMODE");
    static const char clear[] = "\033[H\033[2J";
    if (write(fd, clear, sizeof clear - 1) < 0)
        perror("write");
    close(fd);
    stamp("kd-text");
}

int main(int argc, char **argv) {
    const char *card = "/dev/dri/card0";
    double seconds = 2;
    int vt = 0, use_seat = 0;
    int i = 1;
    for (; i < argc && argv[i][0] == '-'; i++) {
        if (!strcmp(argv[i], "--card") && i + 1 < argc)
            card = argv[++i];
        else if (!strcmp(argv[i], "--seconds") && i + 1 < argc)
            seconds = atof(argv[++i]);
        else if (!strcmp(argv[i], "--kd-text") && i + 1 < argc)
            vt = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--seat"))
            use_seat = 1;
        else {
            usage();
            return 2;
        }
    }
    if (i >= argc) {
        usage();
        return 2;
    }
    const char *mode = argv[i];
    double hold = 0;
    if (!strcmp(mode, "open")) {
        if (i + 1 >= argc) {
            usage();
            return 2;
        }
        int fd = open(card, O_RDWR | O_CLOEXEC);
        if (fd < 0) {
            perror(card);
            return 1;
        }
        if (drmIsMaster(fd))
            drmDropMaster(fd);
        stamp("open");
        usleep((useconds_t)(atof(argv[i + 1]) * 1e6));
        stamp("close");
        close(fd);
        return 0;
    }
    if (!strcmp(mode, "closefb-hold")) {
        if (i + 1 >= argc) {
            usage();
            return 2;
        }
        hold = atof(argv[i + 1]);
    } else if (strcmp(mode, "rmfb") && strcmp(mode, "closefb")) {
        usage();
        return 2;
    }

    int device_id = -1;
    int fd = open_card(card, use_seat, &device_id);
    if (fd < 0) {
        perror(card);
        return 1;
    }
    if (!use_seat && drmSetMaster(fd) < 0 && !drmIsMaster(fd)) {
        perror("drmSetMaster");
        return 1;
    }

    drmModeRes *res = drmModeGetResources(fd);
    if (!res) {
        perror("drmModeGetResources");
        return 1;
    }
    drmModeConnector *conn = NULL;
    for (int c = 0; c < res->count_connectors && !conn; c++) {
        drmModeConnector *k = drmModeGetConnector(fd, res->connectors[c]);
        if (k && k->connection == DRM_MODE_CONNECTED && k->count_modes > 0)
            conn = k;
        else
            drmModeFreeConnector(k);
    }
    if (!conn) {
        fprintf(stderr, "no connected connector\n");
        return 1;
    }

    uint32_t crtc_id = 0;
    drmModeModeInfo mode_info = conn->modes[0];
    drmModeEncoder *enc = conn->encoder_id ? drmModeGetEncoder(fd, conn->encoder_id) : NULL;
    if (enc && enc->crtc_id) {
        crtc_id = enc->crtc_id;
        drmModeCrtc *crtc = drmModeGetCrtc(fd, crtc_id);
        if (crtc && crtc->mode_valid)
            mode_info = crtc->mode;
        drmModeFreeCrtc(crtc);
    } else {
        for (int e = 0; e < conn->count_encoders && !crtc_id; e++) {
            drmModeEncoder *k = drmModeGetEncoder(fd, conn->encoders[e]);
            for (int c = 0; k && c < res->count_crtcs; c++)
                if (k->possible_crtcs & (1u << c)) {
                    crtc_id = res->crtcs[c];
                    break;
                }
            drmModeFreeEncoder(k);
        }
    }
    drmModeFreeEncoder(enc);
    if (!crtc_id) {
        fprintf(stderr, "no CRTC for connector %u\n", conn->connector_id);
        return 1;
    }

    struct drm_mode_create_dumb create = {
        .width = mode_info.hdisplay, .height = mode_info.vdisplay, .bpp = 32,
    };
    if (drmIoctl(fd, DRM_IOCTL_MODE_CREATE_DUMB, &create) < 0) {
        perror("CREATE_DUMB");
        return 1;
    }
    uint32_t fb = 0;
    uint32_t handles[4] = {create.handle}, pitches[4] = {create.pitch}, offsets[4] = {0};
    if (drmModeAddFB2(fd, create.width, create.height, DRM_FORMAT_XRGB8888,
                      handles, pitches, offsets, &fb, 0) < 0) {
        perror("drmModeAddFB2");
        return 1;
    }
    struct drm_mode_map_dumb map = {.handle = create.handle};
    if (drmIoctl(fd, DRM_IOCTL_MODE_MAP_DUMB, &map) < 0) {
        perror("MAP_DUMB");
        return 1;
    }
    uint32_t *px = mmap(NULL, create.size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, map.offset);
    if (px == MAP_FAILED) {
        perror("mmap");
        return 1;
    }

    paint(px, create.width, create.height, create.pitch, 0);
    if (drmModeSetCrtc(fd, crtc_id, fb, 0, 0, &conn->connector_id, 1, &mode_info) < 0) {
        perror("drmModeSetCrtc");
        return 1;
    }
    printf("%.3f lit connector=%u crtc=%u fb=%u mode=%ux%u@%u\n", now(), conn->connector_id,
           crtc_id, fb, mode_info.hdisplay, mode_info.vdisplay, mode_info.vrefresh);
    fflush(stdout);

    unsigned frame = 0;
    double end = now() + seconds;
    while (now() < end) {
        usleep(100000);
        paint(px, create.width, create.height, create.pitch, ++frame);
        drmModeDirtyFB(fd, fb, NULL, 0);
    }
    munmap(px, create.size);

    if (!strcmp(mode, "rmfb")) {
        drmModeRmFB(fd, fb);
        stamp("rmfb");
    } else {
        if (drmModeCloseFB(fd, fb) < 0) {
            printf("%.3f closefb-failed errno=%d %s\n", now(), errno, strerror(errno));
            return 1;
        }
        stamp("closefb");
    }
    struct drm_mode_destroy_dumb destroy = {.handle = create.handle};
    drmIoctl(fd, DRM_IOCTL_MODE_DESTROY_DUMB, &destroy);

    if (!use_seat)
        drmDropMaster(fd);
    if (hold > 0) {
        stamp("hold");
        usleep((useconds_t)(hold * 1e6));
    }
    if (use_seat) {
        libseat_close_device(seat, device_id);
        close(fd);
        libseat_close_seat(seat);
    } else {
        close(fd);
    }
    stamp("closed");
    if (vt > 0)
        kd_text(vt);
    drmModeFreeConnector(conn);
    drmModeFreeResources(res);
    return 0;
}
