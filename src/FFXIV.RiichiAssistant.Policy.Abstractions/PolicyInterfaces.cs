using FFXIV.RiichiAssistant.Core;

namespace FFXIV.RiichiAssistant.Policy.Abstractions;

public interface IDiscardPolicy
{
    IReadOnlyList<ScoredDiscard> RankDiscards(MahjongTableSnapshot snapshot, int maxCount = 3);
}

public interface ICallPolicy
{
    CallDecision? ChooseCall(MahjongTableSnapshot snapshot);
}

public interface IRiichiPolicy
{
    RiichiDecision? ChooseRiichi(MahjongTableSnapshot snapshot);
}

public interface IPushFoldPolicy
{
    PushFoldDecision Evaluate(MahjongTableSnapshot snapshot);
}

public interface IPolicy
{
    PolicyDecision Evaluate(MahjongTableSnapshot snapshot);
}
