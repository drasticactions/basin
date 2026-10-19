ROOT := $(abspath $(dir $(lastword $(MAKEFILE_LIST)))../..)
BIN := bin
SCANNER ?= wayland-scanner

WAYLAND_PROTOCOLS := $(ROOT)/external/wayland-protocols
WLR_PROTOCOLS := $(ROOT)/external/wlr-protocols

LOCK_XML := $(WAYLAND_PROTOCOLS)/staging/ext-session-lock/ext-session-lock-v1.xml
VPTR_XML := $(WLR_PROTOCOLS)/unstable/wlr-virtual-pointer-unstable-v1.xml
XDG_XML := $(WAYLAND_PROTOCOLS)/stable/xdg-shell/xdg-shell.xml
DECORATION_XML := $(WAYLAND_PROTOCOLS)/unstable/xdg-decoration/xdg-decoration-unstable-v1.xml
GESTURES_XML := $(WAYLAND_PROTOCOLS)/unstable/pointer-gestures/pointer-gestures-unstable-v1.xml
COLOR_XML := $(WAYLAND_PROTOCOLS)/staging/color-management/color-management-v1.xml
HYPRLAND_PROTOCOLS := $(ROOT)/external/hyprland-protocols/protocols
FOREIGN_LIST_XML := $(WAYLAND_PROTOCOLS)/staging/ext-foreign-toplevel-list/ext-foreign-toplevel-list-v1.xml
FRACTIONAL_XML := $(WAYLAND_PROTOCOLS)/staging/fractional-scale/fractional-scale-v1.xml
VIEWPORTER_XML := $(WAYLAND_PROTOCOLS)/stable/viewporter/viewporter.xml

CFLAGS += -O1 -Wall -Wextra -I$(BIN) $(shell pkg-config --cflags wayland-client)
LDLIBS += $(shell pkg-config --libs wayland-client)

all: $(BIN)/locktest $(BIN)/vptr $(BIN)/vpad $(BIN)/vtouch $(BIN)/vkbd $(BIN)/vmouse $(BIN)/gestures $(BIN)/colorinfo \
	$(BIN)/hyprsurface $(BIN)/focusgrab $(BIN)/locknotify $(BIN)/toplevelexport $(BIN)/ssdwin \
	$(BIN)/panel $(BIN)/riverrepeat $(BIN)/appmenu $(BIN)/pointerlock $(BIN)/textinput $(BIN)/randremu $(BIN)/x11focus \
	$(BIN)/scrgbwin $(BIN)/actionbind $(BIN)/nv12win

$(BIN):
	mkdir -p $(BIN)

$(BIN)/%-client-protocol.h: | $(BIN)
	@test -f "$(XML_$*)" || { echo "missing $(XML_$*); run git submodule update --init" >&2; exit 1; }
	$(SCANNER) client-header "$(XML_$*)" $@

$(BIN)/%-protocol.c: | $(BIN)
	@test -f "$(XML_$*)" || { echo "missing $(XML_$*); run git submodule update --init" >&2; exit 1; }
	$(SCANNER) private-code "$(XML_$*)" $@

XML_ext-session-lock-v1 := $(LOCK_XML)
XML_wlr-virtual-pointer-unstable-v1 := $(VPTR_XML)
XML_xdg-shell := $(XDG_XML)
XML_xdg-dialog-v1 := $(WAYLAND_PROTOCOLS)/staging/xdg-dialog/xdg-dialog-v1.xml
XML_appmenu := $(ROOT)/external/plasma-wayland-protocols/src/protocols/appmenu.xml
XML_gtk-shell := $(ROOT)/protocol/gtk-shell.xml
XML_xdg-decoration-unstable-v1 := $(DECORATION_XML)
XML_pointer-gestures-unstable-v1 := $(GESTURES_XML)
XML_color-management-v1 := $(COLOR_XML)
XML_gamescope-action-binding := $(ROOT)/protocol/gamescope-action-binding.xml
XML_hyprland-surface-v1 := $(HYPRLAND_PROTOCOLS)/hyprland-surface-v1.xml
XML_hyprland-focus-grab-v1 := $(HYPRLAND_PROTOCOLS)/hyprland-focus-grab-v1.xml
XML_hyprland-lock-notify-v1 := $(HYPRLAND_PROTOCOLS)/hyprland-lock-notify-v1.xml
XML_hyprland-toplevel-mapping-v1 := $(HYPRLAND_PROTOCOLS)/hyprland-toplevel-mapping-v1.xml
XML_hyprland-toplevel-export-v1 := $(HYPRLAND_PROTOCOLS)/hyprland-toplevel-export-v1.xml
XML_ext-foreign-toplevel-list-v1 := $(FOREIGN_LIST_XML)
XML_fractional-scale-v1 := $(FRACTIONAL_XML)
XML_viewporter := $(VIEWPORTER_XML)
XML_linux-dmabuf-v1 := $(WAYLAND_PROTOCOLS)/stable/linux-dmabuf/linux-dmabuf-v1.xml
XML_text-input-unstable-v3 := $(WAYLAND_PROTOCOLS)/unstable/text-input/text-input-unstable-v3.xml
XML_pointer-constraints-unstable-v1 := $(WAYLAND_PROTOCOLS)/unstable/pointer-constraints/pointer-constraints-unstable-v1.xml
XML_relative-pointer-unstable-v1 := $(WAYLAND_PROTOCOLS)/unstable/relative-pointer/relative-pointer-unstable-v1.xml
XML_river-input-management-v1 := $(ROOT)/external/river/protocol/river-input-management-v1.xml
XML_wlr-layer-shell-unstable-v1 := $(WLR_PROTOCOLS)/unstable/wlr-layer-shell-unstable-v1.xml
XML_wlr-foreign-toplevel-management-unstable-v1 := $(WLR_PROTOCOLS)/unstable/wlr-foreign-toplevel-management-unstable-v1.xml

