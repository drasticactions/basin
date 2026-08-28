# RetroWm

RetroWm is a tiling window manager.

## Building

```sh
git submodule update --init --recursive
dotnet build samples/RetroWm
```

## Running

RetroWm needs a compositor that speaks the river window-management protocols,
which means [Inlet](../Inlet) or river itself.

With river:

```sh
river -c retro-wm
```

With Inlet, in two terminals — Inlet prints `SOCKET wayland-N` once it is
listening:

```sh
dotnet run --project samples/Inlet -- --outputs 1
dotnet run --project samples/RetroWm -- --socket wayland-N
```

Or both halves with one command, built in Debug from source:

```sh
scripts/run-inlet-retro-wm.sh --backend nested -c foot -- --trace
```

`--config false` skips the configuration file, `--exit-after N` stops after N
windows were managed, and `--trace` logs every manage sequence.

## Default bindings

| Chord | Action |
|---|---|
| Alt+arrows | Focus the neighbor window in that direction |
| Alt+Esc / Alt+Tab | Cycle focus forward, with Shift backward |
| Alt+Space | System menu for the focused window |
| Alt+Enter | Zoom toggle |
| Alt+I / Alt+Shift+I | Iconize / restore the newest icon |
| Alt+M / Alt+S | Enter the Move / Size keyboard mode |
| Alt+Ctrl+arrows | Move the window one step: reorder in the column, carry into the neighbor column, split off at the edge |
| Alt+Ctrl+Shift+arrows | Move the window's shared edge one fraction step |
| Alt+1 .. Alt+9 | Switch to that workspace |
| Alt+D | Show or hide the dock on this workspace |
| Alt+Shift+1 .. 9 | Send the focused window to that workspace |
| Alt+F4 | Close |
| Alt+Shift+Return | Spawn `terminal_cmd` |
| Alt+Shift+arrows | Send window to the neighbor output |
| Alt+Shift+E | Exit session |

## Configuration

`~/.config/retro-wm/retro-wm.toml`, reloaded on SIGHUP. A missing or broken
file keeps the defaults.

```toml
main_modifier = "alt"
terminal_cmd  = "foot"
decorations   = "ssd"        # every window framed; "prefer-ssd" negotiates,
                             # "csd" frames only rule-named windows

[ui]
font = "DejaVu Sans"         # a system family, or an absolute font path
font_size = 12
border_width = 3
title_active_bg = "#0000AA"

[hotkeys]
"Alt+Return" = "zoom"        # rebind an action
"Alt+D" = "fuzzel"           # or spawn a command

[[rules]]
app_id = "firefox"
force_ssd = true
swallow_top = 36
```

Action names for `[hotkeys]`: `cycle`, `cycle-back`, `menu`, `zoom`,
`iconize`, `restore`, `close`, `spawn-terminal`, `focus-left`,
`focus-right`, `focus-up`, `focus-down`, `move-left`, `move-right`,
`move-up`, `move-down`, `size-left`, `size-right`, `size-up`, `size-down`,
`move-mode`, `size-mode`, `send-left`, `send-right`, `send-up`,
`send-down`, `workspace-1` .. `workspace-9`, `send-workspace-1` ..
`send-workspace-9`, `toggle-dock`, `exit`. Any other value runs as a
command line.

The palette slots under `[ui]`: `title_active_bg`, `title_active_text`,
`title_inactive_bg`, `title_inactive_text`, `window_line`, `chrome_bg`,
`menu_bg`, `menu_text`, `menu_highlight_bg`, `menu_highlight_text`,
`dock_label`, `outline`, `drop_preview`. The dock is icon-only by
default; `dock_labels = true` shows the window title beneath each icon.

`background` paints the desktop behind every window.

```toml
[ui]
background = "#54FC54"
```

```toml
[ui]
dock_bg = "#0000AA"
dock_opacity = 0.5
```
