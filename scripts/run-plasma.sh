#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: scripts/run-plasma.sh [plasma-host|kwin] [OPTIONS] [-- COMPOSITOR_ARGS...]

Runs a Plasma desktop session: the chosen compositor, a private session bus
with the display exported, and plasmashell on top. The private bus matters:
dbus-activated services (kactivitymanagerd, the portals) inherit its
environment, and without WAYLAND_DISPLAY there plasmashell hangs windowless.

  plasma-host        samples/PlasmaHost (default)
  kwin               kwin_wayland

  --backend MODE     nested|drm|headless (default: nested inside a Wayland
                     session, drm outside one)
  --outputs N        output count (headless/nested)
  --scale S          output scale, repeatable per output (plasma-host only)
  --renderer NAME    renderer (plasma-host only)
  --socket NAME      the socket name kwin binds (default wayland-plasma;
                     plasma-host names its own and the script reads it)
  --shell CMD        what to run as the shell (default plasmashell)
  --aot              run the NativeAOT publish of plasma-host
  --powerdevil       also start org_kde_powerdevil on the session bus
  --seconds N        end the session after N seconds
  --no-build         skip building plasma-host

plasmashell prints nothing to stderr. Read it with:
  journalctl --user -t plasmashell -f
EOF
}

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
compositor=plasma-host
backend=
outputs=
scales=()
renderer=
socket_name=wayland-plasma
shell_cmd=plasmashell
powerdevil=0
aot=0
seconds=0
build=1
extra_args=()

while [ $# -gt 0 ]; do
    case "$1" in
        plasma-host|kwin) compositor="$1"; shift ;;
        --backend) backend="$2"; shift 2 ;;
        --outputs) outputs="$2"; shift 2 ;;
        --scale) scales+=("$2"); shift 2 ;;
        --renderer) renderer="$2"; shift 2 ;;
        --socket) socket_name="$2"; shift 2 ;;
        --shell) shell_cmd="$2"; shift 2 ;;
        --powerdevil) powerdevil=1; shift ;;
        --aot) aot=1; shift ;;
        --seconds) seconds="$2"; shift 2 ;;
        --no-build) build=0; shift ;;
        -h|--help) usage; exit 0 ;;
        --) shift; extra_args=("$@"); break ;;
        *) echo "unknown argument: $1" >&2; usage >&2; exit 1 ;;
    esac
done

if [ -z "$backend" ]; then
    if [ -n "${WAYLAND_DISPLAY:-}" ]; then backend=nested; else backend=drm; fi
    echo "no --backend given: using $backend"
fi

comp_pid=
shell_pid=
tail_pid=
powerdevil_pid=
dbus_pid=
log=$(mktemp -t plasma-comp-XXXXXX.log)
shell_log=$(mktemp -t plasma-shell-XXXXXX.log)

cleanup() {
    trap - EXIT INT TERM
    set +e
    [ -n "$shell_pid" ] && kill "$shell_pid" 2>/dev/null
    [ -n "$powerdevil_pid" ] && kill "$powerdevil_pid" 2>/dev/null
    [ -n "$comp_pid" ] && kill "$comp_pid" 2>/dev/null
    [ -n "$tail_pid" ] && kill "$tail_pid" 2>/dev/null
    [ -n "$dbus_pid" ] && kill "$dbus_pid" 2>/dev/null
    wait 2>/dev/null
    rm -f "$log" "$shell_log"
    return 0
}
trap cleanup EXIT INT TERM

