#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: scripts/compare-plasma.sh [OPTIONS]

Compares plasma-host against kwin_wayland three ways, headless:

  coverage   every global each compositor advertises, from wayland-info,
             diffed by name and version. The MISSING section is the claim
             check: what a client can use on kwin and not on plasma-host.
  smoke      real KDE clients started against both compositors, judged by
             a window appearing in org_kde_plasma_window_management.
  bench      a Plasma desktop on each: seconds to the socket, then PSS for
             the compositor, plasmashell and the clients through two
             phases (desktop settled, then N clients).

  --rows LIST        compositors to run, from plasma-host, plasma-host-aot
                     and kwin, default plasma-host,kwin
  --skip-coverage    leave out the globals diff
  --skip-smoke       leave out the client smoke
  --skip-bench       leave out the desktop bench
  --smoke-clients L  ;-separated smoke commands, default
                     "plasmawindowed org.kde.plasma.digitalclock"
  --client CMD       bench client, default weston-simple-shm
  --clients N        bench client count, default 4
  --settle N         seconds for plasmashell to come up, default 75
  --phase-seconds N  seconds sampled per bench phase, default 15
  --out DIR          keep samples and logs here, default a temp dir
  --gate             exit non-zero when kwin advertises a global
                     plasma-host does not
  --no-build         skip building plasma-host

kwin runs with KWIN_WAYLAND_NO_PERMISSION_CHECKS=1 so the privileged
globals it hides from ordinary clients appear in the diff and the smoke
probe can read its window list.
EOF
}

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
rows=plasma-host,kwin
do_coverage=1
do_smoke=1
do_bench=1
smoke_clients="plasmawindowed org.kde.plasma.digitalclock"
client="weston-simple-shm"
client_count=4
settle=75
phase_seconds=15
out=
gate=0
build=1

while [ $# -gt 0 ]; do
    case "$1" in
        --rows) rows="$2"; shift 2 ;;
        --skip-coverage) do_coverage=0; shift ;;
        --skip-smoke) do_smoke=0; shift ;;
        --skip-bench) do_bench=0; shift ;;
        --smoke-clients) smoke_clients="$2"; shift 2 ;;
        --client) client="$2"; shift 2 ;;
        --clients) client_count="$2"; shift 2 ;;
        --settle) settle="$2"; shift 2 ;;
        --phase-seconds) phase_seconds="$2"; shift 2 ;;
        --out) out="$2"; shift 2 ;;
        --gate) gate=1; shift ;;
        --no-build) build=0; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown argument: $1" >&2; usage >&2; exit 1 ;;
    esac
done

[ -n "$out" ] || out=$(mktemp -d -t compare-plasma-XXXXXX)
mkdir -p "$out"
samples=$out/samples.csv
echo "row,phase,who,elapsed,pss_kb,rss_kb,private_kb,anon_kb,swap_kb" > "$samples"
echo "results in $out"

plasmawins=$root/scripts/wlclients/bin/plasmawins
make -C "$root/scripts/wlclients" bin/plasmawins >/dev/null

binary=
if [[ ",$rows," == *,plasma-host,* ]]; then
    if [ "$build" -eq 1 ]; then
        echo "building plasma-host (Release)"
        dotnet build "$root/samples/PlasmaHost" -c Release --nologo -v quiet
    fi
    binary=$(find "$root/samples/PlasmaHost/bin/Release" -maxdepth 2 -mindepth 2 \
        -name plasma-host -type f -perm -u+x 2>/dev/null | head -1)
    if [ -z "$binary" ]; then
        echo "no Release build of plasma-host found; run without --no-build" >&2
        exit 1
    fi
fi

