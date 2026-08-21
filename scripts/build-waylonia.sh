#!/usr/bin/env bash
#
# build-waylonia.sh [OPTIONS]
#
#
#   --version V   version to stamp, default 0.1.0-local.g<commit>
#   --rid RID     runtime identifier, default the host's, or osx-universal on
#                 a Mac
#   --out DIR     where the zip is written, default artifacts/
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

project=apps/Waylonia
slices=(osx-x64 osx-arm64)
version=
rid=
out="$root/artifacts"

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --rid) rid=${2:?--rid needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        -h|--help)
            sed -n '3,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown argument '$1'" >&2
            exit 1
            ;;
    esac
done

host=$(host_rid)
if [ -z "$rid" ]; then
    case "$host" in
        osx-*) rid=osx-universal ;;
        *) rid=$host ;;
    esac
fi

if [ "$rid" = osx-universal ]; then
    case "$host" in
        osx-*) ;;
        *) warn_cross_rid "$rid" "$host" ;;
    esac

    if ! command -v lipo >/dev/null 2>&1; then
        echo "error: lipo is required to build osx-universal binaries" >&2
        exit 1
    fi
else
    warn_cross_rid "$rid" "$host"
fi

: "${version:=$(local_version)}"

name=$(program_name "$project")
folder="$name-$version-$rid"

mkdir -p "$out"
out=$(cd "$out" && pwd)
stage="$out/stage-$name"
rm -rf "$stage"

trap 'rm -rf "$stage"' EXIT

merge_mach_o() {
    local primary=$1 secondary=$2 destination=$3 label=$4
    local primary_arches secondary_arches arch missing=() inputs=() thin status=0

    primary_arches=$(lipo -archs "$primary" 2>/dev/null) || return 2
    secondary_arches=$(lipo -archs "$secondary" 2>/dev/null) || return 2

    for arch in $secondary_arches; do
        case " $primary_arches " in
            *" $arch "*) ;;
            *) missing+=("$arch") ;;
        esac
    done

    if [ ${#missing[@]} -eq 0 ]; then
        if ! cmp -s "$primary" "$secondary"; then
            echo "warning: $label carries no architecture the other slice lacks but differs from it, so $(basename "$(dirname "$primary")")'s copy ships." >&2
        fi
        cp "$primary" "$destination"
        return 0
    fi

    inputs=("$primary")
    if [ "${#missing[@]}" -eq "$(set -- $secondary_arches; echo $#)" ]; then
        inputs+=("$secondary")
    else
        thin=$(mktemp -d)
        for arch in "${missing[@]}"; do
            lipo -thin "$arch" "$secondary" -output "$thin/$arch" || status=1
            inputs+=("$thin/$arch")
        done
    fi

    if [ "$status" -eq 0 ]; then
        lipo -create "${inputs[@]}" -output "$destination" || status=1
    fi

    if [ -n "${thin:-}" ]; then
        rm -rf "$thin"
    fi

    return "$status"
}

merge_slices() {
    local primary=$1 secondary=$2 destination=$3
    mkdir -p "$destination"

    ( cd "$primary" && find . -type f -print0 ) | while IFS= read -r -d '' relative; do
        mkdir -p "$destination/$(dirname "$relative")"
        if [ ! -f "$secondary/$relative" ]; then
            echo "warning: ${relative#./} is in $(basename "$primary") only, so it ships as that slice alone." >&2
            cp "$primary/$relative" "$destination/$relative"
        else
            merge_mach_o "$primary/$relative" "$secondary/$relative" \
                "$destination/$relative" "${relative#./}" || case $? in
                2) cp "$primary/$relative" "$destination/$relative" ;;
                *) echo "merging ${relative#./} failed" >&2; exit 1 ;;
            esac
        fi

        if [ -x "$primary/$relative" ]; then
            chmod +x "$destination/$relative"
        fi
    done

    ( cd "$secondary" && find . -type f -print0 ) | while IFS= read -r -d '' relative; do
        if [ ! -f "$primary/$relative" ]; then
            echo "warning: ${relative#./} is in $(basename "$secondary") only, so it ships as that slice alone." >&2
            mkdir -p "$destination/$(dirname "$relative")"
            cp "$secondary/$relative" "$destination/$relative"
            if [ -x "$secondary/$relative" ]; then
                chmod +x "$destination/$relative"
            fi
        fi
    done
}

echo "version $version, rid $rid"

if [ "$rid" = osx-universal ]; then
    for slice in "${slices[@]}"; do
        echo
        echo "publishing $project for $slice"
        publish_program "$project" "$stage/$slice" "$version" "$slice" 0
    done

    echo
    echo "merging ${slices[*]}"
    merge_slices "$stage/${slices[0]}" "$stage/${slices[1]}" "$stage/$folder"
    verify_version "$(program_binary "$stage/$folder" "$name")" "$version"
else
    echo
    echo "publishing $project"
    publish_program "$project" "$stage/$folder" "$version" "$rid"
fi

case "$rid" in
    linux-*)
        if [ ! -f "$stage/$folder/$name.desktop" ]; then
            echo "warning: $name.desktop is not in the publish. GlobalShortcuts portal will refuse the app id." >&2
        fi
        ;;
esac

make_zip "$out/$folder.zip" "$stage" "$folder"

report_zips "$out/$folder.zip"
