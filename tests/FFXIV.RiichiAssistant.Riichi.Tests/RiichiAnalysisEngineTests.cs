using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Riichi.Tests;

public class RiichiAnalysisEngineTests
{
    [Fact]
    public void Analyze_CountsIndicatorDoraAndAkaDora()
    {
        var snapshot = CreateSnapshot(
            hand:
            [
                new Tile(TileSuit.Man, 5, true),
                new Tile(TileSuit.Man, 3),
                new Tile(TileSuit.Pin, 9),
                new Tile(TileSuit.Sou, 1),
                new Tile(TileSuit.Sou, 2),
                new Tile(TileSuit.Sou, 3),
                new Tile(TileSuit.Pin, 2),
                new Tile(TileSuit.Pin, 3),
                new Tile(TileSuit.Pin, 4),
                new Tile(TileSuit.Man, 7),
                new Tile(TileSuit.Man, 8),
                new Tile(TileSuit.Man, 9),
                new Tile(TileSuit.Honor, 1),
            ],
            doraIndicators: [new Tile(TileSuit.Man, 2), new Tile(TileSuit.Pin, 8)]);

        var analysis = new RiichiAnalysisEngine().Analyze(snapshot);

        Assert.Equal(2, analysis.Dora.DoraCount);
        Assert.Equal(1, analysis.Dora.AkaDoraCount);
        Assert.True(analysis.IsClosedHand);
        Assert.NotNull(analysis.Shanten);
    }

    [Fact]
    public void EvaluateSessionState_RequiresLiveValidStructuresForInRound()
    {
        var waiting = CreateSnapshot(isStructureUpdateObserved: false);
        var inRound = CreateSnapshot();

        Assert.Equal(PluginSessionState.WaitingForRoundStart, SessionStateEvaluator.Evaluate(waiting));
        Assert.Equal(PluginSessionState.InRound, SessionStateEvaluator.Evaluate(inRound));
    }

    [Fact]
    public void ShantenSolver_FindsWinningTilesForTenpaiHand()
    {
        var solver = new ShantenSolver();
        Tile[] hand =
        [
            new Tile(TileSuit.Man, 1),
            new Tile(TileSuit.Man, 2),
            new Tile(TileSuit.Man, 3),
            new Tile(TileSuit.Man, 4),
            new Tile(TileSuit.Man, 5),
            new Tile(TileSuit.Man, 6),
            new Tile(TileSuit.Pin, 2),
            new Tile(TileSuit.Pin, 3),
            new Tile(TileSuit.Pin, 4),
            new Tile(TileSuit.Sou, 7),
            new Tile(TileSuit.Sou, 8),
            new Tile(TileSuit.Sou, 9),
            new Tile(TileSuit.Honor, 5),
        ];

        var result = solver.Analyze(hand);

        Assert.Equal(0, result.Shanten);
        Assert.Contains(result.UkeireTiles, entry => entry.Tile == new Tile(TileSuit.Honor, 5));
    }

    [Fact]
    public void ShantenSolver_UsesVisibleTilesToReduceUkeire()
    {
        var solver = new ShantenSolver();
        Tile[] hand =
        [
            new Tile(TileSuit.Man, 1),
            new Tile(TileSuit.Man, 2),
            new Tile(TileSuit.Man, 3),
            new Tile(TileSuit.Man, 4),
            new Tile(TileSuit.Man, 5),
            new Tile(TileSuit.Man, 6),
            new Tile(TileSuit.Pin, 2),
            new Tile(TileSuit.Pin, 3),
            new Tile(TileSuit.Pin, 4),
            new Tile(TileSuit.Sou, 7),
            new Tile(TileSuit.Sou, 8),
            new Tile(TileSuit.Sou, 9),
            new Tile(TileSuit.Honor, 5),
        ];

        var result = solver.Analyze(hand, [new Tile(TileSuit.Honor, 5), new Tile(TileSuit.Honor, 5)]);

        Assert.Equal(1, result.UkeireTiles.Single(entry => entry.Tile == new Tile(TileSuit.Honor, 5)).RemainingCopies);
    }

    [Fact]
    public void HandScoringEngine_ScoresClosedTanyaoPinfuTsumo()
    {
        var engine = new HandScoringEngine();
        Tile[] concealedTiles =
        [
            new Tile(TileSuit.Man, 2),
            new Tile(TileSuit.Man, 3),
            new Tile(TileSuit.Man, 4),
            new Tile(TileSuit.Man, 3),
            new Tile(TileSuit.Man, 4),
            new Tile(TileSuit.Man, 5),
            new Tile(TileSuit.Pin, 4),
            new Tile(TileSuit.Pin, 5),
            new Tile(TileSuit.Pin, 6),
            new Tile(TileSuit.Sou, 5),
            new Tile(TileSuit.Sou, 6),
            new Tile(TileSuit.Sou, 7),
            new Tile(TileSuit.Pin, 2),
            new Tile(TileSuit.Pin, 2),
        ];

        var result = engine.Evaluate(
            concealedTiles,
            Array.Empty<Meld>(),
            new HandScoringContext(Wind.East, Wind.South, WinType.Tsumo, false, new Tile(TileSuit.Sou, 5), Array.Empty<Tile>()));

        Assert.True(result.IsWinningHand);
        Assert.Equal(3, result.Han);
        Assert.Equal(20, result.Fu);
        Assert.Equal(WaitType.Ryanmen, result.WaitType);
        Assert.Contains(result.YakuValues, value => value.Yaku == Yaku.Tanyao);
        Assert.Contains(result.YakuValues, value => value.Yaku == Yaku.Pinfu);
        Assert.Contains(result.YakuValues, value => value.Yaku == Yaku.MenzenTsumo);
    }

