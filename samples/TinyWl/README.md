# TinyWl

TinyWl is a port of [wlroots' tinywl](https://gitlab.freedesktop.org/wlroots/wlroots/-/tree/master/tinywl) to .NET and designed to be an MVP to demostrate building a compositor with base level classes.

## Building and running

```sh
dotnet run --project samples/TinyWl -c Release -- [-s startup command] [--renderer NAME] [--drm]
```
By default it runs nested against your existing Wayland Compositor. You can run `--drm` to run it directly.

## Keybindings

- `Alt+Escape`, terminate the compositor
- `Alt+F1`, cycle between windows