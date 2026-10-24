#!/usr/bin/env bash
#
# audit-renderers.sh [OPTIONS]
#
# Runs every renderer row through the compositors and checks what a green test
# suite cannot see: that a real session starts, scans out, and tears down with
# nothing live and no crash.
#
#   --headless        Inlet, Westonia and TabWm on the headless backend (default)
#   --nested          TinyWl, Inlet, Westonia and TabWm inside a parent compositor (default)
#   --drm             Inlet, Westonia, TabWm and TinyWl on the display hardware (opt in: takes over
#                     the connected outputs for the length of each run). Each GPU
#                     row also runs a scanout gate: TinyComp under its default
#                     color management, a fullscreen GL client attached, and the
#                     stats probe must report direct scanout for at least 90% of
#                     the commits on that client's output
#   --all             every mode
#   --rows LIST       comma-separated renderer names, default all eight
#   --frames N        frames per Inlet run, default 60
#   --seconds N       seconds per TinyWl run, default 12
#   --client CMD      what to run inside the session, default 'kwm' when present
#   --scanout-client CMD  the fullscreen client the scanout gate attaches, default
#                     'weston-simple-egl -f'. Name wmbench's fsbench2 here (with
#                     BENCH_LIGHT=1 BENCH_BYPASS=1) when weston is not installed
#   --heaptrack       measure the native heap of each Inlet run and hold it to the
#                     native ceilings in tests/Basin.Tests/Budgets/allocation.txt.
#                     The GC uses mmap, so this sees the renderer, GBM, Vulkan and
#                     Skia rather than anything managed
#   --logs DIR        keep the run logs here instead of a temporary directory
#   --no-build        use the binaries already built
#
# The headless mode also checks accessibility once per program. Each program runs
# with BASIN_ACCESSIBILITY=on against a private session bus and AT-SPI bus, and the
# check needs a named window on that bus: Inlet's own window, Westonia's Panel, and
# TabWm's window with at least one named push button. The nested mode checks that each program prints A11Y off,
# because a nested compositor leaves accessibility to the parent session. The check
# is SKIP without dbus-daemon, at-spi-bus-launcher or the Python Atspi bindings
#
# A row that cannot run is reported SKIP, never PASS. That includes a renderer
# this build does not carry, a GPU row with no render node, and a row the
# compositor silently fell back from.

set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
rows=(pixman gl vulkan skia skia-gl skia-vulkan skia-graphite)
modes=()
frames=60
seconds=12
client=
scanout_client='weston-simple-egl -f'
logs=
build=1
heaptrack=0
render_node=${BASIN_RENDER_NODE:-/dev/dri/renderD128}
table=$root/tests/Basin.Tests/Budgets/allocation.txt

while [ $# -gt 0 ]; do
    case "$1" in
        --headless) modes+=(headless); shift ;;
        --nested) modes+=(nested); shift ;;
        --drm) modes+=(drm); shift ;;
        --all) modes=(headless nested drm); shift ;;
        --rows) IFS=, read -r -a rows <<< "$2"; shift 2 ;;
        --frames) frames=$2; shift 2 ;;
        --seconds) seconds=$2; shift 2 ;;
        --client) client=$2; shift 2 ;;
        --scanout-client) scanout_client=$2; shift 2 ;;
        --logs) logs=$2; shift 2 ;;
        --heaptrack) heaptrack=1; shift ;;
        --no-build) build=0; shift ;;
        -h|--help) sed -n '3,41p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 1 ;;
    esac
done

