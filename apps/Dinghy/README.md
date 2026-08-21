# Dinghy

Dinghy is a port of [canoe](https://github.com/roblillack/canoe) to .NET.

## Building

```sh
git submodule update --init --recursive
dotnet build apps/Dinghy
```

## Running

Dinghy needs a compositor that speaks the river window-management protocols,
which means [Inlet](../Inlet) or river itself.

With river:

```sh
river -c dinghy
```

With Inlet, in two terminals — Inlet prints `SOCKET wayland-N` once it is
listening:

```sh
dotnet run --project apps/Inlet -c Release -- --backend nested
dotnet run --project apps/Dinghy -c Release -- --socket wayland-N
```

`scripts/run-inlet-dinghy.sh` does both from a Debug build of each, and takes
Inlet's arguments before `--` and Dinghy's after it:

```sh
scripts/run-inlet-dinghy.sh --backend nested -c foot -- --trace
```

Options:

| Option | What it does |
|---|---|
| `--socket NAME` | The Wayland display to connect to, otherwise `WAYLAND_DISPLAY` |
| `--config` | Read the config file, on by default. `--config false` uses the built-in defaults |
| `--trace` | Print window-management events |
| `--exit-after N` | Stop after N windows, for automated runs |
| `--log-level LEVEL` | Discard diagnostics below this: `trace`, `debug`, `info`, `warn`, `error` |

`--help` lists the options.

## Configuration

Dinghy shared the same configuration schema as canoe's and keeps it in `~/.config/dinghy/dinghy.toml`.

## Third-party dependencies

NuGet:

- [WaylandSharpest](https://www.nuget.org/packages/WaylandSharpest) — the
  Wayland binding and the protocol generator
- [SkiaSharp](https://www.nuget.org/packages/SkiaSharp), with
  `SkiaSharp.NativeAssets.Linux` and `SkiaSharp.HarfBuzz` — draws the titlebars,
  the menus and the desktop
- [Svg.Skia](https://www.nuget.org/packages/Svg.Skia) — draws SVG icons
- [Tomlyn](https://www.nuget.org/packages/Tomlyn) — reads the config file

Submodules, for the protocol XML the build generates from:

- `external/wayland-protocols`
- `external/wlr-protocols`
- `external/river`

System libraries:

- `libwayland-client`
- `libxkbcommon`