    [Fact]
    public void HandScoringEngine_AddsWaitFuAndOpenHandYaku()
    {
        var engine = new HandScoringEngine();
        Tile[] concealedTiles =
        [
            new Tile(TileSuit.Honor, 1),
            new Tile(TileSuit.Honor, 1),
            new Tile(TileSuit.Pin, 1),
            new Tile(TileSuit.Pin, 2),
            new Tile(TileSuit.Pin, 3),
            new Tile(TileSuit.Sou, 7),
            new Tile(TileSuit.Sou, 8),
            new Tile(TileSuit.Sou, 9),
        ];
        Meld[] openMelds =
        [
            new Meld(MeldType.Sequence, [new Tile(TileSuit.Man, 1), new Tile(TileSuit.Man, 2), new Tile(TileSuit.Man, 3)], true),
            new Meld(MeldType.Sequence, [new Tile(TileSuit.Man, 7), new Tile(TileSuit.Man, 8), new Tile(TileSuit.Man, 9)], true),
        ];

        var result = engine.Evaluate(
            concealedTiles,
            openMelds,
            new HandScoringContext(Wind.East, Wind.South, WinType.Ron, false, new Tile(TileSuit.Sou, 7), Array.Empty<Tile>()));

        Assert.True(result.IsWinningHand);
        Assert.Equal(WaitType.Penchan, result.WaitType);
        Assert.True(result.Fu >= 30);
        Assert.Contains(result.YakuValues, value => value.Yaku == Yaku.Chanta);
    }

    [Fact]
    public void HandScoringEngine_ComparesRiichiAndDama()
    {
        var engine = new HandScoringEngine();
        Tile[] concealedTiles =
        [
            new Tile(TileSuit.Man, 2),
            new Tile(TileSuit.Man, 3),
            new Tile(TileSuit.Man, 4),
            new Tile(TileSuit.Man, 3),
            new Tile(TileSuit.Man, 4),
            new Tile(TileSuit.Man, 5),
            new Tile(TileSuit.Pin, 4),
            new Tile(TileSuit.Pin, 5),
            new Tile(TileSuit.Pin, 6),
            new Tile(TileSuit.Sou, 6),
            new Tile(TileSuit.Sou, 7),
            new Tile(TileSuit.Sou, 8),
            new Tile(TileSuit.Pin, 2),
            new Tile(TileSuit.Pin, 2),
        ];

        var comparison = engine.EvaluateRiichiAndDama(
            concealedTiles,
            Array.Empty<Meld>(),
            new HandScoringContext(Wind.East, Wind.South, WinType.Ron, false, new Tile(TileSuit.Pin, 2), Array.Empty<Tile>()));

        Assert.True(comparison.PreferRiichi);
        Assert.True(comparison.Riichi.Han > comparison.Dama.Han);
    }

    private static MahjongTableSnapshot CreateSnapshot(
        IReadOnlyList<Tile>? hand = null,
        IReadOnlyList<Tile>? doraIndicators = null,
        bool isStructureUpdateObserved = true)
    {
        return new MahjongTableSnapshot(
            IsMahjongContentActive: true,
            IsStructureUpdateObserved: isStructureUpdateObserved,
            IsRoundActive: true,
            IsRoundEnded: false,
            LocalPlayerIndex: 0,
            RoundWind: Wind.East,
            SeatWind: Wind.East,
            Honba: 0,
            RiichiSticks: 0,
            Hand: hand ??
            [
                new Tile(TileSuit.Man, 1),
                new Tile(TileSuit.Man, 2),
                new Tile(TileSuit.Man, 3),
                new Tile(TileSuit.Pin, 1),
                new Tile(TileSuit.Pin, 2),
                new Tile(TileSuit.Pin, 3),
                new Tile(TileSuit.Sou, 1),
                new Tile(TileSuit.Sou, 2),
                new Tile(TileSuit.Sou, 3),
                new Tile(TileSuit.Honor, 1),
                new Tile(TileSuit.Honor, 1),
                new Tile(TileSuit.Honor, 5),
                new Tile(TileSuit.Honor, 5),
            ],
            DoraIndicators: doraIndicators ?? Array.Empty<Tile>(),
            VisibleTiles: Array.Empty<Tile>(),
            Players:
            [
                new PlayerSnapshot(0, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(1, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(2, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(3, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
            ]);
    }
}