[ ${#modes[@]} -eq 0 ] && modes=(headless nested)
if [ -z "$client" ] && command -v kwm >/dev/null 2>&1; then
    client=kwm
fi

if [ -z "$logs" ]; then
    logs=$(mktemp -d -t basin-audit-XXXXXX)
    logs_temporary=1
else
    mkdir -p "$logs"
    logs_temporary=0
fi

parent_pid=
parent_runtime=

cleanup() {
    trap - EXIT INT TERM
    set +e
    [ -n "$parent_pid" ] && kill "$parent_pid" 2>/dev/null
    wait 2>/dev/null
    [ -n "$parent_runtime" ] && rm -rf "$parent_runtime"
    [ "$logs_temporary" = 1 ] && rm -rf "$logs"
    return 0
}
trap cleanup EXIT INT TERM

if [ "$build" -eq 1 ]; then
    echo "building Inlet, TinyWl, Westonia and TabWm (Release, counters on)"
    dotnet build "$root/samples/Inlet" -c Release -p:BasinCounters=true --nologo -v quiet
    dotnet build "$root/samples/TinyWl" -c Release -p:BasinCounters=true --nologo -v quiet
    dotnet build "$root/samples/Westonia" -c Release -p:BasinCounters=true --nologo -v quiet
    fuse build "$root/samples/TabWm" -c Release -p:BasinCounters=true --nologo -v quiet
    case " ${modes[*]} " in
        *" drm "*) dotnet build "$root/samples/TinyComp" -c Release -p:BasinCounters=true --nologo -v quiet ;;
    esac
fi

inlet=$(find "$root/samples/Inlet/bin/Release" -maxdepth 2 -mindepth 2 -name inlet -type f -perm -u+x 2>/dev/null | head -1)
tinywl=$(find "$root/samples/TinyWl/bin/Release" -maxdepth 2 -mindepth 2 -name tinywl -type f -perm -u+x 2>/dev/null | head -1)
westonia=$(find "$root/samples/Westonia/bin/Release" -maxdepth 2 -mindepth 2 -name westonia -type f -perm -u+x 2>/dev/null | head -1)
tabwm=$(find "$root/samples/TabWm/bin/Release" -maxdepth 2 -mindepth 2 -name tab-wm -type f -perm -u+x 2>/dev/null | head -1)
tinycomp=$(find "$root/samples/TinyComp/bin/Release" -maxdepth 2 -mindepth 2 -name tinycomp -type f -perm -u+x 2>/dev/null | head -1)
if [ -z "$inlet" ] || [ -z "$tinywl" ]; then
    echo "no Release build found; run without --no-build" >&2
    exit 1
fi

passes=0
failures=0
skips=0
results=()

record() {
    results+=("$(printf '%-8s %-10s %-14s %-6s %s' "$1" "$2" "$3" "$4" "$5")")
    case "$4" in
        PASS) passes=$((passes + 1)) ;;
        SKIP) skips=$((skips + 1)) ;;
        *) failures=$((failures + 1)) ;;
    esac
}

crashed() {
    grep -qE "double free|corrupted|Segmentation fault|Vulkan Loader\] ERROR|Unhandled exception" "$1"
}

fell_back() {
    grep -qE "renderer unavailable|falls back to software rendering" "$1"
}

strays() {
    local inlet_left tinywl_left westonia_left tinycomp_left tabwm_left
    inlet_left=$(pgrep -xc inlet 2>/dev/null || true)
    tinywl_left=$(pgrep -xc tinywl 2>/dev/null || true)
    westonia_left=$(pgrep -xc westonia 2>/dev/null || true)
    tinycomp_left=$(pgrep -xc tinycomp 2>/dev/null || true)
    tabwm_left=$(pgrep -xc tab-wm 2>/dev/null || true)
    echo $(( ${inlet_left:-0} + ${tinywl_left:-0} + ${westonia_left:-0} + ${tinycomp_left:-0} + ${tabwm_left:-0} ))
}

needs_gpu() {
    case "$1" in
        pixman|skia) return 1 ;;
        *) return 0 ;;
    esac
}

