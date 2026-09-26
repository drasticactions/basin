#!/usr/bin/env bash
#
# install-programs.sh [OPTIONS] [NAME...]
#
# Publishes the programs in apps/ and samples/ with NativeAOT, then copies
# each binary and the native libraries beside it into one directory. The
# default is ~/.local/bin, and an existing file of the same name is
# overwritten. The default set is every program except BlurClient, which is a
# test client for one effect. So it holds the compositors, the window
# managers, and the tools that drive them: basinctl, basin-mcp and the portal
# backend.
#
# Naming programs installs those instead, by name or by path. A name is the
# binary's, which is the project's assembly name rather than its directory:
# retro-wm, not RetroWm, and basin-mcp, not BasinMcp. A name also installs
# BlurClient.
#
# The version defaults to 0.1.0-local.g<commit>, with .dirty when tracked
# files differ from the commit, so an installed binary never claims a version
# CI can also mint.
#
# A .desktop file in a publish goes into the desktop database under
# $XDG_DATA_HOME instead, because a portal reads it there. When the set
# includes tinycomp, westonia or maui-comp, the script also writes the two
# xdg-desktop-portal registration files that route the portal interfaces to
# basin, under $XDG_DATA_HOME/xdg-desktop-portal. --no-portal-files skips them.
#
# --ssh DEST installs on another machine instead of this one, and nothing is
# left here. DEST is anything ssh takes, an alias in ~/.ssh/config included,
# so a port or an identity is configured there. --out then names a directory
# on that machine, the runtime identifier is the one the remote reports, and
# the run opens one shared connection so a password is asked once.
#
#   --version V   version to stamp, default 0.1.0-local.g<commit>
#   --rid RID     runtime identifier, default the host's, or the remote's
#   --out DIR     where the programs are installed, default ~/.local/bin
#   --ssh DEST    install on DEST over ssh rather than on this machine
#   --no-portal-files  leave the xdg-desktop-portal registration files alone
#   -n, --dry-run print what an install will do, and change nothing
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

projects=()
version=
rid=
out=
destination=
dry=0
portal_files=1
excluded=(samples/BlurClient)
portal_programs=(tinycomp westonia maui-comp xdg-desktop-portal-basin)
portal_interfaces="org.freedesktop.impl.portal.ScreenCast;org.freedesktop.impl.portal.RemoteDesktop;org.freedesktop.impl.portal.Screenshot;org.freedesktop.impl.portal.Clipboard;org.freedesktop.impl.portal.InputCapture;org.freedesktop.impl.portal.GlobalShortcuts;org.freedesktop.impl.portal.Access;"

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --rid) rid=${2:?--rid needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        --ssh) destination=${2:?--ssh needs a value}; shift 2 ;;
        --no-portal-files) portal_files=0; shift ;;
        -n|--dry-run) dry=1; shift ;;
        -h|--help)
            sed -n '3,40p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
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
    local mode=$1 source=$2 target=$3 temporary
    temporary=$(mktemp "$(dirname -- "$target")/.install.XXXXXX")
    cp "$source" "$temporary"
    chmod "$mode" "$temporary"
    mv -f "$temporary" "$target"
}

is_excluded() {
    local project=$1 name
    for name in "${excluded[@]}"; do
        [ "$project" = "$name" ] && return 0
    done
    return 1
}

quote() {
    printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"
}

control=
run_ssh() {
    ssh -o ControlMaster=auto -o ControlPath="$control" -o ControlPersist=60 \
        "$destination" "$@"
}

run_login() {
    run_ssh "sh -lc $(quote "$1")"
}

wants_portal_files() {
    local project name candidate
    [ "$portal_files" -eq 1 ] || return 1
    for project in "${projects[@]}"; do
        name=$(program_name "$project")
        for candidate in "${portal_programs[@]}"; do
            [ "$name" = "$candidate" ] && return 0
        done
    done
    return 1
}

