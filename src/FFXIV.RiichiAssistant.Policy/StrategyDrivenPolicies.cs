using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Policy.Abstractions;

namespace FFXIV.RiichiAssistant.Policy;

public sealed class StrategyDrivenDiscardPolicy : IDiscardPolicy
{
    private readonly IStrategicPolicyEngine strategicPolicyEngine;

    public StrategyDrivenDiscardPolicy(IStrategicPolicyEngine strategicPolicyEngine)
    {
        this.strategicPolicyEngine = strategicPolicyEngine;
    }

    public IReadOnlyList<ScoredDiscard> RankDiscards(MahjongTableSnapshot snapshot, int maxCount = 3)
    {
        var plan = strategicPolicyEngine.Evaluate(snapshot, maxCount);
        return plan.TopDiscards
            .Select(discard => new ScoredDiscard(
                discard.Tile,
                discard.ResultingShanten,
                discard.UkeireCount,
                discard.ExpectedValue,
                0,
                discard.Reason))
            .ToArray();
    }
}

public sealed class StrategyDrivenCallPolicy : ICallPolicy
{
    private readonly IStrategicPolicyEngine strategicPolicyEngine;

    public StrategyDrivenCallPolicy(IStrategicPolicyEngine strategicPolicyEngine)
    {
        this.strategicPolicyEngine = strategicPolicyEngine;
    }

    public CallDecision? ChooseCall(MahjongTableSnapshot snapshot)
    {
        var call = strategicPolicyEngine.Evaluate(snapshot).PendingCallRecommendation;
        return call is null ? null : new CallDecision(call.CallType, call.ShouldCall, call.Reason);
    }
}

public sealed class StrategyDrivenRiichiPolicy : IRiichiPolicy
{
    private readonly IStrategicPolicyEngine strategicPolicyEngine;

    public StrategyDrivenRiichiPolicy(IStrategicPolicyEngine strategicPolicyEngine)
    {
        this.strategicPolicyEngine = strategicPolicyEngine;
    }

    public RiichiDecision? ChooseRiichi(MahjongTableSnapshot snapshot)
    {
        var call = strategicPolicyEngine.Evaluate(snapshot).PendingCallRecommendation;
        if (call is null || call.CallType != CallType.Riichi)
        {
            return null;
        }

        return new RiichiDecision(call.ShouldCall, call.Reason);
    }
}

public sealed class StrategyDrivenPushFoldPolicy : IPushFoldPolicy
{
    private readonly IStrategicPolicyEngine strategicPolicyEngine;

    public StrategyDrivenPushFoldPolicy(IStrategicPolicyEngine strategicPolicyEngine)
    {
        this.strategicPolicyEngine = strategicPolicyEngine;
    }

    public PushFoldDecision Evaluate(MahjongTableSnapshot snapshot)
    {
        var plan = strategicPolicyEngine.Evaluate(snapshot);
        return new PushFoldDecision(plan.ShouldPush, plan.Threat.ThreatLevel, plan.PushFoldReason);
    }
}
