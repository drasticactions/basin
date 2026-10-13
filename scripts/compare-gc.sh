#!/usr/bin/env bash
#
# compare-gc.sh [OPTIONS]
#
# Runs one Inlet session under the stock .NET collector and under Satori, the
# experimental collector from github.com/VSadov/Satori, and reports what each
# cost the compositor. Every row drives the same workload: Inlet headless on
# pixman, InletWm managing it, a few weston-simple-shm clients on the frame
# path, and tests/BasinChurn opening, resizing, retitling and closing windows
# on a seeded schedule.
#
#   --rows LIST       comma-separated, default stock,satori,satori-nothp
#                       stock          NativeAOT, the stock runtime
#                       satori         NativeAOT, PublishWithSatoriGC
#                       satori-nothp   the same binary with DOTNET_gcTHP=0
#                       satori-lowlat  the same binary with DOTNET_GCLatencyMode=2
#                       jit-stock      self-contained CoreCLR, the stock runtime
#                       jit-satori     self-contained CoreCLR, Satori's runtime
#   --seconds N       how long the churn runs, default 60
#   --rate N          churn actions a second, default 50
#   --windows N       how many windows the churn keeps open, default 6
#   --clients N       weston-simple-shm clients beside the churn, default 2
#   --seed N          the churn's seed, default 1
#   --backend NAME    headless or drm, default headless. drm takes every
#                     connected connector through seatd, so run it from a VT
#                     or an ssh session with no compositor on the display
#   --renderer NAME   default pixman headless and vulkan on drm
#   --warmup N        seconds between the clients and the churn, default 2
#                     headless and 6 on drm, where a monitor can drop the link
#                     for a moment after a modeset
#   --repeats N       how many times to run every row, default 3
#   --interval N      seconds between PSS samples, default 1
#   --out DIR         keep the samples and the logs here
#   --no-build        use the binaries already published under --out
#
# One weston-presentation-shm runs beside the churn in feedback mode, so every
# frame it commits comes back with a wp_presentation timestamp and an MSC. A
# gap in the MSC is a vblank the client asked for and did not get. missed
# counts those gaps, and p99-us and max-us are the present-to-present interval.
# On drm these are real vblanks. Headless, the clock is a timer and the
# interval jitters by a millisecond on its own.
#
# The other columns are the compositor's own. cpu-ms is user plus system time from
# /proc over the whole run, which is where a collector's write barriers and its
# background threads show up. pause-ms is GC.GetTotalPauseDuration. pss-mean is
# the mean over the churn, and hwm is the peak resident set.
#
# Satori maps its heap with transparent huge pages unless DOTNET_gcTHP=0, so on
# a box with THP set to "always" its resident size measures the kernel's policy
# as much as the collector. The satori-nothp row is there to separate the two.
#
# Each build goes to its own --artifacts-path, so a Satori publish never shares
# obj/ with the stock one or with the tree's own builds.

set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
rows=(stock satori satori-nothp)
seconds=60
rate=50
windows=6
clients=2
seed=1
backend=headless
renderer=
warmup=
repeats=3
interval=1
out=
build=1

while [ $# -gt 0 ]; do
    case "$1" in
        --rows) IFS=, read -r -a rows <<< "$2"; shift 2 ;;
        --seconds) seconds=$2; shift 2 ;;
        --rate) rate=$2; shift 2 ;;
        --windows) windows=$2; shift 2 ;;
        --clients) clients=$2; shift 2 ;;
        --seed) seed=$2; shift 2 ;;
        --backend) backend=$2; shift 2 ;;
        --renderer) renderer=$2; shift 2 ;;
        --warmup) warmup=$2; shift 2 ;;
        --repeats) repeats=$2; shift 2 ;;
        --interval) interval=$2; shift 2 ;;
        --out) out=$2; shift 2 ;;
        --no-build) build=0; shift ;;
        -h|--help) sed -n '3,52p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 1 ;;
    esac
done

case "$backend" in
    headless) : "${renderer:=pixman}" "${warmup:=2}" ;;
    drm) : "${renderer:=vulkan}" "${warmup:=6}" ;;
    *) echo "unknown backend: $backend" >&2; exit 1 ;;
esac

out_temporary=0
if [ -z "$out" ]; then
    out=$(mktemp -d -t basin-gc-XXXXXX)
    out_temporary=1
