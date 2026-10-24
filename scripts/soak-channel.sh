#!/usr/bin/env bash
#
# soak-channel.sh [OPTIONS]
#
# Soaks a waypipe channel rather than a local socket: a compositor listening on
# one endpoint, a stock waypipe forwarding a churning client into it, and the
# census, the collection counts and the PSS curve reported together at the end.
#
# soak-session.sh drives a compositor over its own Wayland socket. This drives
# the other entry point, where every client buffer is a host-owned region the
# channel fills and diffs, and every descriptor is a token. That is the only
# shape a Windows client can have, so it is the shape worth soaking.
#
#   --seconds N     how long to churn, default 300
#   --windows N     how many windows the client keeps open, default 4
#   --rate N        client actions a second, default 20
#   --seed N        the client's seed, so a run repeats, default 1
#   --renderer NAME renderer to drive, default pixman
#   --client CMD    the remote client, default the BasinChurn in this tree
#   --compress NAME what both halves compress with: lz4, zstd or none, default lz4
#   --gpu           carry dmabuf: remote buffers become host regions
#   --video CODEC   carry per-buffer video too: h264, vp9 or av1; ,hw decodes on the GPU
#   --vsock [PORT]  carry the channel over AF_VSOCK through vsock_loopback
#                   rather than a unix socket; PORT defaults to 9800
#   --waypipe PATH  the waypipe binary, default the one on PATH
#   --interval N    seconds between samples, default 5
#   --out DIR       keep the samples and the logs here
#   --no-build      use the binaries already built
#
# Both halves run on this box: the point is the channel's own bookkeeping, not
# the network under it. Run it over a real link by hand when that is the
# question.
#
# A rising PSS is not by itself a leak, for the reason soak-session.sh gives.
# Read the census line first: it is the one that names a region, a pipe or a
# token the channel opened and never gave back.

set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
seconds=300
windows=4
rate=20
seed=1
renderer=pixman
client=
compress=lz4
gpu=0
video=none
waypipe=waypipe
interval=5
out=
build=1
vsock=

while [ $# -gt 0 ]; do
    case "$1" in
        --seconds) seconds=$2; shift 2 ;;
        --windows) windows=$2; shift 2 ;;
        --rate) rate=$2; shift 2 ;;
        --seed) seed=$2; shift 2 ;;
        --renderer) renderer=$2; shift 2 ;;
        --client) client=$2; shift 2 ;;
        --compress) compress=$2; shift 2 ;;
        --gpu) gpu=1; shift ;;
        --video) video=$2; shift 2 ;;
        --vsock)
            if [ $# -gt 1 ] && [[ $2 =~ ^[0-9]+$ ]]; then
                vsock=$2; shift 2
            else
                vsock=9800; shift
            fi ;;
        --waypipe) waypipe=$2; shift 2 ;;
        --interval) interval=$2; shift 2 ;;
        --out) out=$2; shift 2 ;;
        --no-build) build=0; shift ;;
        -h|--help) sed -n '3,35p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 1 ;;
    esac
done

if ! command -v "$waypipe" > /dev/null 2>&1 && [ ! -x "$waypipe" ]; then
    echo "no waypipe at '$waypipe'; build one or pass --waypipe PATH" >&2
    exit 1
fi

out_temporary=0
if [ -z "$out" ]; then
    out=$(mktemp -d)
    out_temporary=1
fi
mkdir -p "$out"

if [ "$build" = 1 ]; then
    dotnet build "$root/samples/BasinHeadless" -c Release -p:BasinCounters=true --nologo -v quiet
    dotnet build "$root/tests/BasinChurn" -c Release -p:BasinCounters=true --nologo -v quiet
fi

headless="$root/samples/BasinHeadless/bin/Release/net10.0/basinheadless"
churn="$root/tests/BasinChurn/bin/Release/net10.0/basinchurn"
[ -z "$client" ] && client="$churn --seed $seed --windows $windows --rate $rate --seconds $seconds"
if [ ! -x "$headless" ]; then
    echo "$headless is not built" >&2
    exit 1
fi

# A unix socket path is capped near 108 bytes, and waypipe puts its own name
# beside the one it is given, so the directory has to stay short.
runtime_dir=$(mktemp -d /tmp/bch.XXXX)
export XDG_RUNTIME_DIR=$runtime_dir
endpoint="$runtime_dir/chan.sock"
listen="$endpoint"
if [ -n "$vsock" ]; then
    # CID 1 is this machine, and only vsock_loopback answers on it.
    if [ ! -e /dev/vsock ] || ! grep -q '^vsock_loopback ' /proc/modules; then
        echo "vsock_loopback is not loaded; run sudo modprobe vsock_loopback" >&2
        exit 1
    fi
    listen="vsock:$vsock"
fi

headless_flags=()
[ "$gpu" = 1 ] && headless_flags+=(--gpu)
[ "$video" != none ] && headless_flags+=(--video "$video")
"$headless" --transport managed --renderer "$renderer" --frames 0 --alloc-report \
    --compress "$compress" "${headless_flags[@]}" --waypipe-listen "$listen" > "$out/compositor.log" 2>&1 &
