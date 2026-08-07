# FFXIV.RiichiAssistant.Policy.Abstractions

Defines policy contracts and decision DTOs without any Dalamud dependency.

This mirrors production-grade separation: plugin and game-state layers consume
interfaces, while concrete policies live in a separate project.