socket=
case "$compositor" in
    plasma-host)
        if [ "$aot" -eq 1 ]; then
            if [ "$build" -eq 1 ]; then
                echo "publishing plasma-host (NativeAOT)"
                dotnet publish "$root/samples/PlasmaHost" -c Release -r linux-x64 --nologo -v quiet
            fi
            binary=$(find "$root/samples/PlasmaHost/bin/Release" -path "*/publish/plasma-host" \
                -type f -perm -u+x 2>/dev/null | head -1)
        else
            if [ "$build" -eq 1 ]; then
                echo "building plasma-host (Release)"
                dotnet build "$root/samples/PlasmaHost" -c Release --nologo -v quiet
            fi
            binary=$(find "$root/samples/PlasmaHost/bin/Release" -maxdepth 2 -mindepth 2 \
                -name plasma-host -type f -perm -u+x 2>/dev/null | head -1)
        fi
        if [ -z "$binary" ]; then
            echo "no plasma-host binary found; run without --no-build" >&2
            exit 1
        fi

        args=(--backend "$backend" --shell false)
        [ -n "$outputs" ] && args+=(--outputs "$outputs")
        for s in ${scales[@]+"${scales[@]}"}; do args+=(--scale "$s"); done
        [ -n "$renderer" ] && args+=(--renderer "$renderer")
        [ "$backend" = drm ] && export LIBSEAT_BACKEND="${LIBSEAT_BACKEND:-seatd}"

        "$binary" "${args[@]}" ${extra_args[@]+"${extra_args[@]}"} >"$log" 2>&1 &
        comp_pid=$!
        tail -n +1 -f "$log" &
        tail_pid=$!

        for tenths in $(seq 1 600); do
            socket=$(sed -n 's/^SOCKET //p' "$log" | head -1)
            [ -n "$socket" ] && break
            if ! kill -0 "$comp_pid" 2>/dev/null; then
                echo "plasma-host exited before reporting a socket" >&2
                exit 1
            fi
            sleep 0.1
        done
        if [ -z "$socket" ]; then
            echo "plasma-host reported no socket within 60s" >&2
            exit 1
        fi
        ;;
    kwin)
        command -v kwin_wayland >/dev/null || { echo "kwin_wayland is not installed" >&2; exit 1; }
        args=(--socket "$socket_name" --no-lockscreen)
        case "$backend" in
            headless)
                args+=(--virtual)
                [ -n "$outputs" ] && args+=(--screen-count "$outputs")
                ;;
            drm)
                unset WAYLAND_DISPLAY DISPLAY
                ;;
            nested)
                ;;
        esac
        kwin_wayland "${args[@]}" ${extra_args[@]+"${extra_args[@]}"} >"$log" 2>&1 &
        comp_pid=$!
        tail -n +1 -f "$log" &
        tail_pid=$!
        socket=$socket_name
        for tenths in $(seq 1 300); do
            [ -S "$XDG_RUNTIME_DIR/$socket" ] && break
            if ! kill -0 "$comp_pid" 2>/dev/null; then
                echo "kwin_wayland exited before binding $socket" >&2
                journalctl --user -t kwin_wayland -n 15 --no-pager >&2 || true
                exit 1
            fi
            sleep 0.1
        done
        ;;
esac

for tenths in $(seq 1 20); do
    kill -0 "$comp_pid" 2>/dev/null || break
    sleep 0.1
done
if ! kill -0 "$comp_pid" 2>/dev/null; then
    status=0
    wait "$comp_pid" 2>/dev/null || status=$?
    echo "$compositor bound $socket and then exited with status $status" >&2
    if [ "$compositor" = kwin ]; then
        journalctl --user -t kwin_wayland -n 15 --no-pager >&2 || true
    fi
    [ "$status" -eq 0 ] && status=1
    exit "$status"
fi

echo "$compositor on $socket — clients connect with: WAYLAND_DISPLAY=$socket <command>"

export WAYLAND_DISPLAY="$socket"
export QT_QPA_PLATFORM=wayland
export XDG_CURRENT_DESKTOP=KDE
export XDG_SESSION_TYPE=wayland
export XDG_DATA_DIRS="${XDG_DATA_DIRS:-/usr/local/share:/usr/share}"
export XDG_CONFIG_DIRS="${XDG_CONFIG_DIRS:-/etc/xdg}"
unset DISPLAY

address_file=$(mktemp -t plasma-dbus-XXXXXX)
dbus-daemon --session --fork --print-address=3 --print-pid=4 \
    3>"$address_file" 4>"$address_file.pid"
export DBUS_SESSION_BUS_ADDRESS=$(cat "$address_file")
dbus_pid=$(cat "$address_file.pid")
rm -f "$address_file" "$address_file.pid"
echo "session bus at $DBUS_SESSION_BUS_ADDRESS"

if [ "$powerdevil" -eq 1 ]; then
    /usr/lib/org_kde_powerdevil >/dev/null 2>&1 &
    powerdevil_pid=$!
fi

$shell_cmd >"$shell_log" 2>&1 &
shell_pid=$!
echo "$shell_cmd started (logs in the journal: journalctl --user -t plasmashell -f)"

if [ "$seconds" -gt 0 ]; then
    end=$((SECONDS + seconds))
    while [ "$SECONDS" -lt "$end" ] && kill -0 "$comp_pid" 2>/dev/null && kill -0 "$shell_pid" 2>/dev/null; do
        sleep 0.5
    done
else
    while kill -0 "$comp_pid" 2>/dev/null && kill -0 "$shell_pid" 2>/dev/null; do
        sleep 0.5
    done
fi

if ! kill -0 "$comp_pid" 2>/dev/null; then
    status=0
    wait "$comp_pid" 2>/dev/null || status=$?
    [ "$status" -ne 0 ] && echo "$compositor exited with status $status" >&2
    exit "$status"
fi

if [ "$seconds" -eq 0 ] && ! kill -0 "$shell_pid" 2>/dev/null; then
    status=0
    wait "$shell_pid" 2>/dev/null || status=$?
    [ "$status" -ne 0 ] && { echo "shell exited with status $status" >&2; tail -5 "$shell_log" >&2; }
    exit "$status"
fi

exit 0
