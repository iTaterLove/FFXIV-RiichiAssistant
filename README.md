# FFXIV-RiichiAssistant

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

## Repository Layout (initial)

- `repo.json` — Dalamud custom repo index
- `src/FFXIV.RiichiAssistant/` — plugin project scaffold (to be added)
- `artifacts/` — release zip artifacts (or GitHub Releases assets)
- `docs/` — implementation notes and roadmap

## Status

Scaffold in progress.
