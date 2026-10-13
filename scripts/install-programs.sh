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
#   --system      install for every user rather than for this one (see below)
#   -n, --dry-run print what an install will do, and change nothing
#
# --system installs for every user, because a login greeter runs as the
# greeter user and cannot run a program from anyone's ~/.local/bin. Each
# program goes into its own directory, /usr/local/lib/basin/NAME/, with the
# binary and its native libraries together, and /usr/local/bin/NAME links to
# it. --out names that library root instead. .desktop files go into
# /usr/local/share/applications and the portal registration files under
# /usr/local/share. Every file is renamed into place, so a running program
# keeps the file it started from. The publish is staged as this user, and one
# root script installs it, through sudo here or through one ssh -t session
# with --ssh, so a password is asked once at most.
#
# With basin-greeter in the set, --system also writes the greeter's system
# files: /etc/tmpfiles.d/basin-greeter.conf (then systemd-tmpfiles --create
# on it), /etc/polkit-1/rules.d/50-basin-greeter.rules,
# /etc/basin-greeter/basin-greeter.toml when none exists, and the example
# /etc/greetd/basin-greeter.example.toml. It never edits
# /etc/greetd/config.toml.
#
# With luna-wm in the set, --system also writes what luna-wm --greeter needs:
# /etc/tmpfiles.d/luna-wm-greeter.conf (then systemd-tmpfiles --create on it),
# the same polkit rule, /etc/luna-wm/greeter.toml when none exists, and the
# example /etc/greetd/luna-wm-greeter.example.toml.
#
# With eight-wm in the set, --system writes the same for eight-wm --greeter:
# /etc/tmpfiles.d/eight-wm-greeter.conf, the polkit rule,
# /etc/eight-wm/greeter.toml when none exists, and the example
# /etc/greetd/eight-wm-greeter.example.toml.
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
system=0
system_lib=/usr/local/lib/basin
system_bin=/usr/local/bin
system_share=/usr/local/share
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
        --system) system=1; shift ;;
        -n|--dry-run) dry=1; shift ;;
        -h|--help)
            sed -n '3,60p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
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
stage=
run_ssh() {
    ssh -o ControlMaster=auto -o ControlPath="$control" -o ControlPersist=60 \
        "$destination" "$@"
}

