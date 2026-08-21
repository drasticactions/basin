#!/usr/bin/env bash
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

usage() {
    cat <<'TEXT'
build-packages.sh [OPTIONS] [NAME...]

Packs the libraries under src/ into artifacts/packages, Release and without
lifetime tracking, so a package carries what a consumer runs.

  --version V   version to stamp, default 0.1.0-local.g<commit>
  --out DIR     where the packages are written, default artifacts/packages
  --no-symbols  skip the .snupkg beside each package
TEXT
}

package_id() {
    local project=$1 file id=
    file=$(set -- "$root/$project"/*.csproj; [ -f "$1" ] && printf '%s' "$1")
    if [ -n "$file" ]; then
        id=$(sed -n 's:.*<PackageId>\(.*\)</PackageId>.*:\1:p' "$file" | head -1)
    fi

    if [ -z "$id" ]; then
        id=$(basename "$project")
    fi

    printf '%s' "$id"
}

is_packable() {
    local project=$1 file
    file=$(set -- "$root/$project"/*.csproj; [ -f "$1" ] && printf '%s' "$1")
    [ -n "$file" ] || return 1
    ! grep -q '<IsPackable>false</IsPackable>' "$file"
}

unresolved_dependencies() {
    local package=$1 id=$2 version=$3
    command -v python3 >/dev/null 2>&1 || return 0
    python3 - "$package" "$id" "$version" <<'PY'
import re, sys, zipfile

package, identifier, version = sys.argv[1:4]
with zipfile.ZipFile(package) as archive:
    nuspec = archive.read(identifier + ".nuspec").decode("utf-8-sig")

for name, stamped in re.findall(r'<dependency +id="([^"]+)" +version="([^"]+)"', nuspec):
    if stamped == version:
        print(name)
PY
}

resolve_library() {
    local wanted=$1 lower directory project base
    if [ -d "$root/${wanted%/}" ]; then
        printf '%s' "${wanted%/}"
        return 0
    fi

    lower=$(printf '%s' "$wanted" | tr '[:upper:]' '[:lower:]')
    for directory in "$root"/src/*/; do
        project=$(printf '%s' "${directory#"$root"/}" | sed 's:/$::')
        base=$(basename "$directory" | tr '[:upper:]' '[:lower:]')
        if [ "$base" = "$lower" ] ||
           [ "$(package_id "$project" | tr '[:upper:]' '[:lower:]')" = "$lower" ]; then
            printf '%s' "$project"
            return 0
        fi
    done

    return 1
}

projects=()
version=
out="$root/artifacts/packages"
symbols=1

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        --no-symbols) symbols=0; shift ;;
        -h|--help) usage; exit 0 ;;
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
    for directory in "$root"/src/*/; do
        project=${directory#"$root"/}
        project=${project%/}
        if is_packable "$project"; then
            projects+=("$project")
        fi
    done
else
    resolved=()
    for wanted in "${projects[@]}"; do
        if ! path=$(resolve_library "$wanted"); then
            echo "unknown library '$wanted':" >&2
            ( cd "$root" && ls -d src/*/ | sed 's:/$::; s:^:  :' ) >&2
            exit 1
        fi

        if ! is_packable "$path"; then
            echo "$path sets IsPackable=false and publishes nothing." >&2
            exit 1
        fi

        resolved+=("$path")
    done

    projects=("${resolved[@]}")
fi

if [ ${#projects[@]} -eq 0 ]; then
    echo "no packable project under src/" >&2
    exit 1
fi

: "${version:=$(local_version)}"

ids=()
for directory in "$root"/src/*/; do
    project=${directory#"$root"/}
    project=${project%/}
    if is_packable "$project"; then
        ids+=("$(package_id "$project")")
    fi
done

mkdir -p "$out"
out=$(cd "$out" && pwd)

echo "version $version"
built=()

for project in "${projects[@]}"; do
    id=$(package_id "$project")
    echo
    echo "packing $project"

    rm -f "$out/$id.$version.nupkg" "$out/$id.$version.snupkg"

    dotnet pack "$root/$project" -c Release \
        -p:Version="$version" -p:BasinCounters=false \
        -p:IncludeSymbols=$([ "$symbols" -eq 1 ] && echo true || echo false) \
        -o "$out" --nologo -v quiet

    if [ ! -f "$out/$id.$version.nupkg" ]; then
        echo "$project produced no $id.$version.nupkg" >&2
        exit 1
    fi

    for dependency in $(unresolved_dependencies "$out/$id.$version.nupkg" "$id" "$version"); do
        case " ${ids[*]} " in
            *" $dependency "*) ;;
            *)
                echo "warning: $id depends on $dependency $version, which no project under src/ packs." >&2
                echo "         The package restores only where that dependency is published." >&2
                ;;
        esac
    done

    built+=("$out/$id.$version.nupkg")
    if [ -f "$out/$id.$version.snupkg" ]; then
        built+=("$out/$id.$version.snupkg")
    fi
done

report_files "${built[@]}"