judge_inlet() {
    local mode=$1 row=$2 log=$3 status=$4
    local live scanout rendered
    if fell_back "$log"; then
        record inlet "$mode" "$row" SKIP "fell back to another renderer"
        return
    fi
    if [ "$status" -ne 0 ]; then
        record inlet "$mode" "$row" FAIL "exit $status$(crashed "$log" && echo ' (crash)')"
        return
    fi
    if crashed "$log"; then
        record inlet "$mode" "$row" FAIL "crash markers in the log"
        return
    fi
    live=$(sed -n 's/^FRAMES [0-9]* LIVE //p' "$log" | head -1)
    if [ -z "$live" ]; then
        record inlet "$mode" "$row" FAIL "no FRAMES line"
        return
    fi
    if [ "$live" != 0 ] && [ "$live" != untracked ]; then
        record inlet "$mode" "$row" FAIL "$live objects live at exit"
        sed -n '/^census/,$p' "$log" | head -20
        return
    fi
    scanout=$(sed -n 's/^SCANOUT //p' "$log" | head -1)
    rendered=$(sed -n 's/^FRAMES \([0-9]*\) LIVE.*/\1/p' "$log" | head -1)
    record inlet "$mode" "$row" PASS "$rendered frames${scanout:+, $scanout}"
}

judge_tinywl() {
    local mode=$1 row=$2 log=$3 status=$4
    local presentation
    if fell_back "$log"; then
        record tinywl "$mode" "$row" SKIP "fell back to another renderer"
        return
    fi
    if [ "$status" -ne 0 ]; then
        record tinywl "$mode" "$row" FAIL "exit $status$(crashed "$log" && echo ' (crash)')"
        return
    fi
    if crashed "$log"; then
        record tinywl "$mode" "$row" FAIL "crash markers in the log"
        return
    fi
    if grep -q "objects still live at exit" "$log"; then
        record tinywl "$mode" "$row" FAIL "$(grep -o '[0-9]* objects still live' "$log" | head -1)"
        sed -n '/^census/,$p' "$log" | head -20
        return
    fi
    presentation=$(sed -n 's/.*TinyWl: \(.*presentation\)/\1/p' "$log" | head -1)
    record tinywl "$mode" "$row" PASS "${presentation:-started}"
}

# heaptrack prints its summary in human units, so a ceiling has to come back
# through the same conversion it went out by.
to_bytes() {
    awk -v text="$1" 'BEGIN {
        unit = substr(text, length(text), 1)
        value = substr(text, 1, length(text) - 1) + 0
        if (unit == "K") { print int(value * 1024) }
        else if (unit == "M") { print int(value * 1024 * 1024) }
        else if (unit == "G") { print int(value * 1024 * 1024 * 1024) }
        else { print int(text + 0) }
    }'
}

native_ceiling() {
    [ -n "$table" ] || return 1
    grep -v '^[[:space:]]*#' "$table" \
        | awk -v want="$1" 'NF == 4 && $1 == "native" && $3 == want { print $4 }'
}

judge_native() {
    local mode=$1 row=$2 trace=$3
    local peak bytes ceiling

    peak=$(heaptrack_print "$trace" 2>/dev/null | sed -n 's/^peak heap memory consumption: //p' | head -1)
    if [ -z "$peak" ]; then
        record native "$mode" "$row" SKIP "heaptrack produced no summary"
        return
    fi

    bytes=$(to_bytes "$peak")
    ceiling=$(native_ceiling "inlet-$row")
    if [ -z "$ceiling" ]; then
        record native "$mode" "$row" SKIP "$peak, no row in the table"
    elif [ "$bytes" -le "$ceiling" ]; then
        record native "$mode" "$row" PASS "$peak native peak, under $ceiling bytes"
    else
        record native "$mode" "$row" FAIL "$peak native peak, over $ceiling bytes"
    fi
}

