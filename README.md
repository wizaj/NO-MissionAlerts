# NO-MissionAlerts

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for **Nuclear Option**
that shows story/mission text as a centre-screen alert — the way in-game
system alerts appear — instead of burying it in the small message box next to
the killfeed.

## What it does

- Intercepts mission/story messages (`MissionMessages.ShowMessgeLocal`, the
  channel used by mission-scripted "show message" outcomes) and displays them
  centre-screen: bold amber text, duration scaled to text length, fade-out.
- Keeps the game's own mission-alert sound.
- **The killfeed is untouched** — kills travel a completely separate code
  path (`MessageManager` → `MessageUI.KillFeed`) that this mod never patches.

## Configuration

Written to `BepInEx/config/local.nomissionalerts.cfg` on first run.

| Section | Key | Default | Notes |
|---|---|---|---|
| General | `Enabled` | `true` | |
| General | `HideFromFeed` | `true` | `true` = move text to centre only; `false` = show in both places |
| Style | `FontSize` | `26` | |
| Style | `VerticalAnchor` | `0.32` | Fraction of screen height (0.5 = dead centre) |
| Timing | `BaseSeconds` | `4` | Minimum display time |
| Timing | `PerCharSeconds` | `0.04` | Extra time per character |

## Building

```
dotnet build src/NOMissionAlerts/NOMissionAlerts.csproj -c Release
```

Pass `-p:GameDir="<path>"` if your install is not at the default Windows Steam
location. Copy the resulting DLL into `BepInEx/plugins/`.

## Status

v0.1.0 — untested in game.
