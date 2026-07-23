using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Decision.Tests;

public class RecommendationEngineTests
{
    [Fact]
    public void GetTopDiscards_UsesShantenThenUkeireThenExpectedValue()
    {
        var engine = new RecommendationEngine();
        var recommendations = engine.GetTopDiscards(
        [
            new DiscardCandidateEvaluation(new Tile(TileSuit.Man, 9), 1, 14, 2400, 0.2, [new Tile(TileSuit.Pin, 3)], "keeps value"),
            new DiscardCandidateEvaluation(new Tile(TileSuit.Pin, 1), 0, 8, 3900, 0.4, [new Tile(TileSuit.Sou, 6)], "reaches tenpai"),
            new DiscardCandidateEvaluation(new Tile(TileSuit.Sou, 9), 0, 12, 2600, 0.1, [new Tile(TileSuit.Man, 6)], "best ukeire"),
            new DiscardCandidateEvaluation(new Tile(TileSuit.Honor, 7), 0, 12, 3200, 0.3, [new Tile(TileSuit.Pin, 6)], "same ukeire, higher value"),
        ]);

        Assert.Collection(
            recommendations,
            first => Assert.Equal(new Tile(TileSuit.Honor, 7), first.Tile),
            second => Assert.Equal(new Tile(TileSuit.Sou, 9), second.Tile),
            third => Assert.Equal(new Tile(TileSuit.Pin, 1), third.Tile));
    }

    [Theory]
    [InlineData(CallType.Ron)]
    [InlineData(CallType.Tsumo)]
    public void RecommendCall_AlwaysAcceptsWinningCalls(CallType callType)
    {
        var engine = new RecommendationEngine();
        var recommendation = engine.RecommendCall(new CallRecommendationInput(callType, 0, 3, true, false, true, true));

        Assert.True(recommendation.ShouldCall);
    }

    [Fact]
    public void RecommendCall_OnlyTakesSpeedCallsWhenValueTargetHolds()
    {
        var engine = new RecommendationEngine();

        var accept = engine.RecommendCall(new CallRecommendationInput(CallType.Pon, -1, 2, true, false, false, false));
        var decline = engine.RecommendCall(new CallRecommendationInput(CallType.Pon, -1, 0, false, false, false, false));

        Assert.True(accept.ShouldCall);
        Assert.False(decline.ShouldCall);
    }

    [Fact]
    public void GetTopDiscards_FromSnapshotBuildsRealDiscardSimulation()
    {
        var engine = new RecommendationEngine(new ShantenSolver(), new HandScoringEngine());
        var snapshot = new MahjongTableSnapshot(
            IsMahjongContentActive: true,
            IsStructureUpdateObserved: true,
            IsRoundActive: true,
            IsRoundEnded: false,
            LocalPlayerIndex: 0,
            RoundWind: Wind.East,
            SeatWind: Wind.South,
            Honba: 0,
            RiichiSticks: 0,
            Hand:
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
                new Tile(TileSuit.Honor, 7),
            ],
            DoraIndicators: Array.Empty<Tile>(),
            VisibleTiles: Array.Empty<Tile>(),
            Players:
            [
                new PlayerSnapshot(0, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(1, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(2, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(3, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
            ]);

        var recommendations = engine.GetTopDiscards(snapshot);

        Assert.Equal(3, recommendations.Count);
        Assert.Contains(recommendations, recommendation => recommendation.Tile == new Tile(TileSuit.Honor, 7));
        Assert.All(recommendations, recommendation => Assert.NotNull(recommendation.MainUkeireTiles));
    }

    [Fact]
    public void RecommendCall_UsesRiichiVsDamaComparisonWhenAvailable()
    {
        var engine = new RecommendationEngine();
        var recommendation = engine.RecommendCall(new CallRecommendationInput(
            CallType.Riichi,
            0,
            1,
            true,
            true,
            true,
            false,
            false,
            DamaExpectedValue: 3900,
            RiichiExpectedValue: 5200));

        Assert.True(recommendation.ShouldCall);
        Assert.Contains("expected points", recommendation.Reason);
    }
}