run_inlet() {
    local mode=$1 row=$2 backend=$3 log=$logs/inlet-$1-$2.log
    local status=0
    local command=("$inlet" --backend "$backend" --renderer "$row" --frames "$frames")
    [ -n "$client" ] && command+=(-c "$client")

    local trace=
    if [ "$heaptrack" = 1 ] && command -v heaptrack >/dev/null 2>&1; then
        trace=$logs/native-$1-$2
        command=(heaptrack -o "$trace" "${command[@]}")
    fi

    timeout --preserve-status -k 10 300 "${command[@]}" >"$log" 2>&1 || status=$?
    judge_inlet "$mode" "$row" "$log" "$status"

    if [ "$heaptrack" = 1 ]; then
        if [ -z "$trace" ]; then
            record native "$mode" "$row" SKIP "heaptrack is not installed"
        elif [ -f "$trace.zst" ]; then
            judge_native "$mode" "$row" "$trace.zst"
        else
            record native "$mode" "$row" SKIP "heaptrack wrote no trace"
        fi
    fi
}

judge_westonia() {
    local mode=$1 row=$2 log=$3 status=$4

    if fell_back "$log"; then
        record westonia "$mode" "$row" SKIP "fell back to another renderer"
        return
    fi

    if [ "$status" -ne 0 ]; then
        record westonia "$mode" "$row" FAIL "exit $status$(crashed "$log" && echo ' (crash)')"
        return
    fi

    if crashed "$log"; then
        record westonia "$mode" "$row" FAIL "crash markers in the log"
        return
    fi

    local rendered live
    rendered=$(grep -m1 '^FRAMES ' "$log" | awk '{print $2}')
    if [ -z "$rendered" ]; then
        record westonia "$mode" "$row" FAIL "no FRAMES line"
        return
    fi

    live=$(grep -m1 '^FRAMES ' "$log" | awk '{print $4}')
    if [ "$live" != untracked ] && [ "$live" != 0 ]; then
        record westonia "$mode" "$row" FAIL "$live objects live at exit"
        return
    fi

    record westonia "$mode" "$row" PASS "$rendered frames, shell drawn by Avalonia"
}

run_westonia() {
    local mode=$1 row=$2 backend=$3 log=$logs/westonia-$1-$2.log
    local status=0
    if [ -z "$westonia" ]; then
        record westonia "$mode" "$row" SKIP "no Release build"
        return
    fi

    timeout --preserve-status -k 10 300 "$westonia" --backend "$backend" --renderer "$row" \
        --config false --frames "$frames" >"$log" 2>&1 || status=$?
    judge_westonia "$mode" "$row" "$log" "$status"
}

judge_tabwm() {
    local mode=$1 row=$2 log=$3 status=$4
    local chosen union rendered live

    chosen=$(sed -n 's/^RENDERER //p' "$log" | head -1)
    if fell_back "$log" || { [ -n "$chosen" ] && [ "$chosen" != "$row" ]; }; then
        record tab-wm "$mode" "$row" SKIP "fell back to ${chosen:-another renderer}"
        return
    fi

    if [ "$status" -ne 0 ]; then
        record tab-wm "$mode" "$row" FAIL "exit $status$(crashed "$log" && echo ' (crash)')"
        return
    fi

    if crashed "$log"; then
        record tab-wm "$mode" "$row" FAIL "crash markers in the log"
        return
    fi

    rendered=$(grep -m1 '^FRAMES ' "$log" | awk '{print $2}')
    if [ -z "$rendered" ] || [ "$rendered" -eq 0 ]; then
        record tab-wm "$mode" "$row" FAIL "${rendered:+0 frames}${rendered:-no FRAMES line}"
        return
    fi

    live=$(grep -m1 '^FRAMES ' "$log" | awk '{print $4}')
    if [ "$live" != untracked ] && [ "$live" != 0 ]; then
        record tab-wm "$mode" "$row" FAIL "$live objects live at exit"
        return
    fi

    union=$(sed -n 's/^UNION engine=\([a-z]*\) pump=\([a-z]*\).*/\1\/\2/p' "$log" | head -1)
    if [ -z "$union" ]; then
        record tab-wm "$mode" "$row" FAIL "no UNION line"
        return
    fi

    record tab-wm "$mode" "$row" PASS "$rendered frames, union $union"
}