write_portal_files() {
    local directory=$1 executable=${2:-}
    mkdir -p "$directory/portals"
    printf '[portal]\nDBusName=org.freedesktop.impl.portal.desktop.basin\nInterfaces=%s\nUseIn=basin\n' \
        "$portal_interfaces" > "$directory/portals/basin.portal"
    {
        printf '[preferred]\ndefault=gtk;kde;gnome;\n'
        printf '%s\n' "$portal_interfaces" | tr ';' '\n' | sed '/^$/d; s/$/=basin;/'
    } > "$directory/basin-portals.conf"
    if [ -n "$executable" ]; then
        printf '[D-BUS Service]\nName=org.freedesktop.impl.portal.desktop.basin\nExec=%s\n' \
            "$executable" > "$directory/basin.service"
    fi
}

reload_bus() {
    command -v busctl >/dev/null 2>&1 || return 0
    busctl --user call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ReloadConfig >/dev/null 2>&1 || true
}

remote_expand() {
    local path=$1
    case "$path" in
        "~") path='$HOME' ;;
        "~/"*) path='$HOME/'${path#\~/} ;;
    esac

    run_login "printf '%s\n' \"$path\"" | tail -1
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
            ( cd "$root" && ls -d apps/*/ samples/*/ 2>/dev/null | sed 's:/$::; s:^:  :' ) >&2
            exit 1
        fi

        resolved+=("$path")
    done

    projects=("${resolved[@]}")
fi

host=$(host_rid)

if [ "$dry" -eq 1 ]; then
    : "${version:=$(local_version)}"
    if [ -n "$destination" ]; then
        echo "version $version, rid ${rid:-$host (the remote is not asked in a dry run)}"
        echo "install into $destination:${out:-~/.local/bin}"
    else
        echo "version $version, rid ${rid:-$host}"
        echo "install into ${out:-${XDG_BIN_HOME:-$HOME/.local/bin}}"
    fi

    for project in "${projects[@]}"; do
        printf '  %s  (%s)\n' "$(program_name "$project")" "$project"
    done
    exit 0
fi

stage=$(mktemp -d "${TMPDIR:-/tmp}/basin-install.XXXXXX")

cleanup() {
    if [ -n "$control" ] && [ -S "$control" ]; then
        ssh -o ControlPath="$control" -O exit "$destination" >/dev/null 2>&1 || true
    fi

    rm -rf "$stage"
}

trap cleanup EXIT

if [ -n "$destination" ]; then
    control="$stage/ssh"
    if ! run_ssh true; then
        echo "cannot reach $destination over ssh." >&2
        exit 1
    fi

    if [ -z "$rid" ]; then
        report=$(run_login 'printf "%s %s %s\n" "$(uname -s)" "$(uname -m)" "$(ls /lib/ld-musl-* >/dev/null 2>&1 && echo musl || echo gnu)"' | tail -1)
        rid=$(portable_rid $report || true)
        if [ -z "$rid" ]; then
            echo "warning: $destination reports '$report', which names no runtime identifier, so $host is published instead." >&2
            echo "         Name one with --rid when the two machines differ." >&2
            rid=$host
        fi
    fi

    if [ -z "$out" ]; then
        out=$(remote_expand '${XDG_BIN_HOME:-$HOME/.local/bin}')
    else
        out=$(remote_expand "$out")
    fi

    applications=$(remote_expand '${XDG_DATA_HOME:-$HOME/.local/share}/applications')
    portal_home=$(remote_expand '${XDG_DATA_HOME:-$HOME/.local/share}/xdg-desktop-portal')
    services_home=$(remote_expand '${XDG_DATA_HOME:-$HOME/.local/share}/dbus-1/services')
else
    : "${out:=${XDG_BIN_HOME:-$HOME/.local/bin}}"
    : "${rid:=$host}"
    applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
    portal_home="${XDG_DATA_HOME:-$HOME/.local/share}/xdg-desktop-portal"
    services_home="${XDG_DATA_HOME:-$HOME/.local/share}/dbus-1/services"

    mkdir -p "$out"
    out=$(cd "$out" && pwd)
fi

warn_cross_rid "$rid" "$host"

: "${version:=$(local_version)}"

echo "version $version, rid $rid"
installed=()

payload="$stage/payload"
mkdir -p "$payload/bin" "$payload/applications" "$payload/portal"

for project in "${projects[@]}"; do
    name=$(program_name "$project")
    echo
    echo "publishing $project"
    verify=1
    [ "$rid" = "$host" ] || verify=0
    publish_program "$project" "$stage/$name" "$version" "$rid" "$verify"

    rm -f "$stage/$name/LICENSE" "$stage/$name/README.md"

    for file in "$stage/$name"/*; do
        [ -f "$file" ] || continue
        base=$(basename "$file")
        case "$base" in
            *.desktop) kind=applications; target=$applications ;;
            *) kind=bin; target=$out ;;
        esac

        if [ -n "$destination" ]; then
            mv -f "$file" "$payload/$kind/$base"
            if [ "$kind" = bin ] && [ -x "$payload/$kind/$base" ]; then
                chmod 755 "$payload/$kind/$base"
            else
                chmod 644 "$payload/$kind/$base"
            fi
        elif [ "$kind" = applications ]; then
            mkdir -p "$applications"
            install_file 644 "$file" "$applications/$base"
        elif [ -x "$file" ]; then
            install_file 755 "$file" "$out/$base"
        else
            install_file 644 "$file" "$out/$base"
        fi

        installed+=("$target/$base")
    done

    rm -rf "$stage/$name"
done

if wants_portal_files; then
    if [ -n "$destination" ]; then
        write_portal_files "$payload/portal" "$out/xdg-desktop-portal-basin"
    else
        write_portal_files "$stage/portal" "$out/xdg-desktop-portal-basin"
        mkdir -p "$portal_home/portals" "$services_home"
        install_file 644 "$stage/portal/portals/basin.portal" "$portal_home/portals/basin.portal"
        install_file 644 "$stage/portal/basin-portals.conf" "$portal_home/basin-portals.conf"
        install_file 644 "$stage/portal/basin.service" "$services_home/org.freedesktop.impl.portal.desktop.basin.service"
        reload_bus
    fi

    installed+=("$portal_home/portals/basin.portal" "$portal_home/basin-portals.conf" "$services_home/org.freedesktop.impl.portal.desktop.basin.service")
fi

if [ -n "$destination" ]; then
    echo
    echo "installing on $destination"

    remote=$(cat <<'SCRIPT'
set -eu
out=$1
applications=$2
portal_home=$3
services_home=$4
mkdir -p "$out"
work=$(mktemp -d "$out/.basin-install.XXXXXX")
trap 'rm -rf "$work"' EXIT INT TERM
tar -x -C "$work" -f -
for file in "$work"/bin/*; do
    [ -e "$file" ] || continue
    mv -f "$file" "$out/$(basename "$file")"
done
for file in "$work"/applications/*; do
    [ -e "$file" ] || continue
    mkdir -p "$applications"
    mv -f "$file" "$applications/$(basename "$file")"
done
if [ -f "$work/portal/basin-portals.conf" ]; then
    mkdir -p "$portal_home/portals" "$services_home"
    mv -f "$work/portal/portals/basin.portal" "$portal_home/portals/basin.portal"
    mv -f "$work/portal/basin-portals.conf" "$portal_home/basin-portals.conf"
    [ -f "$work/portal/basin.service" ] && mv -f "$work/portal/basin.service" "$services_home/org.freedesktop.impl.portal.desktop.basin.service"
    command -v busctl >/dev/null 2>&1 && busctl --user call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ReloadConfig >/dev/null 2>&1 || true
fi
if [ -d "$applications" ] && command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$applications" 2>/dev/null || true
fi
SCRIPT
    )

    tar -C "$payload" -cf - bin applications portal \
        | run_ssh "sh -c $(quote "$remote") sh $(quote "$out") $(quote "$applications") $(quote "$portal_home") $(quote "$services_home")"

    path=$(run_login 'printf "%s\n" "$PATH"' 2>/dev/null | tail -1 || true)
else
    if command -v update-desktop-database >/dev/null 2>&1 && [ -d "$applications" ]; then
        update-desktop-database "$applications" 2>/dev/null || true
    fi

    path=$PATH
fi

echo
printf '%s\n' "${installed[@]}" | sort -u

case ":$path:" in
    *":$out:"*) ;;
    *)
        echo
        if [ -n "$destination" ]; then
            echo "warning: could not verify $out on $destination's PATH" >&2
        else
            echo "warning: could not verify $out on PATH" >&2
        fi
        ;;
esac
echo
