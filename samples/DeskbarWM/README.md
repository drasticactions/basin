# DeskbarWM

## Building

```sh
git submodule update --init --recursive
dotnet build samples/DeskbarWM
```

## Running

DeskbarWM implements river window-management
protocols, use [Inlet](../Inlet) or river.

```sh
river -c deskbar-wm
```

```sh
dotnet run --project samples/Inlet -- --outputs 1
dotnet run --project samples/DeskbarWM -- --socket wayland-N
```

```sh
scripts/run-inlet-deskbar-wm.sh --backend nested -c foot -- --trace
```