aot_binary=
if [[ ",$rows," == *,plasma-host-aot,* ]]; then
    if [ "$build" -eq 1 ]; then
        echo "publishing plasma-host (NativeAOT)"
        dotnet publish "$root/samples/PlasmaHost" -c Release -r linux-x64 --nologo -v quiet
    fi
    aot_binary=$(find "$root/samples/PlasmaHost/bin/Release" -path "*/publish/plasma-host" \
        -type f -perm -u+x 2>/dev/null | head -1)
    if [ -z "$aot_binary" ]; then
        echo "no NativeAOT publish of plasma-host found; run without --no-build" >&2
        exit 1
    fi
fi

comp_pid=
dbus_pid=
shell_pid=
client_pids=()
socket=
status=0

kde_vars=()

refresh_kde_vars() {
    kde_vars=(
        WAYLAND_DISPLAY="$socket"
        QT_QPA_PLATFORM=wayland
        XDG_CURRENT_DESKTOP=KDE
        XDG_SESSION_TYPE=wayland
        DBUS_SESSION_BUS_ADDRESS="$bus_address"
        XDG_DATA_DIRS="${XDG_DATA_DIRS:-/usr/local/share:/usr/share}"
        XDG_CONFIG_DIRS="${XDG_CONFIG_DIRS:-/etc/xdg}"
        QT_QUICK_BACKEND=software
    )
}

kde_env() {
    env "${kde_vars[@]}" "$@"
}

cleanup_row() {
    set +e
    for pid in ${client_pids[@]+"${client_pids[@]}"}; do kill "$pid" 2>/dev/null; done
    client_pids=()
    [ -n "$shell_pid" ] && kill "$shell_pid" 2>/dev/null
    shell_pid=
    [ -n "$dbus_pid" ] && kill "$dbus_pid" 2>/dev/null
    dbus_pid=
    if [ -n "$comp_pid" ]; then
        kill "$comp_pid" 2>/dev/null
        for _ in $(seq 1 50); do kill -0 "$comp_pid" 2>/dev/null || break; sleep 0.1; done
        kill -9 "$comp_pid" 2>/dev/null
    fi
    comp_pid=
    socket=
    bus_address=
    wait 2>/dev/null
    set -e
}
trap 'cleanup_row; exit "$status"' EXIT INT TERM

start_compositor() {
    local row=$1 log=$2
    case "$row" in
        plasma-host|plasma-host-aot)
            local run=$binary
            [ "$row" = plasma-host-aot ] && run=$aot_binary
            "$run" --backend headless --shell false >"$log" 2>&1 &
            comp_pid=$!
            for _ in $(seq 1 300); do
                socket=$(sed -n 's/^SOCKET //p' "$log" | head -1)
                [ -n "$socket" ] && break
                kill -0 "$comp_pid" 2>/dev/null || { echo "$row died at startup; see $log" >&2; return 1; }
                sleep 0.1
            done
            ;;
        kwin)
            command -v kwin_wayland >/dev/null || { echo "kwin_wayland is not installed" >&2; return 1; }
            socket=wayland-cmp
            env KWIN_WAYLAND_NO_PERMISSION_CHECKS=1 \
                kwin_wayland --virtual --socket "$socket" --no-lockscreen >"$log" 2>&1 &
            comp_pid=$!
            for _ in $(seq 1 300); do
                [ -S "${XDG_RUNTIME_DIR}/$socket" ] && break
                kill -0 "$comp_pid" 2>/dev/null || { echo "kwin died at startup; see $log" >&2; return 1; }
                sleep 0.1
            done
            ;;
        *)
            echo "unknown row: $row" >&2
            return 1
            ;;
    esac
    [ -n "$socket" ] || { echo "$row bound no socket within 30s" >&2; return 1; }
}

start_bus() {
    local address_file
    address_file=$(mktemp -t compare-plasma-dbus-XXXXXX)
    WAYLAND_DISPLAY="$socket" QT_QPA_PLATFORM=wayland XDG_CURRENT_DESKTOP=KDE \
        dbus-daemon --session --fork --print-address=3 --print-pid=4 \
        3>"$address_file" 4>"$address_file.pid"
    bus_address=$(cat "$address_file")
    dbus_pid=$(cat "$address_file.pid")
    rm -f "$address_file" "$address_file.pid"
    refresh_kde_vars
}