compositor=$!
forwarder=

cleanup() {
    [ -n "$forwarder" ] && kill -TERM "$forwarder" 2>/dev/null || true
    kill -TERM "$compositor" 2>/dev/null || true
    wait "$compositor" 2>/dev/null || true
    rm -rf "$runtime_dir"
    [ "$out_temporary" = 1 ] && rm -rf "$out"
    return 0
}
trap cleanup EXIT

# The compositor says it is accepting once the endpoint is bound, which is the
# one signal that works for a socket file and for a vsock port alike.
bound=0
for _ in $(seq 1 100); do
    if grep -q 'accepting waypipe channels' "$out/compositor.log"; then
        bound=1
        break
    fi
    if ! kill -0 "$compositor" 2>/dev/null; then
        break
    fi
    sleep 0.2
done

if [ "$bound" = 0 ]; then
    echo "the compositor never bound $listen; see $out/compositor.log" >&2
    exit 1
fi

# waypipe client listens and waypipe server connects, which is why the
# compositor takes the listening side and this is the server half.
# shellcheck disable=SC2086
waypipe_flags=(--compress "$compress")
if [ "$gpu" = 0 ] && [ "$video" = none ]; then
    waypipe_flags+=(--no-gpu)
fi
[ "$video" != none ] && waypipe_flags+=("--video=${video%%,*}")
if [ -n "$vsock" ]; then
    waypipe_flags+=(--vsock --socket "1:$vsock")
else
    waypipe_flags+=(--socket "$endpoint")
fi
"$waypipe" "${waypipe_flags[@]}" server -- $client \
    > "$out/client.log" 2>&1 &
forwarder=$!

sleep 2
if ! kill -0 "$forwarder" 2>/dev/null; then
    echo "waypipe exited at once; see $out/client.log" >&2
    exit 1
fi

echo "SOAK endpoint=$listen seconds=$seconds windows=$windows rate=$rate renderer=$renderer compress=$compress"
echo "second,pss_kb,rss_kb" > "$out/samples.csv"

elapsed=0
while [ "$elapsed" -lt "$seconds" ] && kill -0 "$compositor" 2>/dev/null; do
    rollup=/proc/$compositor/smaps_rollup
    if [ -r "$rollup" ]; then
        pss=$(awk '/^Pss:/ { print $2 }' "$rollup")
        rss=$(awk '/^Rss:/ { print $2 }' "$rollup")
        echo "$elapsed,${pss:-0},${rss:-0}" >> "$out/samples.csv"
    fi

    sleep "$interval"
    elapsed=$((elapsed + interval))
done

# Take the channel down first, so the compositor sees the peer close rather
# than a socket that vanished under it, and its own teardown is the thing
# measured.
kill -TERM "$forwarder" 2>/dev/null || true
wait "$forwarder" 2>/dev/null || true
sleep 1
kill -TERM "$compositor" 2>/dev/null || true
wait "$compositor" 2>/dev/null || true
trap - EXIT
rm -rf "$runtime_dir"

alloc=$(grep -m1 '^ALLOC ' "$out/compositor.log" || true)
frames=$(grep -m1 '^FRAMES ' "$out/compositor.log" || true)

echo
echo "$frames"
echo "$alloc"

status=0

live=$(echo "$frames" | awk '{ print $4 }')
case "$live" in
    ""|untracked) echo "CENSUS untracked (rebuild with -p:BasinCounters=true to assert on it)" ;;
    0) echo "CENSUS clean" ;;
    *) echo "CENSUS $live objects still live at exit"; status=1 ;;
esac

for generation in gen0 gen1 gen2; do
    count=$(echo "$alloc" | tr ' ' '\n' | sed -n "s/^$generation=//p")
    if [ "${count:-0}" != "0" ]; then
        echo "COLLECTED $generation=$count"
    fi
done

if grep -qE 'channel [0-9]+ ended \(' "$out/compositor.log"; then
    echo "CHANNEL $(sed -nE 's/.*channel [0-9]+ ended \((.*)\)$/\1/p' "$out/compositor.log" | head -1)"
    status=1
fi

first=$(awk -F, 'NR == 2 { print $2 }' "$out/samples.csv")
last=$(awk -F, 'END { print $2 }' "$out/samples.csv")
peak=$(awk -F, 'NR > 1 && $2 > max { max = $2 } END { print max }' "$out/samples.csv")
samples=$(($(wc -l < "$out/samples.csv") - 1))

if [ "$samples" -lt 2 ]; then
    echo "PSS not sampled often enough to say anything (samples=$samples)"
else
    echo "PSS first=${first}kB last=${last}kB peak=${peak}kB over $samples samples"
    echo "PSS grew $((last - first))kB across the run"
fi

if [ "$out_temporary" = 1 ]; then
    rm -rf "$out"
    echo "(temporary: pass --out DIR to keep the samples and the logs)"
fi

exit $status