run_tabwm() {
    local mode=$1 row=$2 backend=$3 log=$logs/tabwm-$1-$2.log
    local status=0
    if [ -z "$tabwm" ]; then
        record tab-wm "$mode" "$row" SKIP "no Release build"
        return
    fi

    # the default client is a river window manager, which TabWm does not serve, so TabWm runs bare like Westonia
    local command=("$tabwm" --backend "$backend" --renderer "$row" --config false --no-restore --frames "$frames")
    timeout --preserve-status -k 10 300 "${command[@]}" >"$log" 2>&1 || status=$?
    judge_tabwm "$mode" "$row" "$log" "$status"
}

judge_scanout() {
    local mode=$1 row=$2 log=$3
    local verdict
    # a secondary card's view repaints through the oracle and never scans out a client,
    # so a box with no display on its primary card cannot run the gate
    if ! grep -qE '^STATS [^ ]+ scanout=' "$log" && grep -qE '^STATS [^ ]+ full-repaint' "$log"; then
        record scanout "$mode" "$row" SKIP "every output is on a secondary card"
        return
    fi

    verdict=$(awk '
        /^STATS [^ ]+ scanout=/ {
            delete fields
            for (i = 3; i <= NF; i++) {
                n = index($i, "=")
                fields[substr($i, 1, n - 1)] = substr($i, n + 1)
            }
            s = fields["scanout"] + 0
            c = fields["composed"] + 0
            if (fields["direct"] == "True" && s + c > 0 && s * 10 >= (s + c) * 9) {
                printf "PASS %s direct, %d of %d commits scanned out\n", $2, s, s + c
                found = 1
                exit
            }
            last = sprintf("%s direct=%s, %d of %d commits scanned out", $2, fields["direct"], s, s + c)
        }
        END { if (!found) print "FAIL " (last == "" ? "no scanout stats" : last) }
    ' "$log")
    record scanout "$mode" "$row" "${verdict%% *}" "${verdict#* }"
    if [ "${verdict%% *}" != PASS ]; then
        sed -n 's/^STATS   declined \(.*\)/  declined \1/p' "$log"
    fi
}

run_scanout() {
    local mode=$1 row=$2 log=$logs/scanout-$1-$2.log
    if [ -z "$tinycomp" ]; then
        record scanout "$mode" "$row" SKIP "no TinyComp Release build"
        return
    fi
    local client_bin=${scanout_client%% *}
    case "$client_bin" in *=*) client_bin=$(echo "$scanout_client" | tr ' ' '\n' | grep -v = | head -1) ;; esac
    if ! command -v "$client_bin" >/dev/null 2>&1 && [ ! -x "$client_bin" ]; then
        record scanout "$mode" "$row" SKIP "$client_bin is not installed"
        return
    fi

    local fifo=$logs/scanout-$1-$2.fifo
    rm -f "$fifo"
    mkfifo "$fifo"
    timeout --preserve-status -k 10 120 "$tinycomp" --backend drm --renderer "$row" --scale 1 \
        <"$fifo" >"$log" 2>&1 &
    local comp_pid=$!
    exec 3>"$fifo"

    local socket=
    for _ in $(seq 1 100); do
        socket=$(sed -n 's/^SOCKET //p' "$log" | head -1)
        [ -n "$socket" ] && break
        kill -0 "$comp_pid" 2>/dev/null || break
        sleep 0.1
    done

    if fell_back "$log"; then
        record scanout "$mode" "$row" SKIP "fell back to another renderer"
        exec 3>&-
        kill "$comp_pid" 2>/dev/null || true
        wait "$comp_pid" 2>/dev/null || true
        rm -f "$fifo"
        return
    fi
    if [ -z "$socket" ]; then
        record scanout "$mode" "$row" FAIL "no SOCKET line"
        exec 3>&-
        kill "$comp_pid" 2>/dev/null || true
        wait "$comp_pid" 2>/dev/null || true
        rm -f "$fifo"
        return
    fi

    WAYLAND_DISPLAY=$socket setsid sh -c "$scanout_client" >"$logs/scanout-$1-$2.client.log" 2>&1 &
    local client_pid=$!
    sleep 5
    echo stats >&3
    sleep 1
    echo quit >&3
    exec 3>&-
    kill -- -"$client_pid" 2>/dev/null || kill "$client_pid" 2>/dev/null || true
    wait "$comp_pid" 2>/dev/null || true
    wait "$client_pid" 2>/dev/null || true
    rm -f "$fifo"

    judge_scanout "$mode" "$row" "$log"
}