else
    mkdir -p "$out"
    out=$(cd "$out" && pwd)
fi
logs=$out/logs
mkdir -p "$logs"

rid=linux-$(uname -m | sed 's/x86_64/x64/; s/aarch64/arm64/')
tick=$(getconf CLK_TCK)

# A row names a build and the environment its binary runs under.
build_of() {
    case "$1" in
        stock) echo aot-stock ;;
        satori|satori-nothp|satori-lowlat) echo aot-satori ;;
        jit-stock) echo jit-stock ;;
        jit-satori) echo jit-satori ;;
        *) return 1 ;;
    esac
}

env_of() {
    case "$1" in
        satori-nothp) echo DOTNET_gcTHP=0 ;;
        satori-lowlat) echo DOTNET_GCLatencyMode=2 ;;
        *) echo ;;
    esac
}

builds=()
for row in "${rows[@]}"; do
    b=$(build_of "$row") || { echo "unknown row: $row" >&2; exit 1; }
    case " ${builds[*]:-} " in *" $b "*) ;; *) builds+=("$b") ;; esac
done

publish() {
    local name=$1
    local args=(-c Release -r "$rid" --nologo -v quiet
        --artifacts-path "$out/artifacts/$name" -o "$out/$name")
    case "$name" in
        aot-satori) args+=(-p:BasinSatori=true) ;;
        jit-stock) args+=(-p:PublishAot=false --self-contained true) ;;
        jit-satori) args+=(-p:PublishAot=false --self-contained true -p:BasinSatori=true) ;;
    esac
    echo "publishing Inlet: $name"
    dotnet publish "$root/samples/Inlet" "${args[@]}" > "$logs/publish-$name.log" 2>&1 || {
        echo "publish $name failed, see $logs/publish-$name.log" >&2
        exit 1
    }
}

if [ "$build" = 1 ]; then
    for b in "${builds[@]}"; do
        publish "$b"
    done
    echo "building InletWm and BasinChurn"
    dotnet build "$root/samples/InletWm" -c Release --nologo -v quiet > "$logs/build-wm.log" 2>&1
    dotnet build "$root/tests/BasinChurn" -c Release --nologo -v quiet > "$logs/build-churn.log" 2>&1
fi

manager=$root/samples/InletWm/bin/Release/net10.0/inletwm
churn=$root/tests/BasinChurn/bin/Release/net10.0/basinchurn
for binary in "$manager" "$churn"; do
    [ -x "$binary" ] || { echo "$binary is not built" >&2; exit 1; }
done
for b in "${builds[@]}"; do
    [ -x "$out/$b/inlet" ] || { echo "no inlet at $out/$b/inlet" >&2; exit 1; }
done
if [ "$clients" -gt 0 ] && ! command -v weston-simple-shm >/dev/null 2>&1; then
    echo "no weston-simple-shm on PATH; pass --clients 0" >&2
    exit 1
fi
if ! command -v weston-presentation-shm >/dev/null 2>&1; then
    echo "no weston-presentation-shm on PATH" >&2
    exit 1
fi

compositor=
wm=
churn_pid=
probe=
client_pids=()
runtime=

stop_run() {
    set +e
    local pid
    for pid in "$churn_pid" "$probe" "${client_pids[@]:-}" "$wm"; do
        [ -n "$pid" ] && kill -TERM "$pid" 2>/dev/null
    done
    for pid in "$churn_pid" "$probe" "${client_pids[@]:-}" "$wm"; do
        [ -n "$pid" ] && wait "$pid" 2>/dev/null
    done
    if [ -n "$compositor" ]; then
        kill -TERM "$compositor" 2>/dev/null
        for _ in $(seq 1 100); do
            kill -0 "$compositor" 2>/dev/null || break
            sleep 0.1
        done
        kill -9 "$compositor" 2>/dev/null
        wait "$compositor" 2>/dev/null
    fi
    compositor= wm= churn_pid= probe=
    client_pids=()
    [ -n "$runtime" ] && rm -rf "$runtime"
    runtime=
    set -e
    return 0
}

cleanup() {
    trap - EXIT INT TERM
    stop_run
    [ "$out_temporary" = 1 ] && rm -rf "$out"
    return 0
}
trap cleanup EXIT INT TERM

