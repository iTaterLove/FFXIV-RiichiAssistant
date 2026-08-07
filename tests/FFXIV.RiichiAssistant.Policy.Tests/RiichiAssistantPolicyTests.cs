using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Policy;
using FFXIV.RiichiAssistant.Policy.Abstractions;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Policy.Tests;

public class RiichiAssistantPolicyTests
{
    [Fact]
    public void Evaluate_ProducesPolicyDecisionWithBestDiscard()
    {
        var strategic = new StrategicPolicyEngine();
        IPolicy policy = new RiichiAssistantPolicy(
            new StrategyDrivenDiscardPolicy(strategic),
            new StrategyDrivenCallPolicy(strategic),
            new StrategyDrivenRiichiPolicy(strategic),
            new StrategyDrivenPushFoldPolicy(strategic));

        var result = policy.Evaluate(CreateSnapshot());

        Assert.NotNull(result.BestDiscard);
        Assert.Equal(3, result.TopDiscards.Count);
    }

    [Fact]
    public void Evaluate_MapsPendingCallIntoPolicyCallDecision()
    {
        var strategic = new StrategicPolicyEngine();
        IPolicy policy = new RiichiAssistantPolicy(
            new StrategyDrivenDiscardPolicy(strategic),
            new StrategyDrivenCallPolicy(strategic),
            new StrategyDrivenRiichiPolicy(strategic),
            new StrategyDrivenPushFoldPolicy(strategic));

        var result = policy.Evaluate(CreateSnapshot(new CallOpportunity(CallType.Pon, new Tile(TileSuit.Honor, 5), Array.Empty<IReadOnlyList<Tile>>())));

        Assert.NotNull(result.Call);
        Assert.Equal(CallType.Pon, result.Call!.CallType);
    }

    private static MahjongTableSnapshot CreateSnapshot(CallOpportunity? pendingCall = null)
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
                new Tile(TileSuit.Pin, 5),
                new Tile(TileSuit.Sou, 2),
                new Tile(TileSuit.Sou, 5),
                new Tile(TileSuit.Sou, 8),
                new Tile(TileSuit.Honor, 1),
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
            ],
            PendingCallOpportunity: pendingCall);
    }
}
