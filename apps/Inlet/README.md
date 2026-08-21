# Inlet

Inlet is a [River-compatible](https://codeberg.org/river/river) Wayland compositor. 
It implements the river window-management protocols, allowing it to run compatible
window managers.

## Building

```sh
git submodule update --init --recursive
dotnet build apps/Inlet
```

## Running

You need to give it a window manager to run on it.
Any of the ones listed in the [river wiki](https://codeberg.org/river/wiki/src/branch/main/pages/wm-list.md) _should_ work.
You can also try ones in this repo such as [InletWm](../../samples/InletWm) or [Dinghy](../../samples/Dinghy):

`-c` is river's: the command runs through `sh -c` once the socket is up, and
everything in the session comes out of it. A manager documented as
`river -c dinghy` runs the same way here.

```sh
dotnet run --project apps/Inlet -c Release -- -c path/to/inletwm
dotnet run --project apps/Inlet -c Release -- -c 'inletwm --trace & weston-simple-shm'
```

## Configuration

Configuration matches river's. Inlet looks for `$XDG_CONFIG_HOME/inlet/init` and then
`$XDG_CONFIG_HOME/river/init`. Without `XDG_CONFIG_HOME` it reads the same two
files under `$HOME/.config`. Inlet's is used first, followed by river's.

```sh
cat > ~/.config/inlet/init <<'EOF'
#!/bin/sh
exec rill
EOF
chmod +x ~/.config/inlet/init
dotnet run --project apps/Inlet -c Release
```

Options:

| Option | What it does |
|---|---|
| `-c`, `--command CMD` | Run this through `sh -c` once the socket is up, instead of the init file. Its exit does not end the session |
| `--backend KIND` | Where the outputs go: `nested`, `drm` or `headless`. |
| `--xwayland` | Start Xwayland for X11 clients |
| `--width N` | Output width in pixels, default 1280 |
| `--height N` | Output height in pixels, default 720 |
| `--scale S` | Output scale, default taken from the output |
| `--frames N` | Render N frames and exit, 0 to run until stopped |
| `--screenshot PNG` | Write a PNG of the last frame |
| `--renderer NAME` | Renderer to draw with, default `vulkan` or `BASIN_RENDERER`, falling back through `gl` to `pixman` |
| `--log-level LEVEL` | Discard diagnostics below this: `trace`, `debug`, `info`, `warn`, `error` |

`--help` lists the options and the renderer names this build has, and
`--version` reports what the build was stamped with.

## Third-party dependencies

NuGet:

- [WaylandSharpest](https://www.nuget.org/packages/WaylandSharpest) — the
  Wayland binding and the protocol generator

Submodules, for the protocol XML the build generates from:

- `external/wayland-protocols`
- `external/wlr-protocols`
- `external/river`

System libraries:

- `libwayland-server`
- `libpixman-1` — for the software renderer, which the default falls back to
- `libxkbcommon`
- `libdrm`, `libgbm`, `libinput`, `libudev` and `libseat` — for `--drm`
- `liblcms2` 2.19 or later — optional, for ICC profiles and color transforms.
  Without it, color management keeps the parametric descriptions and drops the
  rest
- Mesa or a Vulkan driver — for the GPU renderers
- Xwayland — for `--xwayland`
