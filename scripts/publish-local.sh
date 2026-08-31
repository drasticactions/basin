#!/usr/bin/env bash
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

usage() {
    cat <<'TEXT'
publish-local.sh [OPTIONS] [NAME...]

Packs the libraries under src/ at a fixed local version and installs them into
the nuget cache on this machine, so another solution on the same box restores
them without a source of its own.

  --version V   version to stamp, default 9999.0.0-localbuild
  --cache DIR   global packages folder, default what dotnet reports
  --out DIR     where the packages are packed, default artifacts/packages
  --no-symbols  skip the .snupkg beside each package
  -n            print what would be published and stop

A NAME is a project directory or a package id, the same set build-packages.sh
takes. With none, every packable project under src/ is published.
TEXT
}

global_packages() {
    dotnet nuget locals global-packages --list |
        sed -n 's|.*global-packages: *||p' |
        head -1 |
        tr '\\' '/' |
        sed 's|/*$||'
}

lower() {
    printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

pack_args=()
names=()
version=9999.0.0-localbuild
cache=
out=
dry=0

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --cache) cache=${2:?--cache needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        --no-symbols) pack_args+=(--no-symbols); shift ;;
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
: "${cache:=$(global_packages)}"

if [ -z "$cache" ]; then
    echo "dotnet nuget locals global-packages --list named no folder" >&2
    exit 1
fi

mkdir -p "$out" "$cache"
out=$(cd "$out" && pwd)
cache=$(cd "$cache" && pwd)

echo "version $version"
echo "cache   $cache"

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
ids=()
for package in "$out"/*."$version".nupkg; do
    [ -f "$package" ] || continue
    id=$(basename "$package")
    id=${id%".$version.nupkg"}
    packages+=("$package")
    ids+=("$id")
done

if [ ${#packages[@]} -eq 0 ]; then
    echo "no package at version $version in $out" >&2
    exit 1
fi

folder_version=$(lower "$version")

for id in "${ids[@]}"; do
    installed="$cache/$(lower "$id")/$folder_version"
    if [ -d "$installed" ]; then
        rm -rf "$installed"
        echo "  cleared $installed"
    fi
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="basin-local" value="$out" />
  </packageSources>
  <packageSourceMapping>
    <clear />
  </packageSourceMapping>
</configuration>
EOF

{
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    echo '    <TargetFramework>net10.0</TargetFramework>'
    echo '    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>'
    echo '    <NuGetAudit>false</NuGetAudit>'
    echo '  </PropertyGroup>'
    echo '  <ItemGroup>'
    for id in "${ids[@]}"; do
        echo "    <PackageDownload Include=\"$id\" Version=\"[$version]\" />"
    done
    echo '  </ItemGroup>'
    echo '</Project>'
} > "$work/install.csproj"

dotnet restore "$work/install.csproj" --packages "$cache" --nologo -v quiet

for id in "${ids[@]}"; do
    installed="$cache/$(lower "$id")/$folder_version"
    if [ ! -f "$installed/.nupkg.metadata" ]; then
        echo "$id $version did not install into $installed" >&2
        exit 1
    fi

    echo "published $id $version"
done

report_files "${packages[@]}"
