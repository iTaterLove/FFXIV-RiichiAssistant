using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Policy.Abstractions;

namespace FFXIV.RiichiAssistant.Policy;

public sealed class RiichiAssistantPolicy : IPolicy
{
    private readonly IDiscardPolicy discardPolicy;
    private readonly ICallPolicy callPolicy;
    private readonly IRiichiPolicy riichiPolicy;
    private readonly IPushFoldPolicy pushFoldPolicy;

    public RiichiAssistantPolicy(
        IDiscardPolicy discardPolicy,
        ICallPolicy callPolicy,
        IRiichiPolicy riichiPolicy,
        IPushFoldPolicy pushFoldPolicy)
    {
        this.discardPolicy = discardPolicy;
        this.callPolicy = callPolicy;
        this.riichiPolicy = riichiPolicy;
        this.pushFoldPolicy = pushFoldPolicy;
    }

    public PolicyDecision Evaluate(MahjongTableSnapshot snapshot)
    {
        var topDiscards = discardPolicy.RankDiscards(snapshot, 3);
        var bestDiscard = topDiscards.FirstOrDefault();
        var call = callPolicy.ChooseCall(snapshot);
        var riichi = riichiPolicy.ChooseRiichi(snapshot);
        var pushFold = pushFoldPolicy.Evaluate(snapshot);

        return new PolicyDecision(
            topDiscards,
            bestDiscard,
            call,
            riichi,
            pushFold,
            Notes:
            [
                "Policy decision produced by StrategyDriven* adapters over existing RiichiAssistant logic.",
                "This mirrors production-style separation between abstractions and concrete policy modules."
            ]);
    }
}
