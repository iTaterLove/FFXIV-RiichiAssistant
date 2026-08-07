using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Decision.Tests;

public class StrategicPolicyEngineTests
{
    [Fact]
    public void Evaluate_HighThreatAndFarFromTenpai_SetsFoldAndDeclinesSpeedCall()
    {
        var engine = new StrategicPolicyEngine();
        var snapshot = CreateSnapshot(
            players:
            [
                new PlayerSnapshot(0, 25000, false, 2, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(1, 26000, true, 8, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(2, 24000, true, 7, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(3, 25000, false, 6, Array.Empty<Tile>(), Array.Empty<Meld>()),
            ],
            pendingCall: new CallOpportunity(CallType.Pon, new Tile(TileSuit.Honor, 5), Array.Empty<IReadOnlyList<Tile>>()));

        var plan = engine.Evaluate(snapshot);

        Assert.Equal(ThreatLevel.High, plan.Threat.ThreatLevel);
        Assert.False(plan.ShouldPush);
        Assert.NotNull(plan.PendingCallRecommendation);
        Assert.False(plan.PendingCallRecommendation!.ShouldCall);
        Assert.Equal(CallType.Pon, plan.PendingCallRecommendation.CallType);
    }

    [Fact]
    public void Evaluate_LowThreatRiichiOpportunity_AllowsRiichiPush()
    {
        var engine = new StrategicPolicyEngine();
        var snapshot = CreateSnapshot(
            hand:
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
                new Tile(TileSuit.Honor, 5),
            ],
            pendingCall: new CallOpportunity(CallType.Riichi, new Tile(TileSuit.Honor, 5), Array.Empty<IReadOnlyList<Tile>>()));

        var plan = engine.Evaluate(snapshot);

        Assert.Equal(ThreatLevel.Low, plan.Threat.ThreatLevel);
        Assert.True(plan.ShouldPush);
        Assert.NotNull(plan.PendingCallRecommendation);
        Assert.True(plan.PendingCallRecommendation!.ShouldCall);
        Assert.Equal(CallType.Riichi, plan.PendingCallRecommendation.CallType);
    }

    [Fact]
    public void Evaluate_ReturnsBestDiscardAndTopThreeCandidates()
    {
        var engine = new StrategicPolicyEngine();
        var snapshot = CreateSnapshot();

        var plan = engine.Evaluate(snapshot);

        Assert.NotNull(plan.BestDiscard);
        Assert.Equal(3, plan.TopDiscards.Count);
    }

    [Fact]
    public void Evaluate_HighThreatPrefersGenbutsuHonorDiscard()
    {
        var engine = new StrategicPolicyEngine();
        var snapshot = CreateSnapshot(
            hand:
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
                new Tile(TileSuit.Honor, 6),
            ],
            players:
            [
                new PlayerSnapshot(0, 25000, false, 2, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(1, 26000, true, 7, [new Tile(TileSuit.Honor, 5)], Array.Empty<Meld>()),
                new PlayerSnapshot(2, 24000, false, 6, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(3, 25000, false, 5, Array.Empty<Tile>(), Array.Empty<Meld>()),
            ]);

        var plan = engine.Evaluate(snapshot);

        Assert.NotNull(plan.BestDiscard);
        Assert.Equal(new Tile(TileSuit.Honor, 5), plan.BestDiscard!.Tile);
    }

    [Fact]
    public void Evaluate_RiichiSujiSignalImprovesSafetyRanking()
    {
        var engine = new StrategicPolicyEngine();
        var snapshot = CreateSnapshot(
            hand:
            [
                new Tile(TileSuit.Man, 1),
                new Tile(TileSuit.Man, 2),
                new Tile(TileSuit.Pin, 2),
                new Tile(TileSuit.Pin, 3),
                new Tile(TileSuit.Pin, 4),
                new Tile(TileSuit.Pin, 6),
                new Tile(TileSuit.Pin, 7),
                new Tile(TileSuit.Pin, 8),
                new Tile(TileSuit.Sou, 2),
                new Tile(TileSuit.Sou, 3),
                new Tile(TileSuit.Sou, 4),
                new Tile(TileSuit.Sou, 5),
                new Tile(TileSuit.Sou, 6),
                new Tile(TileSuit.Sou, 7),
            ],
            players:
            [
                new PlayerSnapshot(0, 25000, false, 2, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(1, 26000, true, 7, [new Tile(TileSuit.Man, 4)], Array.Empty<Meld>()),
                new PlayerSnapshot(2, 24000, false, 6, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(3, 25000, false, 5, Array.Empty<Tile>(), Array.Empty<Meld>()),
            ],
            visibleTiles:
            [
                new Tile(TileSuit.Man, 3),
                new Tile(TileSuit.Man, 3),
                new Tile(TileSuit.Man, 3),
                new Tile(TileSuit.Man, 3),
            ]);

        var plan = engine.Evaluate(snapshot);

        Assert.NotNull(plan.BestDiscard);
        Assert.Equal(new Tile(TileSuit.Man, 1), plan.BestDiscard!.Tile);
    }

    private static MahjongTableSnapshot CreateSnapshot(
        IReadOnlyList<Tile>? hand = null,
        IReadOnlyList<PlayerSnapshot>? players = null,
        CallOpportunity? pendingCall = null,
        IReadOnlyList<Tile>? visibleTiles = null)
    {
        return new MahjongTableSnapshot(
            IsMahjongContentActive: true,
            IsStructureUpdateObserved: true,
            IsRoundActive: true,
            IsRoundEnded: false,
            LocalPlayerIndex: 0,
            RoundWind: Wind.East,
            SeatWind: Wind.South,
            Honba: 0,
            RiichiSticks: 0,
            Hand: hand ??
            [
                new Tile(TileSuit.Man, 1),
                new Tile(TileSuit.Man, 2),
                new Tile(TileSuit.Man, 3),
                new Tile(TileSuit.Man, 4),
                new Tile(TileSuit.Man, 5),
                new Tile(TileSuit.Man, 6),
                new Tile(TileSuit.Pin, 2),
                new Tile(TileSuit.Pin, 3),
                new Tile(TileSuit.Pin, 5),
                new Tile(TileSuit.Sou, 2),
                new Tile(TileSuit.Sou, 5),
                new Tile(TileSuit.Sou, 8),
                new Tile(TileSuit.Honor, 1),
                new Tile(TileSuit.Honor, 7),
            ],
            DoraIndicators: Array.Empty<Tile>(),
            VisibleTiles: visibleTiles ?? Array.Empty<Tile>(),
            Players: players ??
            [
                new PlayerSnapshot(0, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(1, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(2, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
                new PlayerSnapshot(3, 25000, false, 0, Array.Empty<Tile>(), Array.Empty<Meld>()),
            ],
            PendingCallOpportunity: pendingCall);
    }
}
