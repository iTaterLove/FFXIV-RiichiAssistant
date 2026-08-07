# Architecture

This repository follows a layered structure inspired by production Dalamud plugin repos while preserving RiichiAssistant's existing Mahjong logic.

## Layers

- FFXIV.RiichiAssistant.Plugin: Dalamud host layer, command handling, UI rendering, addon integration.
- FFXIV.RiichiAssistant.Plugin.Game: Dalamud-free runtime contracts between addon readers and policy orchestration.
- FFXIV.RiichiAssistant.Policy.Abstractions: Policy contracts and decision DTOs.
- FFXIV.RiichiAssistant.Policy: Concrete policies composed from existing RiichiAssistant strategy/recommendation engines.
- FFXIV.RiichiAssistant.Decision: Existing decision logic including recommendation and strategic offense/defense planning.
- FFXIV.RiichiAssistant.Riichi: Shanten and scoring analysis.
- FFXIV.RiichiAssistant.Core: Domain model and shared primitives.

## Dependency direction

Plugin -> Plugin.Game / Policy / Decision / Riichi / Core
Policy -> Policy.Abstractions / Decision / Core
Decision -> Riichi / Core
Riichi -> Core
Plugin.Game -> Policy.Abstractions / Core
Policy.Abstractions -> Core
Core -> (none)

## Notes

- Production-style modularization is now in place.
- Existing Mahjong logic is reused through adapters in Policy.
- The plugin still uses the existing strategic and recommendation engines, now also surfaced via policy abstractions.