run_tinywl() {
    local mode=$1 row=$2 backend=$3 log=$logs/tinywl-$1-$2.log
    local status=0
    local command=("$tinywl" --backend "$backend" --renderer "$row")
    [ -n "$client" ] && command+=(--startup "$client")
    timeout --preserve-status -s INT -k 10 "$seconds" "${command[@]}" >"$log" 2>&1 || status=$?
    judge_tinywl "$mode" "$row" "$log" "$status"
}

a11y_launcher=
for candidate in /usr/lib/at-spi-bus-launcher /usr/libexec/at-spi-bus-launcher /usr/lib/at-spi2-core/at-spi-bus-launcher; do
    if [ -x "$candidate" ]; then
        a11y_launcher=$candidate
        break
    fi
done

a11y_ready() {
    [ -n "$a11y_launcher" ] && command -v dbus-daemon >/dev/null 2>&1 &&
        python3 -I -c 'import gi; gi.require_version("Atspi", "2.0"); from gi.repository import Atspi' >/dev/null 2>&1
}

# The probe walks the AT-SPI tree as Orca does and prints FOUND when the window and
# the named push buttons are there. It reads role numbers, because AccessKit answers
# no GetRoleName
write_a11y_probe() {
    cat >"$logs/a11y-probe.py" <<'PROBE'
import sys
import time
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

name, need_button = sys.argv[1], sys.argv[2] == "1"
deadline = time.time() + 15
while True:
    window = False
    buttons = 0
    desktop = Atspi.get_desktop(0)
    stack = [(desktop.get_child_at_index(i), 0) for i in range(desktop.get_child_count())]
    while stack:
        node, depth = stack.pop()
        if node is None or depth > 12:
            continue
        try:
            role = node.get_role()
            label = node.get_name() or ""
            count = node.get_child_count()
        except Exception:
            continue
        if role in (Atspi.Role.FRAME, Atspi.Role.WINDOW) and label == name:
            window = True
        if role in (Atspi.Role.PUSH_BUTTON, Atspi.Role.BUTTON) and label:
            buttons += 1
        stack.extend((node.get_child_at_index(i), depth + 1) for i in range(count))
    if window and (buttons > 0 or not need_button):
        print(f"FOUND window={name} buttons={buttons}")
        sys.exit(0)
    if time.time() > deadline:
        print(f"MISSING window={'yes' if window else 'no'} buttons={buttons}")
        sys.exit(1)
    time.sleep(0.5)
PROBE
}

