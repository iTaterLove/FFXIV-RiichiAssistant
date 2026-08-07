using FFXIV.RiichiAssistant.Core;

namespace FFXIV.RiichiAssistant.Policy.Abstractions;

public sealed record ScoredDiscard(
    Tile Tile,
    int ResultingShanten,
    int UkeireCount,
    double ExpectedValue,
    double Risk,
    string Reason);

public sealed record CallDecision(CallType CallType, bool ShouldCall, string Reason);

public sealed record RiichiDecision(bool ShouldRiichi, string Reason);

public sealed record PushFoldDecision(bool ShouldPush, ThreatLevel ThreatLevel, string Reason);

public sealed record PolicyDecision(
    IReadOnlyList<ScoredDiscard> TopDiscards,
    ScoredDiscard? BestDiscard,
    CallDecision? Call,
    RiichiDecision? Riichi,
    PushFoldDecision PushFold,
    IReadOnlyList<string> Notes);