cleanup() {
    if [ -n "$control" ] && [ -S "$control" ]; then
        ssh -o ControlPath="$control" -O exit "$destination" >/dev/null 2>&1 || true
    fi

    rm -rf "$stage"
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

if [ "$system" -eq 1 ]; then
    [ -z "$out" ] || system_lib=$out
    wants_greeter=0
    wants_luna=0
    wants_eight=0
    for project in "${projects[@]}"; do
        [ "$(program_name "$project")" = basin-greeter ] && wants_greeter=1
        [ "$(program_name "$project")" = luna-wm ] && wants_luna=1
        [ "$(program_name "$project")" = eight-wm ] && wants_eight=1
    done

    if [ "$dry" -eq 1 ]; then
        : "${version:=$(local_version)}"
        echo "version $version, rid ${rid:-$host}"
        echo "install for every user${destination:+ on $destination}, through sudo"
        for project in "${projects[@]}"; do
            name=$(program_name "$project")
            printf '  %s/%s/  (%s: the binary and its native libraries)\n' "$system_lib" "$name" "$project"
            printf '  %s/%s -> %s/%s/%s\n' "$system_bin" "$name" "$system_lib" "$name" "$name"
        done
        echo "  $system_share/applications/  (any .desktop file a publish carries)"
        if wants_portal_files; then
            echo "  $system_share/xdg-desktop-portal/portals/basin.portal"
            echo "  $system_share/xdg-desktop-portal/basin-portals.conf"
            echo "  $system_share/dbus-1/services/org.freedesktop.impl.portal.desktop.basin.service"
        fi
        if [ "$wants_greeter" -eq 1 ]; then
            echo "  /etc/tmpfiles.d/basin-greeter.conf  (then systemd-tmpfiles --create on it)"
            echo "  /etc/polkit-1/rules.d/50-basin-greeter.rules"
            echo "  /etc/basin-greeter/basin-greeter.toml  (only when none exists)"
            echo "  /etc/greetd/basin-greeter.example.toml"
        fi
        if [ "$wants_luna" -eq 1 ]; then
            echo "  /etc/tmpfiles.d/luna-wm-greeter.conf  (then systemd-tmpfiles --create on it)"
            echo "  /etc/polkit-1/rules.d/50-basin-greeter.rules"
            echo "  /etc/luna-wm/greeter.toml  (only when none exists)"
            echo "  /etc/greetd/luna-wm-greeter.example.toml"
        fi
        if [ "$wants_eight" -eq 1 ]; then
            echo "  /etc/tmpfiles.d/eight-wm-greeter.conf  (then systemd-tmpfiles --create on it)"
            echo "  /etc/polkit-1/rules.d/50-basin-greeter.rules"
            echo "  /etc/eight-wm/greeter.toml  (only when none exists)"
            echo "  /etc/greetd/eight-wm-greeter.example.toml"
        fi
        exit 0
    fi

    stage=$(mktemp -d "${TMPDIR:-/tmp}/basin-install.XXXXXX")
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
                rid=$host
            fi
        fi
    fi

    : "${rid:=$host}"
    warn_cross_rid "$rid" "$host"
    : "${version:=$(local_version)}"
    echo "version $version, rid $rid"

    payload="$stage/payload"
    mkdir -p "$payload/programs" "$payload/applications" "$payload/etc"
    : > "$payload/keep"

    for project in "${projects[@]}"; do
        name=$(program_name "$project")
        echo
        echo "publishing $project"
        verify=1
        [ "$rid" = "$host" ] || verify=0
        publish_program "$project" "$stage/$name" "$version" "$rid" "$verify"
        rm -f "$stage/$name/LICENSE" "$stage/$name/README.md"
        mkdir -p "$payload/programs/$name"
        for file in "$stage/$name"/*; do
            [ -f "$file" ] || continue
            case "$(basename "$file")" in
                *.desktop) mv -f "$file" "$payload/applications/" ;;
                *) mv -f "$file" "$payload/programs/$name/" ;;
            esac
        done
        rm -rf "$stage/$name"
    done

    if wants_portal_files; then
        write_portal_files "$payload/portal" "$system_bin/xdg-desktop-portal-basin"
    fi

    if [ "$wants_greeter" -eq 1 ] || [ "$wants_luna" -eq 1 ] || [ "$wants_eight" -eq 1 ]; then
        mkdir -p "$payload/etc/polkit-1/rules.d"
        cat > "$payload/etc/polkit-1/rules.d/50-basin-greeter.rules" <<'RULES'
// basin-greeter, luna-wm --greeter and eight-wm --greeter join Wi-Fi networks from the login screen
// as system-wide connections. These two actions are all they need, and only for
// the greeter user.
polkit.addRule(function(action, subject) {
    if (subject.user == "greeter" &&
        (action.id == "org.freedesktop.NetworkManager.network-control" ||
         action.id == "org.freedesktop.NetworkManager.settings.modify.system")) {
        return polkit.Result.YES;
    }
});
RULES
    fi

    if [ "$wants_greeter" -eq 1 ]; then
        mkdir -p "$payload/etc/tmpfiles.d" "$payload/etc/basin-greeter" "$payload/etc/greetd"
        printf 'd /var/lib/basin-greeter 0700 greeter greeter -\n' > "$payload/etc/tmpfiles.d/basin-greeter.conf"
        if [ -f "$root/samples/BasinGreeter/basin-greeter.toml" ]; then
            cp "$root/samples/BasinGreeter/basin-greeter.toml" "$payload/etc/basin-greeter/basin-greeter.toml"
            printf 'basin-greeter/basin-greeter.toml\n' >> "$payload/keep"
        fi
        cat > "$payload/etc/greetd/basin-greeter.example.toml" <<'GREETD'
# An example /etc/greetd/config.toml for basin-greeter. Copy what you need
# into config.toml. The greeter uses the greeter user's systemd bus for the
# screen reader. On a system without a user bus, use
# command = "dbus-run-session basin-greeter" instead.
[terminal]
vt = 1

[default_session]
command = "basin-greeter"
user = "greeter"
GREETD
    fi

    if [ "$wants_luna" -eq 1 ]; then
        mkdir -p "$payload/etc/tmpfiles.d" "$payload/etc/luna-wm" "$payload/etc/greetd"
        printf 'd /var/lib/luna-wm-greeter 0700 greeter greeter -\n' > "$payload/etc/tmpfiles.d/luna-wm-greeter.conf"
        if [ -f "$root/samples/LunaWm/greeter.toml" ]; then
            cp "$root/samples/LunaWm/greeter.toml" "$payload/etc/luna-wm/greeter.toml"
            printf 'luna-wm/greeter.toml\n' >> "$payload/keep"
        fi
        cat > "$payload/etc/greetd/luna-wm-greeter.example.toml" <<'GREETD'
# An example /etc/greetd/config.toml for luna-wm's login screen. Copy what you
# need into config.toml. The greeter reads /etc/luna-wm/greeter.toml and
# remembers each user in /var/lib/luna-wm-greeter/state.toml.
[terminal]
vt = 1

[default_session]
command = "luna-wm --greeter"
user = "greeter"
GREETD
    fi

    if [ "$wants_eight" -eq 1 ]; then
        mkdir -p "$payload/etc/tmpfiles.d" "$payload/etc/eight-wm" "$payload/etc/greetd"
        printf 'd /var/lib/eight-wm-greeter 0700 greeter greeter -\n' > "$payload/etc/tmpfiles.d/eight-wm-greeter.conf"
        if [ -f "$root/samples/EightWm/greeter.toml" ]; then
            cp "$root/samples/EightWm/greeter.toml" "$payload/etc/eight-wm/greeter.toml"
            printf 'eight-wm/greeter.toml\n' >> "$payload/keep"
        fi
        cat > "$payload/etc/greetd/eight-wm-greeter.example.toml" <<'GREETD'
# An example /etc/greetd/config.toml for eight-wm's login screen. Copy what you
# need into config.toml. The greeter reads /etc/eight-wm/greeter.toml and
# remembers each user in /var/lib/eight-wm-greeter/state.toml.
[terminal]
vt = 1

[default_session]
command = "eight-wm --greeter"
user = "greeter"
GREETD
    fi

    tar -C "$payload" -cf "$stage/payload.tar" .

    system_script=$(cat <<'SCRIPT'
set -eu
archive=$1
lib=$2
bin=$3
share=$4
work=$(mktemp -d /tmp/basin-system.XXXXXX)
trap 'rm -rf "$work"' EXIT INT TERM
tar -x --no-same-owner -C "$work" -f "$archive"
place() {
    mkdir -p "$(dirname "$2")"
    temporary=$(mktemp "$(dirname "$2")/.install.XXXXXX")
    cp "$1" "$temporary"
    chmod "$3" "$temporary"
    mv -f "$temporary" "$2"
    echo "$2"
}
for program in "$work"/programs/*; do
    [ -d "$program" ] || continue
    name=$(basename "$program")
    mkdir -p "$lib/$name"
    chmod 755 "$lib" "$lib/$name"
    for file in "$program"/*; do
        [ -f "$file" ] || continue
        if [ -x "$file" ]; then mode=755; else mode=644; fi
        place "$file" "$lib/$name/$(basename "$file")" "$mode"
    done
    mkdir -p "$bin"
    ln -sfn "$lib/$name/$name" "$bin/.$name.link"
    mv -Tf "$bin/.$name.link" "$bin/$name"
    echo "$bin/$name -> $lib/$name/$name"
done
for file in "$work"/applications/*; do
    [ -f "$file" ] || continue
    place "$file" "$share/applications/$(basename "$file")" 644
done
if [ -f "$work/portal/basin-portals.conf" ]; then
    place "$work/portal/portals/basin.portal" "$share/xdg-desktop-portal/portals/basin.portal" 644
    place "$work/portal/basin-portals.conf" "$share/xdg-desktop-portal/basin-portals.conf" 644
    place "$work/portal/basin.service" "$share/dbus-1/services/org.freedesktop.impl.portal.desktop.basin.service" 644
fi
( cd "$work/etc" && find . -type f | sed 's:^\./::' ) | while read -r relative; do
    if grep -qxF "$relative" "$work/keep" && [ -e "/etc/$relative" ]; then
        echo "/etc/$relative (kept, it exists)"
        continue
    fi
    place "$work/etc/$relative" "/etc/$relative" 644
done
for greeter in basin-greeter luna-wm-greeter eight-wm-greeter; do
    if [ -f "/etc/tmpfiles.d/$greeter.conf" ] && [ -f "$work/etc/tmpfiles.d/$greeter.conf" ]; then
        if getent passwd greeter >/dev/null; then
            systemd-tmpfiles --create "/etc/tmpfiles.d/$greeter.conf"
        else
            echo "warning: no greeter user, so /var/lib/$greeter was not created" >&2
        fi
    fi
done
if [ -d "$share/applications" ] && command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$share/applications" 2>/dev/null || true
fi
SCRIPT
    )

    echo
    if [ -n "$destination" ]; then
        echo "installing on $destination for every user"
        archive=$(run_ssh 'mktemp /tmp/basin-system.XXXXXX.tar')
        run_ssh "cat > $(quote "$archive")" < "$stage/payload.tar"
        status=0
        terminal=-T
        [ -t 0 ] && terminal=-t
        ssh "$terminal" -o ControlMaster=auto -o ControlPath="$control" -o ControlPersist=60 "$destination" \
            "sudo sh -c $(quote "$system_script") sh $(quote "$archive") $(quote "$system_lib") $(quote "$system_bin") $(quote "$system_share")" || status=$?
        run_ssh "rm -f $(quote "$archive")" || true
        exit "$status"
    fi

    echo "installing for every user"
    sudo sh -c "$system_script" sh "$stage/payload.tar" "$system_lib" "$system_bin" "$system_share"
    exit 0
fi

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
