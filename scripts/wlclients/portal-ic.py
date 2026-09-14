#!/usr/bin/env python3
"""Drive the InputCapture backend: set a right-edge barrier, enable, watch.

Speaks the org.freedesktop.impl.portal.InputCapture interface directly, the
way tests/Basin.Tests/Portal drives it and the way the distro frontend would.
It creates a session, reads the zones, puts one pointer barrier down the right
edge of the first zone, enables capture, and prints the Activated / Deactivated
signals the compositor emits when the pointer crosses the barrier. Move the
cursor past the edge with the `move` stdin command on a headless or DRM run.

Because it talks to the backend rather than the frontend, run it with no distro
xdg-desktop-portal present: the compositor adopts the first caller as its
frontend, and the sender check then accepts it. The zone struct the backend
returns is (width, height, x, y); a vertical barrier spans y = 0 to height - 1.

Usage: portal-ic.py [SECONDS]   (default 25; cross the edge meanwhile)
"""
import sys
import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

IMPL = "org.freedesktop.impl.portal.InputCapture"
DEST = "org.freedesktop.impl.portal.desktop.basin"
ROOT = "/org/freedesktop/portal/desktop"

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
sender = bus.get_unique_name().lstrip(":").replace(".", "_")
loop = GLib.MainLoop()
session = f"{ROOT}/session/{sender}/ic"
request = f"{ROOT}/request/{sender}/r"


def call(member, sig, args):
    return bus.call_sync(DEST, ROOT, IMPL, member, GLib.Variant(sig, args),
                         None, Gio.DBusCallFlags.NONE, 10000, None).unpack()


def main():
    secs = int(sys.argv[1]) if len(sys.argv) > 1 else 25

    print("CreateSession", call("CreateSession", "(oossa{sv})",
          (request, session, "org.example.capture", "",
           {"capabilities": GLib.Variant("u", 3)})), flush=True)

    zresp, zres = call("GetZones", "(oosa{sv})",
                       (request, session, "org.example.capture", {}))
    zones = zres["zones"]
    zone_set = zres["zone_set"]
    print("GetZones", zresp, "zones=", zones, "zone_set=", zone_set, flush=True)
    w, h, zx, zy = list(zones)[0]

    barrier = {"barrier_id": GLib.Variant("u", 1),
               "position": GLib.Variant("(iiii)", (zx + w, zy, zx + w, zy + h - 1))}
    print("SetPointerBarriers", call("SetPointerBarriers", "(oosa{sv}aa{sv}u)",
          (request, session, "org.example.capture", {}, [barrier], zone_set)),
          "edge_x=", zx + w, flush=True)

    for name in ("Activated", "Deactivated", "Disabled"):
        bus.signal_subscribe(
            DEST, IMPL, name, ROOT, None, Gio.DBusSignalFlags.NONE,
            (lambda n: lambda c, s, o, i, sn, p: print(f"SIGNAL {n} {p.unpack()}", flush=True))(name))

    call("Enable", "(osa{sv})", (session, "org.example.capture", {}))
    print(f"Enable sent; barrier down the right edge at x={zx + w}. Watching {secs}s.", flush=True)

    GLib.timeout_add(secs * 1000, lambda: (loop.quit(), False)[1])
    loop.run()
    call("Disable", "(osa{sv})", (session, "org.example.capture", {}))
    print("Disable sent", flush=True)


main()
