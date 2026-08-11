# NO-MissionAlerts

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for **Nuclear Option**
that shows story/mission text as a centre-screen alert — the way in-game
system alerts appear — instead of burying it in the small message box next to
the killfeed.

## What it does

- Intercepts mission/story messages (`MissionMessages.ShowMessgeLocal`, the
  channel used by mission-scripted "show message" outcomes) and displays them
  centre-screen: bold amber text, duration scaled to text length, fade-out.
- Plays the game's own mission-alert sound every time the displayed text
  changes.
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
| Timing | `BaseSeconds` | `8` | Minimum display time |
| Timing | `PerCharSeconds` | `0.08` | Extra time per character |
| Timing | `CoalesceSeconds` | `1.5` | Messages arriving within this window merge into one alert |

## Building

```
dotnet build src/NOMissionAlerts/NOMissionAlerts.csproj -c Release
```

Pass `-p:GameDir="<path>"` if your install is not at the default Windows Steam
location. Copy the resulting DLL into `BepInEx/plugins/`.

## Status

v0.3.0 — burst coalescing: story paragraphs scripted as several back-to-back
message outcomes (verified against the shipping assembly: each
`ShowMessageOutcome` carries exactly one string, and nothing in the chain
splits text) now merge into a single alert instead of stretching into
minutes of sequential lines. Untested in game.
v0.2.0 — sound on every text change; pacing halved (durations doubled).
v0.1.0 tested and working in game.
