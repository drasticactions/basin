#!/usr/bin/env bash
#
# install-programs.sh [OPTIONS] [NAME...]
#
# Publishes the sample compositors, the window managers, Inlet and Dam with
# NativeAOT, then copies each binary and the native libraries beside it into
# one directory. The default is ~/.local/bin, and an existing file of the
# same name is overwritten.
#
# Naming programs installs those instead, by name or by path. A name is the
# binary's, which is the project's assembly name rather than its directory:
# retro-wm, not RetroWm. The client samples and Waylonia are not in the
# default set, and a name still installs any of them.
#
# The version defaults to 0.1.0-local.g<commit>, with .dirty when tracked
# files differ from the commit, so an installed binary never claims a version
# CI can also mint.
#
# Waylonia installs for the host only. A release of it is one run of
# build-waylonia.sh on each of the three platforms it runs on, and a macOS
# install here is the host's own slice rather than the universal binary.
#
# A .desktop file in a publish is installed into the desktop database under
# $XDG_DATA_HOME instead, because that is where a portal reads it.
#
#   --version V   version to stamp, default 0.1.0-local.g<commit>
#   --rid RID     runtime identifier, default the host's
#   --out DIR     where the programs are installed, default ~/.local/bin
#   -n, --dry-run print what an install will do, and change nothing
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

projects=()
version=
rid=
out="${XDG_BIN_HOME:-$HOME/.local/bin}"
dry=0
excluded=(apps/Waylonia samples/BlurClient samples/WorkspacePager)

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --rid) rid=${2:?--rid needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        -n|--dry-run) dry=1; shift ;;
        -h|--help)
            sed -n '3,29p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        -*)
            echo "unknown argument '$1'" >&2
            exit 1
            ;;
        *)
            projects+=("$1")
            shift
            ;;
    esac
done

install_file() {
    local mode=$1 source=$2 destination=$3 temporary
    temporary=$(mktemp "$(dirname -- "$destination")/.install.XXXXXX")
    cp "$source" "$temporary"
    chmod "$mode" "$temporary"
    mv -f "$temporary" "$destination"
}

is_excluded() {
    local project=$1 name
    for name in "${excluded[@]}"; do
        [ "$project" = "$name" ] && return 0
    done
    return 1
}

if [ ${#projects[@]} -eq 0 ]; then
    for directory in "$root"/apps/*/ "$root"/samples/*/; do
        project=${directory#"$root"/}
        project=${project%/}
        candidates=("$directory"*.csproj)
        if ! is_excluded "$project" && [ -f "${candidates[0]}" ]; then
            projects+=("$project")
        fi
    done
else
    resolved=()
    for wanted in "${projects[@]}"; do
        if ! path=$(resolve_program "$wanted"); then
            echo "unknown program '$wanted':" >&2
            ( cd "$root" && ls -d apps/*/ samples/*/ | sed 's:/$::; s:^:  :' ) >&2
            exit 1
        fi

        resolved+=("$path")
    done

    projects=("${resolved[@]}")
fi

host=$(host_rid)
: "${rid:=$host}"
warn_cross_rid "$rid" "$host"

: "${version:=$(local_version)}"

if [ "$dry" -eq 1 ]; then
    echo "version $version, rid $rid"
    echo "install into $out"
    for project in "${projects[@]}"; do
        printf '  %s  (%s)\n' "$(program_name "$project")" "$project"
    done
    exit 0
fi

applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"

mkdir -p "$out"
out=$(cd "$out" && pwd)
stage=$(mktemp -d "${TMPDIR:-/tmp}/basin-install.XXXXXX")

trap 'rm -rf "$stage"' EXIT

echo "version $version, rid $rid"
installed=()

for project in "${projects[@]}"; do
    name=$(program_name "$project")
    echo
    echo "publishing $project"
    publish_program "$project" "$stage/$name" "$version" "$rid"

    rm -f "$stage/$name/LICENSE" "$stage/$name/README.md"

    for file in "$stage/$name"/*; do
        [ -f "$file" ] || continue
        base=$(basename "$file")
        case "$base" in
            *.desktop)
                mkdir -p "$applications"
                install_file 644 "$file" "$applications/$base"
                installed+=("$applications/$base")
                ;;
            *)
                if [ -x "$file" ]; then
                    install_file 755 "$file" "$out/$base"
                else
                    install_file 644 "$file" "$out/$base"
                fi

                installed+=("$out/$base")
                ;;
        esac
    done

    rm -rf "$stage/$name"
done

if command -v update-desktop-database >/dev/null 2>&1 && [ -d "$applications" ]; then
    update-desktop-database "$applications" 2>/dev/null || true
fi

echo
printf '%s\n' "${installed[@]}" | sort -u

case ":$PATH:" in
    *":$out:"*) ;;
    *) echo; echo "warning: $out is not on PATH, so an installed program will not start by name." >&2 ;;
esac
echo
