#!/usr/bin/env bash
#
# audit-renderers.sh [OPTIONS]
#
# Runs every renderer row through the compositors and checks what a green test
# suite cannot see: that a real session starts, scans out, and tears down with
# nothing live and no crash.
#
#   --headless        Inlet on the headless backend (default)
#   --nested          TinyWl and Inlet inside a parent compositor (default)
#   --drm             Inlet and TinyWl on the display hardware (opt in: takes over
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
        -h|--help) sed -n '3,31p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
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
    echo "building Inlet, TinyWl and Westonia (Release, counters on)"
    dotnet build "$root/samples/Inlet" -c Release -p:BasinCounters=true --nologo -v quiet
    dotnet build "$root/samples/TinyWl" -c Release -p:BasinCounters=true --nologo -v quiet
    dotnet build "$root/samples/Westonia" -c Release -p:BasinCounters=true --nologo -v quiet
    case " ${modes[*]} " in
        *" drm "*) dotnet build "$root/samples/TinyComp" -c Release -p:BasinCounters=true --nologo -v quiet ;;
    esac
fi

inlet=$(find "$root/samples/Inlet/bin/Release" -maxdepth 2 -mindepth 2 -name inlet -type f -perm -u+x 2>/dev/null | head -1)
tinywl=$(find "$root/samples/TinyWl/bin/Release" -maxdepth 2 -mindepth 2 -name tinywl -type f -perm -u+x 2>/dev/null | head -1)
westonia=$(find "$root/samples/Westonia/bin/Release" -maxdepth 2 -mindepth 2 -name westonia -type f -perm -u+x 2>/dev/null | head -1)
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
    local inlet_left tinywl_left westonia_left tinycomp_left
    inlet_left=$(pgrep -xc inlet 2>/dev/null || true)
    tinywl_left=$(pgrep -xc tinywl 2>/dev/null || true)
    westonia_left=$(pgrep -xc westonia 2>/dev/null || true)
    tinycomp_left=$(pgrep -xc tinycomp 2>/dev/null || true)
    echo $(( ${inlet_left:-0} + ${tinywl_left:-0} + ${westonia_left:-0} + ${tinycomp_left:-0} ))
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

judge_scanout() {
    local mode=$1 row=$2 log=$3
    local verdict
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
                ;;
            nested)
                run_tinywl nested "$row" nested
                run_inlet nested "$row" nested
                run_westonia nested "$row" nested
                ;;
            drm)
                run_inlet drm "$row" drm
                run_westonia drm "$row" drm
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
