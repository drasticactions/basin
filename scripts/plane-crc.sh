#!/bin/bash
set -eu

usage() {
    cat <<'EOF'
usage: plane-crc.sh [--card DEV] [--crtc N] [--frames N] [--settle S] --cmd FIFO

Compares the CRTC's hardware CRC between plane-offload-on and plane-offload-off
states of the same static scene. The CRC covers the full scanned-out picture,
overlay planes and hardware cursor included, so equal CRCs mean the plane path
put the same pixels on the wire as the composited reference. Needs root for
debugfs, a compositor that takes the "offload on|off" stdin command through
FIFO, and a still scene: park the pointer and stop any animation first.

  --card DEV    debugfs device directory under /sys/kernel/debug/dri
                (default: the first one with a crc node for the chosen crtc)
  --crtc N      crtc index (default 0)
  --frames N    frames to read per phase; the last 3 must agree (default 8)
  --settle S    seconds to wait after each offload toggle (default 1)

Exit status: 0 planes match, 1 mismatch, 2 the test could not run.
EOF
}

card=
crtc=0
frames=8
settle=1
cmd=
while [ $# -gt 0 ]; do
    case "$1" in
        --card) card=$2; shift 2 ;;
        --crtc) crtc=$2; shift 2 ;;
        --frames) frames=$2; shift 2 ;;
        --settle) settle=$2; shift 2 ;;
        --cmd) cmd=$2; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

[ -n "$cmd" ] || { echo "plane-crc: --cmd FIFO is required" >&2; exit 2; }
[ -p "$cmd" ] || { echo "plane-crc: $cmd is not a fifo" >&2; exit 2; }
[ "$(id -u)" -eq 0 ] || { echo "plane-crc: debugfs needs root" >&2; exit 2; }

if [ -z "$card" ]; then
    for dir in /sys/kernel/debug/dri/*/; do
        if [ -e "$dir/crtc-$crtc/crc/control" ] && grep -q "crtc=crtc" "$dir/state" 2>/dev/null; then
            card=$(basename "$dir")
            break
        fi
    done
fi
[ -n "$card" ] || { echo "plane-crc: no debugfs device with crtc-$crtc/crc found" >&2; exit 2; }
crcdir=/sys/kernel/debug/dri/$card/crtc-$crtc/crc
state=/sys/kernel/debug/dri/$card/state
[ -e "$crcdir/control" ] || { echo "plane-crc: $crcdir/control not found" >&2; exit 2; }

lit_planes() {
    awk '/^plane\[/{on=0} /\tcrtc=crtc/{on=1} /\tfb=/{if (on && $0 !~ /fb=0$/) count++; on=0} END{print count+0}' "$state"
}

capture() {
    echo auto > "$crcdir/control"
    local lines
    lines=$(timeout 10 python3 -c '
import os, sys
fd = os.open(sys.argv[1], os.O_RDONLY)
for _ in range(int(sys.argv[2])):
    chunk = os.read(fd, 4096)
    if not chunk:
        sys.exit(1)
    sys.stdout.write(chunk.decode())
' "$crcdir/data" "$frames") || {
        echo "plane-crc: reading $crcdir/data timed out" >&2
        exit 2
    }
    echo none > "$crcdir/control" 2>/dev/null || true
    local tail3
    tail3=$(printf '%s\n' "$lines" | tail -n 3 | awk '{$1=""; print}' | sort -u)
    if [ "$(printf '%s\n' "$tail3" | wc -l)" -ne 1 ]; then
        echo "plane-crc: CRC not stable over the last 3 frames; is the scene still?" >&2
        exit 2
    fi
    printf '%s\n' "$tail3"
}

planes_on=$(lit_planes)
crc_on=$(capture)
echo "CRC-ON  $crc_on (planes lit: $planes_on)"

echo "offload off" > "$cmd"
sleep "$settle"
planes_off=$(lit_planes)
crc_off=$(capture)
echo "CRC-OFF $crc_off (planes lit: $planes_off)"

echo "offload on" > "$cmd"

if [ "$planes_on" -le "$planes_off" ]; then
    echo "plane-crc: no plane was released by offload off ($planes_on -> $planes_off); nothing was compared" >&2
    exit 2
fi

if [ "$crc_on" = "$crc_off" ]; then
    echo "PLANES-MATCH"
    exit 0
fi

echo "PLANES-MISMATCH"
exit 1
