using FFXIV.RiichiAssistant.Core;

namespace FFXIV.RiichiAssistant.Riichi;

public sealed record DoraSummary(int DoraCount, int AkaDoraCount)
{
    public int TotalCount => DoraCount + AkaDoraCount;
}

public sealed record RiichiAnalysis(
    PluginSessionState SessionState,
    DoraSummary Dora,
    bool IsClosedHand,
    ShantenResult? Shanten,
    int VisibleTileCount,
    IReadOnlyList<string> Warnings);

public interface IRiichiAnalysisEngine
{
    RiichiAnalysis Analyze(MahjongTableSnapshot? snapshot);
}

public sealed class RiichiAnalysisEngine : IRiichiAnalysisEngine
{
    private readonly IShantenSolver shantenSolver;

    public RiichiAnalysisEngine()
        : this(new ShantenSolver())
    {
    }

    public RiichiAnalysisEngine(IShantenSolver shantenSolver)
    {
        this.shantenSolver = shantenSolver;
    }

    public RiichiAnalysis Analyze(MahjongTableSnapshot? snapshot)
    {
        var sessionState = SessionStateEvaluator.Evaluate(snapshot);
        if (snapshot is null)
        {
            return new RiichiAnalysis(sessionState, new DoraSummary(0, 0), true, null, 0, ["No Mahjong snapshot is available."]);
        }

        var localPlayer = snapshot.GetLocalPlayer();
        var allOwnedTiles = snapshot.Hand;
        var akaDoraCount = allOwnedTiles.Count(tile => tile.IsRed)
            + (localPlayer?.OpenMelds.Sum(meld => meld.Tiles.Count(tile => tile.IsRed)) ?? 0);
        var doraCount = CountDora(snapshot.DoraIndicators, allOwnedTiles)
            + CountDora(snapshot.DoraIndicators, localPlayer?.OpenMelds.SelectMany(meld => meld.Tiles) ?? []);
        var warnings = new List<string>();
        var shanten = snapshot.HasValidHandCount && localPlayer is not null
            ? shantenSolver.Analyze(snapshot.Hand, snapshot.VisibleTiles, localPlayer.OpenMelds.Count)
            : null;

        if (!snapshot.HasValidHandCount)
        {
            warnings.Add("Hand tile count is not yet stable.");
        }

        if (!snapshot.IsStructureUpdateObserved)
        {
            warnings.Add("Mahjong structures have not started updating yet.");
        }

        return new RiichiAnalysis(
            sessionState,
            new DoraSummary(doraCount, akaDoraCount),
            localPlayer?.OpenMelds.Count == 0,
            shanten,
            snapshot.VisibleTiles.Count,
            warnings);
    }

    private static int CountDora(IEnumerable<Tile> indicators, IEnumerable<Tile> tiles)
    {
        var doraTiles = indicators.Select(GetDoraFromIndicator).ToArray();
        return tiles.Count(tile => doraTiles.Contains(Normalize(tile)));
    }

    private static Tile Normalize(Tile tile)
    {
        return tile.IsRed ? tile with { IsRed = false } : tile;
    }

    private static Tile GetDoraFromIndicator(Tile indicator)
    {
        if (indicator.Suit == TileSuit.Honor)
        {
            return indicator.Rank switch
            {
                1 => new Tile(TileSuit.Honor, 2),
                2 => new Tile(TileSuit.Honor, 3),
                3 => new Tile(TileSuit.Honor, 4),
                4 => new Tile(TileSuit.Honor, 1),
                5 => new Tile(TileSuit.Honor, 6),
                6 => new Tile(TileSuit.Honor, 7),
                7 => new Tile(TileSuit.Honor, 5),
                _ => Normalize(indicator),
            };
        }

        var nextRank = indicator.Rank == 9 ? 1 : indicator.Rank + 1;
        return new Tile(indicator.Suit, nextRank);
    }
}