$(BIN)/locktest: locktest.c $(BIN)/ext-session-lock-v1-protocol.c $(BIN)/ext-session-lock-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ locktest.c $(BIN)/ext-session-lock-v1-protocol.c $(LDLIBS)

$(BIN)/vptr: vptr.c $(BIN)/wlr-virtual-pointer-unstable-v1-protocol.c $(BIN)/wlr-virtual-pointer-unstable-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ vptr.c $(BIN)/wlr-virtual-pointer-unstable-v1-protocol.c $(LDLIBS)

$(BIN)/randremu: randremu.c | $(BIN)
	$(CC) -O1 -Wall -Wextra -o $@ randremu.c $(shell pkg-config --cflags --libs xcb xcb-randr)

$(BIN)/x11focus: x11focus.c | $(BIN)
	$(CC) -O1 -Wall -Wextra -o $@ x11focus.c $(shell pkg-config --cflags --libs xcb)

# links libdrm and libseat rather than wayland, and runs as root on a bare card, so it is not part of all
$(BIN)/kmsretain: kmsretain.c | $(BIN)
	$(CC) -O1 -Wall -Wextra -o $@ kmsretain.c $(shell pkg-config --cflags --libs libdrm libseat)

# a Windows program, so it needs mingw-w64 and is not part of all
$(BIN)/modeswitch.exe: modeswitch.c | $(BIN)
	x86_64-w64-mingw32-gcc -O2 -Wall -o $@ modeswitch.c -mwindows -lgdi32

$(BIN)/vpad: vpad.c | $(BIN)
	$(CC) $(CFLAGS) -o $@ vpad.c -lm

$(BIN)/vtouch: vtouch.c | $(BIN)
	$(CC) $(CFLAGS) -o $@ vtouch.c -lm

GESTURES_GEN := $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
	$(BIN)/pointer-gestures-unstable-v1-protocol.c \
	$(BIN)/pointer-gestures-unstable-v1-client-protocol.h

$(BIN)/gestures: gestures.c $(GESTURES_GEN) | $(BIN)
	$(CC) $(CFLAGS) -o $@ gestures.c $(BIN)/xdg-shell-protocol.c \
		$(BIN)/pointer-gestures-unstable-v1-protocol.c $(LDLIBS)

$(BIN)/hyprsurface: hyprsurface.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
		$(BIN)/hyprland-surface-v1-protocol.c $(BIN)/hyprland-surface-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ hyprsurface.c $(BIN)/xdg-shell-protocol.c $(BIN)/hyprland-surface-v1-protocol.c $(LDLIBS)

$(BIN)/focusgrab: focusgrab.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
		$(BIN)/hyprland-focus-grab-v1-protocol.c $(BIN)/hyprland-focus-grab-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ focusgrab.c $(BIN)/xdg-shell-protocol.c $(BIN)/hyprland-focus-grab-v1-protocol.c $(LDLIBS)

$(BIN)/locknotify: locknotify.c $(BIN)/hyprland-lock-notify-v1-protocol.c $(BIN)/hyprland-lock-notify-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ locknotify.c $(BIN)/hyprland-lock-notify-v1-protocol.c $(LDLIBS)

EXPORT_GEN := $(BIN)/ext-foreign-toplevel-list-v1-protocol.c $(BIN)/ext-foreign-toplevel-list-v1-client-protocol.h \
	$(BIN)/wlr-foreign-toplevel-management-unstable-v1-protocol.c $(BIN)/wlr-foreign-toplevel-management-unstable-v1-client-protocol.h \
	$(BIN)/hyprland-toplevel-mapping-v1-protocol.c $(BIN)/hyprland-toplevel-mapping-v1-client-protocol.h \
	$(BIN)/hyprland-toplevel-export-v1-protocol.c $(BIN)/hyprland-toplevel-export-v1-client-protocol.h

$(BIN)/toplevelexport: toplevelexport.c $(EXPORT_GEN) | $(BIN)
	$(CC) $(CFLAGS) -o $@ toplevelexport.c $(BIN)/ext-foreign-toplevel-list-v1-protocol.c \
		$(BIN)/wlr-foreign-toplevel-management-unstable-v1-protocol.c \
		$(BIN)/hyprland-toplevel-mapping-v1-protocol.c $(BIN)/hyprland-toplevel-export-v1-protocol.c $(LDLIBS)

