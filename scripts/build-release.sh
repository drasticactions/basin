#!/usr/bin/env bash
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

projects=()
version=
rid=
out="$root/artifacts"
waylonia=apps/Waylonia

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --rid) rid=${2:?--rid needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        -h|--help)
            sed -n '3,21p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
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

if [ ${#projects[@]} -eq 0 ]; then
    for directory in "$root"/apps/*/ "$root"/samples/*/; do
        project=${directory#"$root"/}
        project=${project%/}
        candidates=("$directory"*.csproj)
        if [ "$project" != "$waylonia" ] && [ -f "${candidates[0]}" ]; then
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

        if [ "$path" = "$waylonia" ]; then
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

mkdir -p "$out"
out=$(cd "$out" && pwd)
stage="$out/stage"
rm -rf "$stage"

# A failed publish leaves a half-staged folder, and the next run must not zip it.
trap 'rm -rf "$stage"' EXIT

echo "version $version, rid $rid"
built=()

for project in "${projects[@]}"; do
    name=$(program_name "$project")
    folder="$name-$version-$rid"
    echo
    echo "publishing $project"
    publish_program "$project" "$stage/$folder" "$version" "$rid"

    make_zip "$out/$folder.zip" "$stage" "$folder"
    built+=("$out/$folder.zip")
done

report_zips "${built[@]}"