check_a11y() {
    local program=$1 name=$2 need_button=$3
    shift 3
    local log=$logs/a11y-$program.log
    if ! a11y_ready; then
        record "$program" a11y - SKIP "no dbus-daemon, at-spi-bus-launcher or Python Atspi"
        return
    fi

    local runtime session_log session daemon launcher compositor result stdin_fd
    runtime=$(mktemp -d -t basin-audit-a11y-XXXXXX)
    chmod 700 "$runtime"
    session_log=$runtime/session.address
    env -u DBUS_SESSION_BUS_ADDRESS -u AT_SPI_BUS_ADDRESS XDG_RUNTIME_DIR="$runtime" \
        dbus-daemon --session --print-address=1 --nofork --nopidfile >"$session_log" 2>/dev/null &
    daemon=$!
    for _ in $(seq 1 50); do
        [ -s "$session_log" ] && break
        sleep 0.1
    done
    session=$(head -1 "$session_log")
    env -u AT_SPI_BUS_ADDRESS -u DISPLAY XDG_RUNTIME_DIR="$runtime" DBUS_SESSION_BUS_ADDRESS="$session" \
        ATSPI_DBUS_IMPLEMENTATION=dbus-daemon "$a11y_launcher" --launch-immediately >/dev/null 2>&1 &
    launcher=$!
    for _ in $(seq 1 50); do
        [ -S "$runtime/at-spi/bus" ] && break
        sleep 0.1
    done
    busctl --address="$session" set-property org.a11y.Bus /org/a11y/bus org.a11y.Status IsEnabled b true >/dev/null 2>&1 || true

    # A FIFO keeps stdin open without a helper process, so the compositor does not
    # read end of file and quit before the probe
    mkfifo "$runtime/stdin"
    exec {stdin_fd}<>"$runtime/stdin"
    env XDG_RUNTIME_DIR="$runtime" DBUS_SESSION_BUS_ADDRESS="$session" AT_SPI_BUS_ADDRESS="unix:path=$runtime/at-spi/bus" \
        BASIN_ACCESSIBILITY=on BASIN_MUTE=1 WAYLAND_DISPLAY= "$@" >"$log" 2>&1 <&"$stdin_fd" &
    compositor=$!
    for _ in $(seq 1 100); do
        grep -qE '^A11Y (on|off)' "$log" && break
        kill -0 "$compositor" 2>/dev/null || break
        sleep 0.1
    done

    if ! grep -q '^A11Y on' "$log"; then
        record "$program" a11y - FAIL "no A11Y on line"
    elif result=$(AT_SPI_BUS_ADDRESS="unix:path=$runtime/at-spi/bus" DBUS_SESSION_BUS_ADDRESS="$session" \
        XDG_RUNTIME_DIR="$runtime" python3 -I "$logs/a11y-probe.py" "$name" "$need_button" 2>/dev/null); then
        record "$program" a11y - PASS "${result#FOUND }"
    else
        record "$program" a11y - FAIL "${result:-the probe printed nothing}"
    fi

    kill "$compositor" "$launcher" "$daemon" 2>/dev/null || true
    wait "$compositor" "$launcher" "$daemon" 2>/dev/null || true
    exec {stdin_fd}>&-
    rm -rf "$runtime"
}

check_gate_off() {
    local program=$1 log=$2
    [ -f "$log" ] || return 0
    if grep -q '^A11Y off' "$log"; then
        record "$program" nested a11y PASS "the gate is off"
    else
        record "$program" nested a11y FAIL "no A11Y off line"
    fi
}

start_parent() {
    parent_runtime=$(mktemp -d -t basin-audit-parent-XXXXXX)
    local log=$logs/parent.log
    if command -v sway >/dev/null 2>&1; then
        XDG_RUNTIME_DIR=$parent_runtime WLR_BACKENDS=headless WLR_RENDERER=gles2 \
            setsid sway -c /dev/null >"$log" 2>&1 &
        parent_pid=$!
    elif command -v weston >/dev/null 2>&1; then
        XDG_RUNTIME_DIR=$parent_runtime \
            setsid weston --backend=headless --renderer=gl --socket=basin-audit >"$log" 2>&1 &
        parent_pid=$!
    else
        echo "no sway or weston to nest inside" >&2
        return 1
    fi

    local socket=
    for _ in $(seq 1 200); do
        socket=$(find "$parent_runtime" -maxdepth 1 -name 'wayland-*' ! -name '*.lock' -printf '%f\n' 2>/dev/null | head -1)
        [ -n "$socket" ] && break
        if ! kill -0 "$parent_pid" 2>/dev/null; then
            echo "the parent compositor exited before opening a socket; see $log" >&2
            return 1
        fi
        sleep 0.1
    done

    if [ -z "$socket" ]; then
        echo "the parent compositor opened no socket within 20s; see $log" >&2
        return 1
    fi

    parent_socket=$socket
    return 0
}

