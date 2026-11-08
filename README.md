# SpaceVors

A minimalist Vampire-Survivors-style game in space. You fly a ship by Newtonian thrust,
your guns fire themselves. Destroyed enemies leave behind loot. Pick it up for levels, healing 
and other goodies. Every level choose from upgrade bundles. Between the
enemy waves there are lulls to loot. Survive as long as you can.

## Install

Grab the build for your system from the
[latest release](https://github.com/mikharju/spacevors/releases/latest).

**Linux (x86_64)** — download `SpaceVors-x86_64.AppImage`, then:

```bash
chmod +x SpaceVors-x86_64.AppImage
./SpaceVors-x86_64.AppImage
```

No FUSE on your distro (common on recent openSUSE/Arch)? Run it unpacked:
`./SpaceVors-x86_64.AppImage --appimage-extract-and-run`

**Windows (x64)** — download `SpaceVors-windows-x64.zip`, unzip it to a folder and run
`Game.exe`. No installer, nothing to install, and no .NET needed — the runtime is inside.

## Controls

Ship selection:

- W/S or Up/Down, or the mouse wheel: browse ships · mouse over a card highlights it
- Enter, click, or 1–8: choose that ship

In flight:

- W / S: forward / backward thrust · A / D: strafe left / right · Shift: boost
- Mouse: aim — the ship rotates toward the cursor
- Left click: lock the clicked ship, mine or rock as primary target (red brackets); click
  empty space to clear it
- Tab: ship stats screen (pauses the game); "Manage mounts and cargo" there rearranges guns
- 1–5 or click: take an upgrade **bundle** — two cards at once, the strong one on top
- R: spend a salvage charge to reroll the whole upgrade hand
- R after game over: back to ship selection

## Build

Needs the .NET 10 SDK.

```bash
dotnet run --project src/Game/Game.csproj
```

A release build of the game executable:

```bash
dotnet publish src/Game/Game.csproj -c Release --runtime linux-x64 --self-contained true
```

Use `--runtime win-x64` for a Windows build.

## Test

```bash
dotnet test src/Tests/Tests.csproj -c Release
```

The tests never open a window.
