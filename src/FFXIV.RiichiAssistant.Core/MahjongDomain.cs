using System.Globalization;

namespace FFXIV.RiichiAssistant.Core;

public enum PluginSessionState
{
    Inactive,
    WaitingForRoundStart,
    InRound,
    RoundEnd,
}

public enum Wind
{
    East,
    South,
    West,
    North,
}

public enum TileSuit
{
    Man,
    Pin,
    Sou,
    Honor,
}

public enum MeldType
{
    Sequence,
    Triplet,
    Kan,
}

public enum CallType
{
    Chi,
    Pon,
    Kan,
    Riichi,
    Ron,
    Tsumo,
}

public readonly record struct Tile(TileSuit Suit, int Rank, bool IsRed = false)
{
    public bool IsHonor => Suit == TileSuit.Honor;

    public bool IsTerminal => !IsHonor && (Rank == 1 || Rank == 9);

    public override string ToString()
    {
        var rankLabel = Rank.ToString(CultureInfo.InvariantCulture);
        var suitLabel = Suit switch
        {
            TileSuit.Man => "m",
            TileSuit.Pin => "p",
            TileSuit.Sou => "s",
            TileSuit.Honor => "z",
            _ => "?",
        };

        return IsRed ? $"{rankLabel}{suitLabel}r" : $"{rankLabel}{suitLabel}";
    }
}

public sealed record Meld(MeldType Type, IReadOnlyList<Tile> Tiles, bool IsOpen = true, int? CalledFromPlayerIndex = null);

public sealed record PlayerSnapshot(
    int PlayerIndex,
    int Score,
    bool IsRiichi,
    int DiscardCount,
    IReadOnlyList<Tile> Discards,
    IReadOnlyList<Meld> OpenMelds);

public sealed record CallOpportunity(
    CallType CallType,
    Tile ClaimedTile,
    IReadOnlyList<IReadOnlyList<Tile>> CandidateGroups);

public sealed record MahjongTableSnapshot(
    bool IsMahjongContentActive,
    bool IsStructureUpdateObserved,
    bool IsRoundActive,
    bool IsRoundEnded,
    int LocalPlayerIndex,
    Wind RoundWind,
    Wind SeatWind,
    int Honba,
    int RiichiSticks,
    IReadOnlyList<Tile> Hand,
    IReadOnlyList<Tile> DoraIndicators,
    IReadOnlyList<Tile> VisibleTiles,
    IReadOnlyList<PlayerSnapshot> Players,
    CallOpportunity? PendingCallOpportunity = null)
{
    public bool HasValidHandCount => Hand.Count is 13 or 14;

    public bool HasValidPlayerCount => Players.Count == 4;

    public bool HasValidScores => Players.All(player => player.Score >= 0);

    public bool IsValidForRecommendations =>
        IsMahjongContentActive &&
        IsStructureUpdateObserved &&
        IsRoundActive &&
        HasValidHandCount &&
        HasValidPlayerCount &&
        HasValidScores;

    public PlayerSnapshot? GetLocalPlayer()
    {
        return Players.FirstOrDefault(player => player.PlayerIndex == LocalPlayerIndex);
    }
}

public static class SessionStateEvaluator
{
    public static PluginSessionState Evaluate(MahjongTableSnapshot? snapshot)
    {
        if (snapshot is null || !snapshot.IsMahjongContentActive)
        {
            return PluginSessionState.Inactive;
        }

        if (snapshot.IsRoundEnded)
        {
            return PluginSessionState.RoundEnd;
        }

        if (snapshot.IsValidForRecommendations)
        {
            return PluginSessionState.InRound;
        }

        return PluginSessionState.WaitingForRoundStart;
    }
}