#!/usr/bin/env bash
#

set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
inlet_args=()
deskbar_wm_args=()
build=1

while [ $# -gt 0 ]; do
    case "$1" in
        --no-build)
            build=0
            shift
            ;;
        -h|--help)
            sed -n '3,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        --)
            shift
            deskbar_wm_args=("$@")
            break
            ;;
        *)
            inlet_args+=("$1")
            shift
            ;;
    esac
done

if [ "$build" -eq 1 ]; then
    echo "building inlet and deskbar-wm (Debug)"
    dotnet build "$root/samples/Inlet" -c Debug --nologo -v quiet
    dotnet build "$root/samples/DeskbarWM" -c Debug --nologo -v quiet
fi

find_binary() {
    find "$1/bin/Debug" -maxdepth 2 -mindepth 2 -name "$2" -type f -perm -u+x 2>/dev/null | head -1
}

inlet=$(find_binary "$root/samples/Inlet" inlet)
deskbar_wm=$(find_binary "$root/samples/DeskbarWM" deskbar-wm)
if [ -z "$inlet" ] || [ -z "$deskbar_wm" ]; then
    echo "no Debug build found; run without --no-build" >&2
    exit 1
fi

log=$(mktemp -t inlet-XXXXXX.log)
inlet_pid=
deskbar_wm_pid=
tail_pid=

cleanup() {
    trap - EXIT INT TERM
    # A process that already ended makes kill fail, and this must not become the script's status.
    set +e
    [ -n "$deskbar_wm_pid" ] && kill "$deskbar_wm_pid" 2>/dev/null
    [ -n "$inlet_pid" ] && kill "$inlet_pid" 2>/dev/null
    [ -n "$tail_pid" ] && kill "$tail_pid" 2>/dev/null
    wait 2>/dev/null
    rm -f "$log"
    return 0
}
trap cleanup EXIT INT TERM

if [ -z "${WAYLAND_DISPLAY:-}" ] && [[ " ${inlet_args[*]-} " != *" --backend "* ]]; then
    echo "no WAYLAND_DISPLAY here: inlet will drive the display hardware. Pass --backend to choose."
fi

"$inlet" ${inlet_args[@]+"${inlet_args[@]}"} >"$log" 2>&1 &
inlet_pid=$!
tail -n +1 -f "$log" &
tail_pid=$!

socket=
for tenths in $(seq 1 600); do
    socket=$(sed -n 's/^SOCKET //p' "$log" | head -1)
    [ -n "$socket" ] && break
    if ! kill -0 "$inlet_pid" 2>/dev/null; then
        wait "$inlet_pid" 2>/dev/null && status=0 || status=$?
        echo "inlet exited with status $status before reporting a socket" >&2
        exit 1
    fi
    [ "$tenths" = 50 ] && echo "waiting for inlet to report its socket..."
    sleep 0.1
done

if [ -z "$socket" ]; then
    echo "inlet reported no socket within 60s; it is still running, so something is stuck" >&2
    exit 1
fi

echo "inlet on $socket — clients connect with: WAYLAND_DISPLAY=$socket <command>"

WAYLAND_DISPLAY="$socket" "$deskbar_wm" --socket "$socket" ${deskbar_wm_args[@]+"${deskbar_wm_args[@]}"} &
deskbar_wm_pid=$!

while kill -0 "$inlet_pid" 2>/dev/null && kill -0 "$deskbar_wm_pid" 2>/dev/null; do
    sleep 0.2
done

if kill -0 "$inlet_pid" 2>/dev/null; then
    ended=$deskbar_wm_pid
    name=deskbar_wm
else
    ended=$inlet_pid
    name=inlet
fi

status=0
wait "$ended" 2>/dev/null || status=$?
if [ "$status" -ne 0 ]; then
    echo "$name exited with status $status" >&2
fi

exit "$status"