read_smaps() {
    local pid=$1 raw
    [ -e "/proc/$pid/smaps_rollup" ] || return 1
    raw=$(cat "/proc/$pid/smaps_rollup" 2>/dev/null) || raw=
    if [ -z "$raw" ]; then
        raw=$(sudo -n cat "/proc/$pid/smaps_rollup" 2>/dev/null) || return 1
    fi
    [ -n "$raw" ] || return 1
    awk '
        /^Rss:/ { rss = $2 }
        /^Pss:/ { pss = $2 }
        /^Private_Clean:/ { private += $2 }
        /^Private_Dirty:/ { private += $2 }
        /^Anonymous:/ { anon = $2 }
        /^Swap:/ { swap = $2 }
        END { printf "%d %d %d %d %d", pss, rss, private, anon, swap }
    ' <<< "$raw"
}

emit() {
    local row=$1 phase=$2 who=$3 elapsed=$4 pid=$5 values
    values=$(read_smaps "$pid") || return 0
    [ -n "$values" ] || return 0
    echo "$row,$phase,$who,$elapsed,${values// /,}" >> "$samples"
}

emit_clients() {
    local row=$1 phase=$2 elapsed=$3
    local pid values pss=0 rss=0 private=0 anon=0 swap=0 seen=0
    for pid in ${client_pids[@]+"${client_pids[@]}"}; do
        values=$(read_smaps "$pid") || continue
        [ -n "$values" ] || continue
        read -r a b c d e <<< "$values"
        pss=$((pss + a)); rss=$((rss + b)); private=$((private + c))
        anon=$((anon + d)); swap=$((swap + e)); seen=1
    done
    [ "$seen" = 1 ] || return 0
    echo "$row,$phase,clients,$elapsed,$pss,$rss,$private,$anon,$swap" >> "$samples"
}

window_count() {
    kde_env timeout 5 "$plasmawins" 2>/dev/null | grep -c '^WINDOW' || true
}

IFS=, read -ra row_list <<< "$rows"

