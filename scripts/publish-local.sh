#!/usr/bin/env bash
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

usage() {
    cat <<'TEXT'
publish-local.sh [OPTIONS] [NAME...]

Packs the libraries under src/ at a fixed local version and publishes them into
a folder feed on this machine, so another solution on the same box restores them.

  --version V   version to stamp, default 9999.0.0-localbuild
  --feed DIR    folder feed to publish into, default ~/.nuget/local-packages
  --out DIR     where the packages are packed, default artifacts/packages
  --name NAME   source name to register the feed under, default basin-local
  --no-symbols  skip the .snupkg beside each package
  --no-source   leave the nuget source list alone
  -n            print what would be published and stop

A NAME is a project directory or a package id, the same set build-packages.sh
takes. With none, every packable project under src/ is published.
TEXT
}

global_packages() {
    dotnet nuget locals global-packages --list |
        sed -n 's|^global-packages: *||p' |
        head -1 |
        sed 's|/*$||'
}

pack_args=()
names=()
version=9999.0.0-localbuild
feed="$HOME/.nuget/local-packages"
out=
source_name=basin-local
register=1
dry=0

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --feed) feed=${2:?--feed needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        --name) source_name=${2:?--name needs a value}; shift 2 ;;
        --no-symbols) pack_args+=(--no-symbols); shift ;;
        --no-source) register=0; shift ;;
        -n|--dry-run) dry=1; shift ;;
        -h|--help) usage; exit 0 ;;
        -*)
            echo "unknown argument '$1'" >&2
            exit 1
            ;;
        *)
            names+=("$1")
            shift
            ;;
    esac
done

: "${out:=$root/artifacts/packages}"

mkdir -p "$out" "$feed"
out=$(cd "$out" && pwd)
feed=$(cd "$feed" && pwd)

echo "version $version"
echo "feed    $feed"

if [ "$dry" -eq 1 ]; then
    if [ ${#names[@]} -eq 0 ]; then
        for directory in "$root"/src/*/; do
            project=${directory#"$root"/}
            project=${project%/}
            if is_packable "$project"; then
                echo "  $project"
            fi
        done
    else
        printf '  %s\n' "${names[@]}"
    fi
    exit 0
fi

rm -f "$out"/*."$version".nupkg "$out"/*."$version".snupkg

"$root/scripts/build-packages.sh" --version "$version" --out "$out" \
    ${pack_args[@]+"${pack_args[@]}"} ${names[@]+"${names[@]}"}

packages=()
for package in "$out"/*."$version".nupkg; do
    [ -f "$package" ] || continue
    packages+=("$package")
done

if [ ${#packages[@]} -eq 0 ]; then
    echo "no package at version $version in $out" >&2
    exit 1
fi

cache=$(global_packages)

for package in "${packages[@]}"; do
    id=$(basename "$package")
    id=${id%".$version.nupkg"}
    lower=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')

    rm -f "$feed/$id.$version.nupkg"
    rm -rf "$feed/$lower/$version"

    dotnet nuget push "$package" --source "$feed" --no-symbols >/dev/null

    if [ -n "$cache" ] && [ -d "$cache/$lower/$version" ]; then
        rm -rf "$cache/$lower/$version"
        echo "  cleared $cache/$lower/$version"
    fi

    echo "published $id $version"
done

if [ "$register" -eq 1 ]; then
    if ( cd "$HOME" && dotnet nuget list source ) | grep -qF "$feed"; then
        echo "source $feed already registered"
    else
        ( cd "$HOME" && dotnet nuget add source "$feed" --name "$source_name" >/dev/null )
        echo "registered source '$source_name' at $feed"
    fi
fi

report_files "${packages[@]}"