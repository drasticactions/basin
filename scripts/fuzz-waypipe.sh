#!/usr/bin/env bash
#
# fuzz-waypipe.sh [OPTIONS]
#
# Runs AFL against the waypipe receiver. The harness in scripts/fuzz-waypipe
# reads one input as the bytes a waypipe server sends: the connection header and
# then frames. It replays the input into a fresh WaypipeChannel attached to an
# in-process display, so the connection header, the frame header, the fd tag,
# every engine message, the LZ4 and zstd paths and the protocol injector all see
# it. An exception other than WaypipeException is a crash.
#
# SharpFuzz instruments Basin.Transport.Waypipe.dll after the build, so only the
# receiver produces coverage. The run is JIT only and gates nothing.
#
#   --seconds N     stop after N seconds, default until interrupted
#   --out DIR       where the build, the seeds and the findings go,
#                   default /tmp/basin-fuzz-waypipe
#   --afl DIR       the AFL checkout, default ../AFL beside this repository,
#                   or $AFL_DIR
#   --no-build      reuse the instrumented build already in DIR
#   --minimize      minimize every crash in DIR and stop
#   --resume        continue the findings already in DIR
#
# The script takes AFL 2.53b. SharpFuzz states no supported version, but its
# install script builds 2.53b and its CI tests that build. Users report AFL++
# 4.09c works, and 4.40c worked here. AFL++ 5.00c accepts the SharpFuzz fork
# server but reads the whole map as covered, so it never keeps a new input.
# Build 2.53b once, beside this repository:
#
#   git clone https://github.com/google/AFL.git ../AFL
#   git -C ../AFL checkout v2.53b
#   AFL_NO_X86=1 make -C ../AFL afl-fuzz afl-showmap
#
# AFL_NO_X86 skips a compiler probe that current gcc rejects for its implicit
# int, which says nothing about afl-fuzz itself.
#
# Minimizing runs inside the harness, because afl-tmin replaces its input file
# and the SharpFuzz loop keeps reading the descriptor it started with, so every
# later run sees stale input. The harness drops chunks while the same exception
# comes from the same method.
#
# The seeds are tests/Basin.Tests/Corpus/waypipe. A file over 128 KB is left
# out, because AFL slows down on large seeds and mutates them badly. A crash
# goes back into that directory after it is minimized, named crash-*.bin, with
# the fix, so WaypipeFuzzTests replays and mutates it from then on.
#
# Needs the sharpfuzz tool:
#   dotnet tool install --global SharpFuzz.CommandLine --version 2.3.0

set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
out=/tmp/basin-fuzz-waypipe
afl=${AFL_DIR:-$root/../AFL}
seconds=
build=1
minimize=0
resume=0

while [ $# -gt 0 ]; do
    case "$1" in
        --seconds) seconds=$2; shift 2 ;;
        --out) out=$2; shift 2 ;;
        --afl) afl=$2; shift 2 ;;
        --no-build) build=0; shift ;;
        --minimize) minimize=1; shift ;;
        --resume) resume=1; shift ;;
        -h|--help) sed -n '2,48p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 1 ;;
    esac
done

bin="$out/bin"
harness="$bin/fuzz-waypipe"

if [ "$minimize" = 1 ]; then
    if [ ! -x "$harness" ]; then
        echo "no harness in $bin; run a fuzz first" >&2
        exit 1
    fi
    mkdir -p "$out/minimized"
    found=0
    for crash in "$out"/findings/crashes/id:*; do
        [ -e "$crash" ] || continue
        found=1
        name=$(basename "$crash" | cut -d, -f1 | tr ':' '-')
        "$harness" --minimize "$crash" "$out/minimized/crash-$name.bin" < /dev/null || true
    done
    if [ "$found" = 0 ]; then
        echo "no crashes under $out/findings"
    fi
    exit 0
fi

fuzzer="$afl/afl-fuzz"
if [ ! -x "$fuzzer" ]; then
    echo "no afl-fuzz in $afl; build AFL 2.53b there (see --help) or name it with --afl" >&2
    exit 1
fi
banner=$("$fuzzer" 2>&1 | head -1 || true)
if [[ "$banner" != *2.53b* ]]; then
    echo "$fuzzer is not AFL 2.53b, the version SharpFuzz's CI tests" >&2
    exit 1
fi

sharpfuzz=$(command -v sharpfuzz || echo "$HOME/.dotnet/tools/sharpfuzz")
if [ ! -x "$sharpfuzz" ]; then
    echo "sharpfuzz is not installed: dotnet tool install --global SharpFuzz.CommandLine --version 2.3.0" >&2
    exit 1
fi

# afl-fuzz refuses to start when core dumps go to a pipe, which systemd-coredump
# does. The harness reports a crash as an exception, so the core is not needed.
export AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1
export AFL_SKIP_CPUFREQ=1
export AFL_NO_AFFINITY=1
# afl-fuzz checks the target for its own instrumentation, and the target is the
# .NET host, which has none. The coverage comes from the rewritten assembly.
export AFL_SKIP_BIN_CHECK=1
export DOTNET_TieredCompilation=0
export DOTNET_gcServer=0

if [ "$build" = 1 ]; then
    rm -rf "$bin"
    dotnet publish "$root/scripts/fuzz-waypipe" -c Release -o "$bin" --nologo -v quiet
    "$sharpfuzz" "$bin/Basin.Transport.Waypipe.dll"
fi

if [ ! -x "$harness" ]; then
    echo "no harness in $bin; run without --no-build" >&2
    exit 1
fi

input="$out/in"
if [ "$resume" = 1 ]; then
    input=-
else
    rm -rf "$out/in" "$out/findings"
    mkdir -p "$out/in"
    for seed in "$root"/tests/Basin.Tests/Corpus/waypipe/*.bin; do
        if [ "$(stat -c %s "$seed")" -le 131072 ]; then
            cp "$seed" "$out/in/"
        fi
    done
fi

args=(-i "$input" -o "$out/findings" -t 10000 -m none)
if [ -n "$seconds" ]; then
    # 2.53b has no time limit of its own, and SIGINT is how it stops cleanly.
    timeout -s INT "$seconds" "$fuzzer" "${args[@]}" -- "$harness" || true
else
    "$fuzzer" "${args[@]}" -- "$harness" || true
fi

stats="$out/findings/fuzzer_stats"
if [ -f "$stats" ]; then
    grep -E '^(execs_done|execs_per_sec|paths_total|unique_crashes|unique_hangs) ' "$stats"
fi
crashes=$(find "$out/findings/crashes" -name 'id:*' 2> /dev/null | wc -l)
hangs=$(find "$out/findings/hangs" -name 'id:*' 2> /dev/null | wc -l)
echo "FUZZ crashes=$crashes hangs=$hangs findings=$out/findings"
if [ "$crashes" -gt 0 ]; then
    echo "replay them with: $harness --replay FILE..., minimize with: $0 --out $out --minimize" >&2
    exit 1
fi
