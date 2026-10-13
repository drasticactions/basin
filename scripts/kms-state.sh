#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat >&2 <<'EOF'
kms-state.sh [--card N] [--interval MS] [--every] [--once]

Prints one line per CRTC from /sys/kernel/debug/dri/N/state with a monotonic
timestamp: ACTIVE, the mode, and every plane on that CRTC with its FB_ID,
format and size. It polls every 50 ms and prints a CRTC only when its line
changes, so a log across a handoff shows each transition once.

  --card N       the DRM minor, as in /dev/dri/cardN, default 0
  --interval MS  the poll period, default 50
  --every        print every poll, not only the changes
  --once         print the current state and exit

Reads debugfs, so it runs itself under sudo when it is not root.
EOF
}

card=0
interval=50
every=0
once=0
while (($#)); do
    case $1 in
        --card) card=$2; shift 2 ;;
        --interval) interval=$2; shift 2 ;;
        --every) every=1; shift ;;
        --once) once=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage; exit 2 ;;
    esac
done

if ((EUID != 0)); then
    exec sudo "$0" --card "$card" --interval "$interval" \
        $( ((every)) && echo --every) $( ((once)) && echo --once)
fi

state=/sys/kernel/debug/dri/$card/state
if [[ ! -r $state ]]; then
    echo "kms-state.sh: cannot read $state" >&2
    exit 1
fi

snapshot() {
    awk '
        /^plane\[/ { section = "plane"; plane = $1; gsub(/[^0-9]/, "", plane); plane = "plane" plane; pcrtc = ""; pfb = ""; pfmt = ""; psize = ""; next }
        /^crtc\[/ { section = "crtc"; crtc = $2; order[++n] = crtc; next }
        /^connector\[/ { section = "connector"; next }
        /^[^\t]/ { section = ""; next }
        section == "plane" && /^\tcrtc=/ { pcrtc = substr($0, 7); next }
        section == "plane" && /^\tfb=/ { pfb = substr($0, 5); next }
        section == "plane" && /^\t\tformat=/ { split(substr($0, 10), f, " "); pfmt = f[1]; next }
        section == "plane" && /^\t\tsize=/ { psize = substr($0, 8); next }
        section == "plane" && /^\tcrtc-pos=/ {
            if (pcrtc != "(null)" && pfb != "0")
                planes[pcrtc] = planes[pcrtc] " " plane "=fb" pfb ":" pfmt ":" psize
            next
        }
        section == "crtc" && /^\tactive=/ { active[crtc] = substr($0, 9); next }
        section == "crtc" && /^\tmode:/ { split($0, m, "\""); if (m[2] != "") mode[crtc] = m[2] "@" $3; next }
        END {
            for (i = 1; i <= n; i++) {
                c = order[i]
                printf "%s active=%s mode=%s planes=%s\n", c, active[c], (c in mode ? mode[c] : "-"), (c in planes ? substr(planes[c], 2) : "-")
            }
        }
    ' "$state"
}

declare -A last=()
while :; do
    ts=$(awk '{ printf "%.3f", $1 }' /proc/uptime)
    while IFS= read -r line; do
        key=${line%% *}
        if ((every)) || [[ ${last[$key]-} != "$line" ]]; then
            printf '%s %s\n' "$ts" "$line"
            last[$key]=$line
        fi
    done < <(snapshot)
    ((once)) && exit 0
    sleep "$(awk -v ms="$interval" 'BEGIN { printf "%.3f", ms / 1000 }')"
done
