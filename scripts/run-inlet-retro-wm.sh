#!/usr/bin/env bash

set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
inlet_args=()
retrowm_args=()
build=1

while [ $# -gt 0 ]; do
    case "$1" in
        --no-build)
            build=0
            shift
            ;;
        --)
            shift
            retrowm_args=("$@")
            break
            ;;
        *)
            inlet_args+=("$1")
            shift
            ;;
    esac
done

if [ "$build" -eq 1 ]; then
    echo "building inlet and retro-wm (Debug)"
    dotnet build "$root/samples/Inlet" -c Debug --nologo -v quiet
    dotnet build "$root/samples/RetroWm" -c Debug --nologo -v quiet
fi

find_binary() {
    find "$1/bin/Debug" -maxdepth 2 -mindepth 2 -name "$2" -type f -perm -u+x 2>/dev/null | head -1
}

inlet=$(find_binary "$root/samples/Inlet" inlet)
retrowm=$(find_binary "$root/samples/RetroWm" retro-wm)
if [ -z "$inlet" ] || [ -z "$retrowm" ]; then
    echo "no Debug build found; run without --no-build" >&2
    exit 1
fi

log=$(mktemp -t inlet-XXXXXX.log)
inlet_pid=
retrowm_pid=
tail_pid=

cleanup() {
    trap - EXIT INT TERM
    set +e
    [ -n "$retrowm_pid" ] && kill "$retrowm_pid" 2>/dev/null
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

WAYLAND_DISPLAY="$socket" "$retrowm" --socket "$socket" ${retrowm_args[@]+"${retrowm_args[@]}"} &
retrowm_pid=$!

while kill -0 "$inlet_pid" 2>/dev/null && kill -0 "$retrowm_pid" 2>/dev/null; do
    sleep 0.2
done

if kill -0 "$inlet_pid" 2>/dev/null; then
    ended=$retrowm_pid
    name=retro-wm
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
