#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
clients="$here/wlclients"

usage() {
    cat >&2 <<'EOF'
virtual-pointer.sh X Y WIDTH HEIGHT [CLICKS] [BUTTON]
virtual-pointer.sh --drag X1 Y1 X2 Y2 WIDTH HEIGHT [BUTTON]
virtual-pointer.sh --scroll X Y WIDTH HEIGHT STEPS [horizontal]

Warps a pointer to an absolute position over wlr-virtual-pointer and clicks
there. A headless run has no pointer otherwise, so this is what drives a
pointer path that no keyboard binding reaches.

  X Y             where to click, in the coordinate space WIDTH x HEIGHT
  --drag          press at X1 Y1, move there in steps, release at X2 Y2
  --scroll        warp to X Y and send STEPS wheel steps of 10, negative for
                  the other way, vertical unless "horizontal" follows
  WIDTH HEIGHT    that space, normally the output's pixel size
  CLICKS          how many clicks, default 1; pass 2 for a double click
  BUTTON          evdev button code, default 0x110 (left), 0x111 is right

  WAYLAND_DISPLAY names the compositor, as usual.

The coordinates are the output's own pixels, not logical units, so read them
off a screenshot of the same output.
EOF
}

if [[ ${1-} == -h || ${1-} == --help ]]; then
    usage
    exit 0
fi

if [[ ${1-} == --drag ]]; then
    if [[ $# -lt 7 ]]; then
        usage
        exit 1
    fi
elif [[ ${1-} == --scroll ]]; then
    if [[ $# -lt 6 ]]; then
        usage
        exit 1
    fi
elif [[ $# -lt 4 ]]; then
    usage
    exit 1
fi

if ! make -C "$clients" bin/vptr >/dev/null; then
    echo "virtual-pointer.sh: cannot build the pointer client" >&2
    exit 1
fi

exec "$clients/bin/vptr" "$@"