cpu_ticks() {
    awk '{ print $14 + $15 }' "/proc/$1/stat" 2>/dev/null
}

field() {
    echo "$2" | tr ' ' '\n' | sed -n "s/^$1=//p"
}

# Prints "frames missed p99-us max-us" for a weston-presentation-shm log taken
# from the start of the churn on.
presentation_stats() {
    local log=$1 from=$2
    awk -v from="$from" '
        / seq [0-9]+/ {
            n = $1; sub(":", "", n)
            if (n + 0 < from + 0) next
            for (i = 1; i <= NF; i++) {
                if ($i == "p2p") p2p = $(i + 1)
                if ($i == "seq") seq = $(i + 1)
            }
            if (have) {
                gap = seq - last - 1
                if (gap > 0) missed += gap
                print p2p > "/dev/stderr"
            }
            last = seq; have = 1; frames++
        }
        END { printf "%d %d", frames, missed }
    ' "$log" 2> "$log.p2p"
    local sorted count p99 max
    sorted=$(sort -n "$log.p2p")
    count=$(echo "$sorted" | grep -c . || true)
    if [ "$count" -gt 0 ]; then
        p99=$(echo "$sorted" | sed -n "$(( (count * 99 + 99) / 100 ))p")
        max=$(echo "$sorted" | tail -1)
    else
        p99=0 max=0
    fi
    echo " $p99 $max"
}

results=$out/results.csv
echo "row,repeat,startup_ms,cpu_ms,pause_ms,gen0,gen1,gen2,alloc_bytes,pss_mean_kb,pss_last_kb,hwm_kb,frames,missed,p99_us,max_us,gc" > "$results"

run_once() {
    local row=$1 repeat=$2
    local binary=$out/$(build_of "$row")/inlet
    local extra
    extra=$(env_of "$row")
    local log=$logs/$row-$repeat.log
    local samples=$logs/$row-$repeat-pss.csv
    local socket= start end startup

    runtime=$(mktemp -d -t basin-gc-run-XXXXXX)
    start=$(date +%s%N)
    local outputs=(--outputs 1)
    [ "$backend" = drm ] && outputs=()
    XDG_RUNTIME_DIR=$runtime LIBSEAT_BACKEND=seatd env $extra "$binary" --backend "$backend" \
        --renderer "$renderer" "${outputs[@]}" --alloc-report -c true > "$log" 2>&1 &
    compositor=$!
    for _ in $(seq 1 3000); do
        socket=$(sed -n 's/^SOCKET //p' "$log" | head -1)
        [ -n "$socket" ] && break
        kill -0 "$compositor" 2>/dev/null || break
        sleep 0.01
    done
    end=$(date +%s%N)
    if [ -z "$socket" ]; then
        echo "  $row #$repeat: no socket, see $log" >&2
        stop_run
        return 1
    fi
    startup=$(((end - start) / 1000000))

    XDG_RUNTIME_DIR=$runtime "$manager" --socket "$socket" > "$logs/$row-$repeat-wm.log" 2>&1 &
    wm=$!
    sleep 1
    local i
    for i in $(seq 1 "$clients"); do
        XDG_RUNTIME_DIR=$runtime WAYLAND_DISPLAY=$socket weston-simple-shm \
            > "$logs/$row-$repeat-client-$i.log" 2>&1 &
        client_pids+=($!)
    done
    local probe_log=$logs/$row-$repeat-presentation.log
    XDG_RUNTIME_DIR=$runtime WAYLAND_DISPLAY=$socket stdbuf -oL weston-presentation-shm -f \
        > "$probe_log" 2>&1 &
    probe=$!
    sleep "$warmup"
    local probe_from
    probe_from=$(awk '/ seq [0-9]+/ { n = $1 } END { sub(":", "", n); print n + 1 }' "$probe_log")

    local ticks_before
    ticks_before=$(cpu_ticks "$compositor")
    XDG_RUNTIME_DIR=$runtime "$churn" --socket "$socket" --seed "$seed" --windows "$windows" \
        --rate "$rate" --seconds "$seconds" > "$logs/$row-$repeat-churn.log" 2>&1 &
    churn_pid=$!

    echo "second,pss_kb" > "$samples"
    local elapsed=0
    while kill -0 "$churn_pid" 2>/dev/null && kill -0 "$compositor" 2>/dev/null; do
        local pss
        pss=$(awk '/^Pss:/ { print $2 }' "/proc/$compositor/smaps_rollup" 2>/dev/null || true)
        [ -n "$pss" ] && echo "$elapsed,$pss" >> "$samples"
        sleep "$interval"
        elapsed=$((elapsed + interval))
    done
    wait "$churn_pid" 2>/dev/null || true
    churn_pid=

    if ! kill -0 "$compositor" 2>/dev/null; then
        echo "  $row #$repeat: the compositor died, see $log" >&2
        stop_run
        return 1
    fi
    local ticks_after hwm
    ticks_after=$(cpu_ticks "$compositor")
    hwm=$(awk '/^VmHWM:/ { print $2 }' "/proc/$compositor/status")
    kill -TERM "$probe" 2>/dev/null || true
    wait "$probe" 2>/dev/null || true
    probe=
    stop_run
    local frames missed p99 max
    read -r frames missed p99 max <<< "$(presentation_stats "$probe_log" "$probe_from")"

    local alloc
    alloc=$(grep -m1 '^ALLOC ' "$log" || true)
    if [ -z "$alloc" ]; then
        echo "  $row #$repeat: no ALLOC line, see $log" >&2
        return 1
    fi
    local cpu_ms=$(((ticks_after - ticks_before) * 1000 / tick))
    local pss_mean pss_last
    pss_mean=$(awk -F, 'NR > 1 { s += $2; n++ } END { if (n) printf "%d", s / n; else print 0 }' "$samples")
    pss_last=$(awk -F, 'END { print $2 }' "$samples")

    echo "$row,$repeat,$startup,$cpu_ms,$(field pause-ms "$alloc"),$(field gen0 "$alloc"),$(field gen1 "$alloc"),$(field gen2 "$alloc"),$(field bytes "$alloc"),$pss_mean,$pss_last,$hwm,$frames,$missed,$p99,$max,$(field gc "$alloc")" >> "$results"
    echo "  $row #$repeat: startup=${startup}ms cpu=${cpu_ms}ms $(echo "$alloc" | cut -d' ' -f2-) pss-mean=${pss_mean}kB hwm=${hwm}kB frames=$frames missed=$missed p99=${p99}us max=${max}us"
}