if [ "$do_coverage" -eq 1 ]; then
    echo
    echo "== coverage =="
    for row in "${row_list[@]}"; do
        start_compositor "$row" "$out/coverage-$row.log"
        sleep 1
        WAYLAND_DISPLAY="$socket" timeout 15 wayland-info 2>/dev/null \
            | sed -n "s/^interface: '\([^']*\)',[[:space:]]*version:[[:space:]]*\([0-9]*\).*/\1 \2/p" \
            | sort > "$out/globals-$row.txt"
        echo "$row advertises $(wc -l < "$out/globals-$row.txt") globals"
        cleanup_row
    done

    if [ ${#row_list[@]} -eq 2 ]; then
        a=$out/globals-${row_list[0]}.txt
        b=$out/globals-${row_list[1]}.txt
        missing=$(join -v2 -j1 <(cut -d' ' -f1 "$a") <(cut -d' ' -f1 "$b"))
        extra=$(join -v1 -j1 <(cut -d' ' -f1 "$a") <(cut -d' ' -f1 "$b"))
        behind=$(join -j1 "$a" "$b" | awk '$2 < $3 { printf "%s  %s=%s %s=%s\n", $1, r1, $2, r2, $3 }' \
            r1="${row_list[0]}" r2="${row_list[1]}")
        echo
        echo "-- globals ${row_list[1]} has and ${row_list[0]} is MISSING --"
        if [ -n "$missing" ]; then echo "$missing"; else echo "(none)"; fi
        echo
        echo "-- versions where ${row_list[0]} is behind ${row_list[1]} --"
        if [ -n "$behind" ]; then echo "$behind"; else echo "(none)"; fi
        echo
        echo "-- globals only ${row_list[0]} has --"
        if [ -n "$extra" ]; then echo "$extra"; else echo "(none)"; fi
        if [ "$gate" -eq 1 ] && [ -n "$missing" ]; then
            status=1
        fi
    fi
fi

if [ "$do_smoke" -eq 1 ]; then
    echo
    echo "== smoke =="
    IFS=';' read -ra smoke_list <<< "$smoke_clients"
    for row in "${row_list[@]}"; do
        start_compositor "$row" "$out/smoke-$row.log"
        start_bus
        for cmd in "${smoke_list[@]}"; do
            cmd=$(echo "$cmd" | sed 's/^ *//;s/ *$//')
            [ -n "$cmd" ] || continue
            before=$(window_count)
            env "${kde_vars[@]}" $cmd >"$out/smoke-$row-${cmd%% *}.log" 2>&1 &
            probe_pid=$!
            verdict=FAIL
            for _ in $(seq 1 90); do
                if [ "$(window_count)" -gt "$before" ]; then verdict=PASS; break; fi
                kill -0 "$probe_pid" 2>/dev/null || break
                sleep 0.5
            done
            kill "$probe_pid" 2>/dev/null || true
            echo "$row: $cmd -> $verdict"
            [ "$verdict" = FAIL ] && status=1
        done
        if kde_env timeout 15 kscreen-doctor -o >"$out/smoke-$row-kscreen.log" 2>&1; then
            echo "$row: kscreen-doctor -o -> PASS"
        else
            echo "$row: kscreen-doctor -o -> FAIL"
            status=1
        fi
        cleanup_row
    done
fi

if [ "$do_bench" -eq 1 ]; then
    echo
    echo "== bench =="
    for row in "${row_list[@]}"; do
        t0=$SECONDS
        start_compositor "$row" "$out/bench-$row.log"
        socket_seconds=$((SECONDS - t0))
        start_bus
        env "${kde_vars[@]}" plasmashell >"$out/bench-$row-shell.log" 2>&1 &
        shell_pid=$!
        echo "$row: socket after ${socket_seconds}s, settling ${settle}s for plasmashell"
        sleep "$settle"
        if ! kill -0 "$comp_pid" 2>/dev/null || ! kill -0 "$shell_pid" 2>/dev/null; then
            echo "$row: the session died during settle -> INCOMPLETE" >&2
            status=1
            cleanup_row
            continue
        fi

        for i in $(seq 1 "$phase_seconds"); do
            emit "$row" desktop compositor "$i" "$comp_pid"
            emit "$row" desktop shell "$i" "$shell_pid"
            sleep 1
        done

        for _ in $(seq 1 "$client_count"); do
            WAYLAND_DISPLAY="$socket" $client >/dev/null 2>&1 &
            client_pids+=($!)
        done
        sleep 5
        for i in $(seq 1 "$phase_seconds"); do
            emit "$row" clients compositor "$i" "$comp_pid"
            emit "$row" clients shell "$i" "$shell_pid"
            emit_clients "$row" clients "$i"
            sleep 1
        done

        echo "$row,startup,socket,0,$((socket_seconds * 1000)),0,0,0,0" >> "$samples"
        cleanup_row
    done

    echo
    printf '%-14s %-9s %-11s %s\n' row phase who "mean PSS (MB)"
    awk -F, '
        NR == 1 { next }
        $2 == "startup" { startup[$1] = $5 / 1000; next }
        { key = $1 "," $2 "," $3; sum[key] += $5; n[key]++ }
        END {
            for (key in sum) {
                split(key, k, ",")
                printf "%-14s %-9s %-11s %.1f\n", k[1], k[2], k[3], sum[key] / n[key] / 1024
            }
            for (row in startup) {
                printf "%-14s startup to socket: %ss\n", row, startup[row]
            }
        }
    ' "$samples" | sort
fi

exit "$status"
