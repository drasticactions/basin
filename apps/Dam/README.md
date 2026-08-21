# Dam

```sh
dam -s -m last -- chromium --kiosk https://example.org
```

## Build and run

```sh
dotnet run --project apps/Dam -c Release -- [OPTIONS] [--] [APPLICATION...]
```

| Flag | Long form | Meaning |
|---|---|---|
| `-d` | `--no-decorations` | Tell clients the server decorates. Dam draws nothing, so windows come up borderless |
| `-D` | — | Equivalent to `--log-level debug` |
| `-m MODE` | `--output-mode` | `extend` (default) stretches the layout across every output. `last` uses only the newest one |
| `-s` | `--allow-vt-switch` | Enable the `XF86Switch_VT_1..12` keybindings |
| `-v` | `--version` | The version CI stamped |
| `-x` | `--no-xwayland` | Do not start Xwayland |