if [ ! -e "$render_node" ]; then
    echo "no render node at $render_node: the GPU rows will be skipped"
fi

for mode in "${modes[@]}"; do
    echo
    echo "== $mode =="
    if [ "$mode" = nested ]; then
        if ! start_parent; then
            for row in "${rows[@]}"; do
                record tinywl nested "$row" SKIP "no parent compositor"
            done
            continue
        fi
        echo "parent compositor on $parent_socket"
        saved_runtime=${XDG_RUNTIME_DIR:-}
        saved_display=${WAYLAND_DISPLAY:-}
        export XDG_RUNTIME_DIR=$parent_runtime
        export WAYLAND_DISPLAY=$parent_socket
    fi

    if [ "$mode" = drm ] && [ -z "${LIBSEAT_BACKEND:-}" ] && [ -z "${XDG_SESSION_ID:-}" ]; then
        export LIBSEAT_BACKEND=seatd
    fi

    for row in "${rows[@]}"; do
        if needs_gpu "$row" && [ ! -e "$render_node" ]; then
            record inlet "$mode" "$row" SKIP "no render node"
            continue
        fi

        case "$mode" in
            headless)
                run_inlet headless "$row" headless
                run_westonia headless "$row" headless
                run_tabwm headless "$row" headless
                ;;
            nested)
                run_tinywl nested "$row" nested
                run_inlet nested "$row" nested
                run_westonia nested "$row" nested
                run_tabwm nested "$row" nested
                ;;
            drm)
                run_inlet drm "$row" drm
                run_westonia drm "$row" drm
                run_tabwm drm "$row" drm
                run_tinywl drm "$row" drm
                if needs_gpu "$row"; then
                    run_scanout drm "$row"
                fi
                ;;
        esac

        left=$(strays)
        if [ "$left" != 0 ]; then
            echo "warning: $left compositor process(es) still running after the $row run" >&2
        fi
    done

    if [ "$mode" = headless ]; then
        write_a11y_probe
        check_a11y inlet inlet 0 "$inlet" --backend headless --renderer pixman
        [ -n "$westonia" ] && check_a11y westonia Panel 0 "$westonia" --backend headless --renderer pixman --config false
        [ -n "$tabwm" ] && check_a11y tab-wm tab-wm 1 "$tabwm" --backend headless --renderer pixman --config false --no-restore
    fi

    if [ "$mode" = nested ]; then
        first=${rows[0]}
        check_gate_off inlet "$logs/inlet-nested-$first.log"
        check_gate_off westonia "$logs/westonia-nested-$first.log"
        check_gate_off tab-wm "$logs/tabwm-nested-$first.log"
    fi

    if [ "$mode" = nested ]; then
        [ -n "${saved_runtime:-}" ] && export XDG_RUNTIME_DIR=$saved_runtime || unset XDG_RUNTIME_DIR
        [ -n "${saved_display:-}" ] && export WAYLAND_DISPLAY=$saved_display || unset WAYLAND_DISPLAY
    fi

    if [ "$mode" = nested ] && [ -n "$parent_pid" ]; then
        kill "$parent_pid" 2>/dev/null || true
        wait "$parent_pid" 2>/dev/null || true
        parent_pid=
        rm -rf "$parent_runtime"
        parent_runtime=
    fi
done

echo
printf '%-8s %-10s %-14s %-6s %s\n' PROGRAM MODE ROW RESULT DETAIL
for line in "${results[@]}"; do
    echo "$line"
done

echo
echo "$passes passed, $failures failed, $skips skipped"
[ "$logs_temporary" = 0 ] && echo "logs in $logs"
[ "$failures" -eq 0 ]
