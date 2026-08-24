#!/usr/bin/env bash
set -euo pipefail

# Drive kde_lockscreen_overlay_v1 synthetically with a real KDE client.
#
# kclock is the canonical overlay client. Its --alarm-lockscreen-popup mode is
# the exact path a firing alarm takes: it binds kde_lockscreen_overlay_v1, calls
# allow() on its window while unmapped, maps the popup and raises it. The alarm
# id need not name a real alarm -- the popup shows "Alarm not found" and still
# takes the whole overlay path -- so no alarm is scheduled and no phone is
# needed. KDE Connect's desktop daemon does not link this interface, so kclock
# is the faithful stand-in for the spec's "ring the paired phone" check.
#
# The run is headless and deterministic: PlasmaHost composites into a buffer and
# writes PNGs over its stdin command channel. It never takes a real display.
#
# Needs: kclock built with KCLOCK_BUILD_SHELL_OVERLAY (Arch's kclock is),
# weston-simple-shm, ImageMagick, and a built PlasmaHost.

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/.." && pwd)"
out="${OUT:-$(mktemp -d)}"
mkdir -p "$out"
uuid="00000000-1111-2222-3333-444444444444"

echo "artifacts in $out"

for tool in kclock weston-simple-shm magick; do
    command -v "$tool" >/dev/null || { echo "missing $tool" >&2; exit 1; }
done

cmds="$out/cmds"
rm -f "$cmds"
mkfifo "$cmds"

# Hold the fifo open so PlasmaHost's stdin never sees EOF.
exec 3<>"$cmds"

dotnet run --project "$root/samples/PlasmaHost" -c Release -- \
    --backend headless --shell false <&3 >"$out/host.log" 2>&1 &
host=$!

cleanup() {
    kill "$host" 2>/dev/null || true
    pkill -f "kclock --alarm-lockscreen-popup $uuid" 2>/dev/null || true
    exec 3>&- || true
    rm -f "$cmds"
}
trap cleanup EXIT

for _ in $(seq 1 60); do
    grep -q "^SOCKET" "$out/host.log" 2>/dev/null && break
    sleep 0.5
done
grep -q "^SOCKET" "$out/host.log" || { echo "PlasmaHost never printed SOCKET" >&2; cat "$out/host.log" >&2; exit 1; }
# PlasmaHost binds wayland-0.. by index; read the name it actually chose.
display="$(grep -m1 "^SOCKET" "$out/host.log" | awk '{print $2}')"
echo "PlasmaHost on $display"

export WAYLAND_DISPLAY="$display"
export QT_QPA_PLATFORM=wayland

# An ordinary client that never calls allow -- the negative control.
weston-simple-shm >"$out/shm.log" 2>&1 &
sleep 2

# The overlay client. WAYLAND_DEBUG records the wire so the allow request is
# provable, not inferred from what ends up on top.
WAYLAND_DEBUG=1 kclock --alarm-lockscreen-popup "$uuid" >"$out/kclock.log" 2>&1 &
sleep 5

echo "=== kde_lockscreen_overlay_v1 wire traffic from kclock ==="
grep -a "lockscreen_overlay" "$out/kclock.log" || echo "(none -- kclock did not drive the protocol)"
echo

echo "shot $out/prelock.png" >&3
sleep 1

"$root/scripts/session-lock.sh" 12 FF204060 >"$out/lock.log" 2>&1 &
sleep 4
echo "shot $out/locked.png" >&3
sleep 1

# kclock's alarm popup is centred at 960,540 on the 1920x1080 output. Sample a
# point inside its content, and two points that are only ever desktop or lock
# surface. The lock colour is srgb 32,64,96 (from FF204060 above).
echo "=== pixels (lock colour is srgb 32,64,96) ==="
for f in prelock locked; do
    printf '%-9s ' "$f"
    magick "$out/$f.png" -format \
      'kclock-content(960,540)=%[pixel:p{960,540}] desktop-left(200,200)=%[pixel:p{200,200}] far-corner(1800,900)=%[pixel:p{1800,900}]\n' \
      info:
done

echo
echo "expected while locked: kclock-content is NOT the lock colour (the allowed"
echo "alarm surface composites above the lock), desktop-left and far-corner ARE"
echo "the lock colour (every un-allowed surface is covered)."
