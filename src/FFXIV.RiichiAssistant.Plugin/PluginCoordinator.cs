using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Plugin;

public sealed record PluginFrame(
    PluginSessionState SessionState,
    RiichiAnalysis Analysis,
    IReadOnlyList<DiscardRecommendation> TopDiscards,
    CallRecommendation? PendingCallRecommendation,
    StrategicTurnPlan? Strategy);

public sealed class PluginCoordinator
{
    private readonly IRiichiAnalysisEngine analysisEngine;
    private readonly IRecommendationEngine recommendationEngine;
    private readonly IStrategicPolicyEngine strategicPolicyEngine;

    public PluginCoordinator(
        IRiichiAnalysisEngine analysisEngine,
        IRecommendationEngine recommendationEngine,
        IStrategicPolicyEngine strategicPolicyEngine)
    {
        this.analysisEngine = analysisEngine;
        this.recommendationEngine = recommendationEngine;
        this.strategicPolicyEngine = strategicPolicyEngine;
    }

    public PluginFrame Update(
        MahjongTableSnapshot? snapshot,
        IEnumerable<DiscardCandidateEvaluation>? discardCandidates = null,
        CallRecommendationInput? pendingCall = null)
    {
        var analysis = analysisEngine.Analyze(snapshot);
        if (analysis.SessionState != PluginSessionState.InRound)
        {
            return new PluginFrame(analysis.SessionState, analysis, Array.Empty<DiscardRecommendation>(), null, null);
        }

        StrategicTurnPlan? strategy = null;
        var topDiscards = discardCandidates is not null
            ? recommendationEngine.GetTopDiscards(discardCandidates)
            : snapshot is not null
                ? (strategy = strategicPolicyEngine.Evaluate(snapshot)).TopDiscards
                : Array.Empty<DiscardRecommendation>();
        var callRecommendation = pendingCall is not null
            ? recommendationEngine.RecommendCall(pendingCall)
            : strategy?.PendingCallRecommendation;
        return new PluginFrame(analysis.SessionState, analysis, topDiscards, callRecommendation, strategy);
    }
}