#!/usr/bin/env bash

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)

program_name() {
    local project=$1 file assembly=
    file=$(set -- "$root/$project"/*.csproj; [ -f "$1" ] && printf '%s' "$1")
    if [ -n "$file" ]; then
        assembly=$(sed -n 's:.*<AssemblyName>\(.*\)</AssemblyName>.*:\1:p' "$file" | head -1)
    fi

    if [ -z "$assembly" ]; then
        assembly=$(basename "$project" | tr '[:upper:]' '[:lower:]')
    fi

    printf '%s' "$assembly"
}

program_binary() {
    local directory=$1 name=$2
    if [ -f "$directory/$name.exe" ]; then
        printf '%s' "$directory/$name.exe"
    else
        printf '%s' "$directory/$name"
    fi
}

resolve_program() {
    local wanted=$1 lower base directory project
    if [ -d "$root/${wanted%/}" ]; then
        printf '%s' "${wanted%/}"
        return 0
    fi

    lower=$(printf '%s' "$wanted" | tr '[:upper:]' '[:lower:]')
    for directory in "$root"/apps/*/ "$root"/samples/*/; do
        project=$(printf '%s' "${directory#"$root"/}" | sed 's:/$::')
        base=$(basename "$directory" | tr '[:upper:]' '[:lower:]')
        if [ "$base" = "$lower" ] || [ "$(program_name "$project")" = "$lower" ]; then
            printf '%s' "$project"
            return 0
        fi
    done

    return 1
}

host_rid() {
    dotnet --info | sed -n 's/^ *RID: *//p' | head -1
}

warn_cross_rid() {
    local rid=$1 host=$2
    if [ "$rid" != "$host" ]; then
        echo "warning: publishing $rid from $host needs a cross toolchain this does not install." >&2
        echo "         Build on the target machine when the native link fails." >&2
    fi
}

local_version() {
    local commit dirty=
    commit=$(git -C "$root" rev-parse --short HEAD 2>/dev/null || echo unknown)
    if ! git -C "$root" diff --quiet HEAD 2>/dev/null; then
        dirty=.dirty
    fi

    printf '0.1.0-local.g%s%s' "$commit" "$dirty"
}

verify_version() {
    local binary=$1 version=$2 stamped
    stamped=$("$binary" --version)
    if [ "${stamped%%+*}" != "$version" ]; then
        echo "warning: $(basename "$binary") reports version '$stamped', not '$version'" >&2
    fi
}

publish_program() {
    local project=$1 destination=$2 version=$3 rid=$4 verify=${5:-1} name
    name=$(program_name "$project")

    dotnet publish "$root/$project" -c Release -r "$rid" \
        -p:Version="$version" -o "$destination" --nologo -v quiet

    rm -f "$destination"/*.pdb "$destination"/*.xml "$destination"/*.dbg
    rm -rf "$destination"/*.dSYM

    cp "$root/LICENSE" "$destination/LICENSE"
    if [ -f "$root/$project/README.md" ]; then
        cp "$root/$project/README.md" "$destination/README.md"
    fi

    if [ "$verify" -eq 1 ]; then
        verify_version "$(program_binary "$destination" "$name")" "$version"
    fi
}

make_zip() {
    local zipfile=$1 parent=$2 dir=$3
    rm -f "$zipfile"
    if command -v zip >/dev/null 2>&1; then
        ( cd "$parent" && zip -qr "$zipfile" "$dir" )
    elif command -v bsdtar >/dev/null 2>&1; then
        bsdtar -a -cf "$zipfile" -C "$parent" "$dir"
    else
        # Python's writer records the file mode, so the binary stays executable when unzipped.
        python3 - "$zipfile" "$parent" "$dir" <<'PY'
import os, sys, zipfile
zipfile_path, parent, top = sys.argv[1:4]
with zipfile.ZipFile(zipfile_path, "w", zipfile.ZIP_DEFLATED) as archive:
    for directory, _, files in sorted(os.walk(os.path.join(parent, top))):
        for name in sorted(files):
            path = os.path.join(directory, name)
            archive.write(path, os.path.relpath(path, parent))
PY
    fi
}

report_zips() {
    local zipfile
    echo
    for zipfile in "$@"; do
        printf '%s  %s\n' "$(du -h "$zipfile" | cut -f1)" "$zipfile"
    done
    echo
}