clean:
	rm -rf $(BIN)

.PHONY: all clean

$(BIN)/colorinfo: colorinfo.c $(BIN)/color-management-v1-protocol.c $(BIN)/color-management-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ colorinfo.c $(BIN)/color-management-v1-protocol.c $(LDLIBS)

$(BIN)/vkbd: vkbd.c | $(BIN)
	$(CC) $(CFLAGS) -o $@ vkbd.c

$(BIN)/vmouse: vmouse.c | $(BIN)
	$(CC) $(CFLAGS) -o $@ vmouse.c

$(BIN)/ssdwin: ssdwin.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
		$(BIN)/xdg-decoration-unstable-v1-protocol.c $(BIN)/xdg-decoration-unstable-v1-client-protocol.h \
		$(BIN)/fractional-scale-v1-protocol.c $(BIN)/fractional-scale-v1-client-protocol.h \
		$(BIN)/viewporter-protocol.c $(BIN)/viewporter-client-protocol.h \
		$(BIN)/xdg-dialog-v1-protocol.c $(BIN)/xdg-dialog-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ ssdwin.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-decoration-unstable-v1-protocol.c \
		$(BIN)/fractional-scale-v1-protocol.c $(BIN)/viewporter-protocol.c $(BIN)/xdg-dialog-v1-protocol.c $(LDLIBS)

$(BIN)/panel: panel.c $(BIN)/wlr-layer-shell-unstable-v1-protocol.c $(BIN)/wlr-layer-shell-unstable-v1-client-protocol.h \
		$(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ panel.c $(BIN)/wlr-layer-shell-unstable-v1-protocol.c $(BIN)/xdg-shell-protocol.c $(LDLIBS)

$(BIN)/riverrepeat: riverrepeat.c $(BIN)/river-input-management-v1-protocol.c $(BIN)/river-input-management-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ riverrepeat.c $(BIN)/river-input-management-v1-protocol.c $(LDLIBS)

$(BIN)/appmenu: appmenu.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
		$(BIN)/appmenu-protocol.c $(BIN)/appmenu-client-protocol.h \
		$(BIN)/gtk-shell-protocol.c $(BIN)/gtk-shell-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) $(shell pkg-config --cflags gio-2.0 gio-unix-2.0) -o $@ appmenu.c $(BIN)/xdg-shell-protocol.c \
		$(BIN)/appmenu-protocol.c $(BIN)/gtk-shell-protocol.c $(LDLIBS) $(shell pkg-config --libs gio-2.0 gio-unix-2.0)

POINTERLOCK_GEN := $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
	$(BIN)/pointer-constraints-unstable-v1-protocol.c $(BIN)/pointer-constraints-unstable-v1-client-protocol.h \
	$(BIN)/relative-pointer-unstable-v1-protocol.c $(BIN)/relative-pointer-unstable-v1-client-protocol.h

$(BIN)/pointerlock: pointerlock.c $(POINTERLOCK_GEN) | $(BIN)
	$(CC) $(CFLAGS) -o $@ pointerlock.c $(BIN)/xdg-shell-protocol.c \
		$(BIN)/pointer-constraints-unstable-v1-protocol.c $(BIN)/relative-pointer-unstable-v1-protocol.c $(LDLIBS)

$(BIN)/scrgbwin: scrgbwin.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
		$(BIN)/color-management-v1-protocol.c $(BIN)/color-management-v1-client-protocol.h
	$(CC) $(CFLAGS) -o $@ scrgbwin.c $(BIN)/xdg-shell-protocol.c $(BIN)/color-management-v1-protocol.c $(LDLIBS)

$(BIN)/actionbind: actionbind.c $(BIN)/gamescope-action-binding-protocol.c $(BIN)/gamescope-action-binding-client-protocol.h
	$(CC) $(CFLAGS) $(shell pkg-config --cflags xkbcommon) -o $@ actionbind.c $(BIN)/gamescope-action-binding-protocol.c $(LDLIBS) $(shell pkg-config --libs xkbcommon)

$(BIN)/textinput: textinput.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
		$(BIN)/text-input-unstable-v3-protocol.c $(BIN)/text-input-unstable-v3-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) -o $@ textinput.c $(BIN)/xdg-shell-protocol.c $(BIN)/text-input-unstable-v3-protocol.c $(LDLIBS)

$(BIN)/nv12win: nv12win.c $(BIN)/xdg-shell-protocol.c $(BIN)/xdg-shell-client-protocol.h \
	$(BIN)/linux-dmabuf-v1-protocol.c $(BIN)/linux-dmabuf-v1-client-protocol.h | $(BIN)
	$(CC) $(CFLAGS) $(shell pkg-config --cflags gbm) -o $@ nv12win.c $(BIN)/xdg-shell-protocol.c $(BIN)/linux-dmabuf-v1-protocol.c $(LDLIBS) $(shell pkg-config --libs gbm)
