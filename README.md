# FFXIV-RiichiAssistant

![FFXIV RiichiAssistant Logo](docs/assets/logo.png)

Dalamud plugin repository and plugin source scaffold for a Final Fantasy XIV Riichi Mahjong assistant plugin.

## Goal

Provide in-game decision support while playing Mahjong, including:

- Mahjong session detection when seated at the Mahjong table
- Hand / score / discard-count readout
- Dora + aka-dora tracking
- Top 3 discard recommendations with reasoning
- Call recommendations for:
  - Pon
  - Chi
  - Kan
  - Riichi
  - Tsumo
  - Ron

## Dalamud Custom Plugin Repository URL

After this repo is public and committed:

`https://raw.githubusercontent.com/iTaterLove/FFXIV-RiichiAssistant/main/repo.json`

Add that URL in Dalamud:
`Settings -> Experimental -> Custom Plugin Repositories`

## Repository Layout

- `repo.json` — Dalamud custom repo index
- `src/FFXIV.RiichiAssistant.Plugin/` — Dalamud plugin scaffold and Mahjong state integration
- `artifacts/` — release zip artifacts (or GitHub Releases assets)
- `build/Package-Plugin.ps1` — repo-local packaging script that emits a Dalamud-ready zip into `artifacts/`
- `docs/` — implementation notes and roadmap

## Status

Core, riichi-analysis, decision, and plugin scaffold projects are in place. The plugin project now includes a minimal Dalamud entrypoint that polls Mahjong UI state and feeds the analysis/recommendation pipeline.

## Manual Testing

The plugin exposes a minimal in-game debug window for the current normalized Mahjong state.

- Slash command: `/riichiassistant`
- Dalamud main/config open hooks also open the same debug window.
- The panel now includes per-player score/riichi/discard rows and a live manual call recommendation preview.

## Packaging

Build and package the plugin zip from the repo root with:

```powershell
.\build\Package-Plugin.ps1
```

The script publishes the plugin project, writes a minimal plugin manifest alongside the binaries, and creates `artifacts/FFXIVRiichiAssistant.zip`.
If `C:\Users\<you>\.dotnet\dotnet.exe` exists, the script prefers that local SDK automatically.

## Mahjong Layout Profiles

The plugin now loads Mahjong addon value-map profiles from a runtime JSON file so mappings can be changed without rebuilding.

- File path: `Dalamud plugin config directory/mahjong-layout-profiles.json`
- Default profile: `safe-fallback`
- Included profile templates:
  - `safe-fallback` (conservative no-index fallback)
  - `manual-template` (editable index template for hand/discard/call fields)

At startup, the plugin creates this file automatically if it does not exist.
Switch `selectedProfile` in the JSON and restart the plugin to swap mappings.