echo "GC backend=$backend renderer=$renderer rows=${rows[*]} seconds=$seconds rate=$rate windows=$windows clients=$clients repeats=$repeats"
for repeat in $(seq 1 "$repeats"); do
    for row in "${rows[@]}"; do
        run_once "$row" "$repeat" || true
    done
done

echo
echo "median over $repeats repeats"
printf '%-14s %10s %8s %9s %6s %6s %6s %12s %10s %9s %7s %7s %8s %8s %-12s\n' \
    row startup-ms cpu-ms pause-ms gen0 gen1 gen2 alloc-bytes pss-mean hwm frames missed p99-us max-us gc
for row in "${rows[@]}"; do
    awk -F, -v row="$row" '
        function median(column,    n, i, j, t, v) {
            n = 0
            for (i = 1; i <= count; i++) { v[++n] = value[i, column] }
            for (i = 1; i <= n; i++) for (j = i + 1; j <= n; j++) if (v[j] < v[i]) { t = v[i]; v[i] = v[j]; v[j] = t }
            if (n == 0) return "-"
            return (n % 2) ? v[(n + 1) / 2] : (v[n / 2] + v[n / 2 + 1]) / 2
        }
        NR > 1 && $1 == row {
            count++
            for (c = 3; c <= 16; c++) value[count, c] = $c + 0
            gc = $17
        }
        END {
            if (!count) { printf "%-14s %s\n", row, "no complete run"; exit }
            printf "%-14s %10s %8s %9s %6s %6s %6s %12s %10s %9s %7s %7s %8s %8s %-12s\n", row,
                median(3), median(4), median(5), median(6), median(7), median(8),
                median(9), median(10) "kB", median(12) "kB",
                median(13), median(14), median(15), median(16), gc
        }
    ' "$results"
done

if [ "$out_temporary" = 0 ]; then
    echo
    echo "results in $results, logs in $logs"
fi
