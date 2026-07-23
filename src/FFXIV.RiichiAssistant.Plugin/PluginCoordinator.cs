using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Plugin;

public sealed record PluginFrame(
    PluginSessionState SessionState,
    RiichiAnalysis Analysis,
    IReadOnlyList<DiscardRecommendation> TopDiscards,
    CallRecommendation? PendingCallRecommendation);

public sealed class PluginCoordinator
{
    private readonly IRiichiAnalysisEngine analysisEngine;
    private readonly IRecommendationEngine recommendationEngine;

    public PluginCoordinator(IRiichiAnalysisEngine analysisEngine, IRecommendationEngine recommendationEngine)
    {
        this.analysisEngine = analysisEngine;
        this.recommendationEngine = recommendationEngine;
    }

    public PluginFrame Update(
        MahjongTableSnapshot? snapshot,
        IEnumerable<DiscardCandidateEvaluation>? discardCandidates = null,
        CallRecommendationInput? pendingCall = null)
    {
        var analysis = analysisEngine.Analyze(snapshot);
        if (analysis.SessionState != PluginSessionState.InRound)
        {
            return new PluginFrame(analysis.SessionState, analysis, Array.Empty<DiscardRecommendation>(), null);
        }

        var topDiscards = discardCandidates is not null
            ? recommendationEngine.GetTopDiscards(discardCandidates)
            : snapshot is not null
                ? recommendationEngine.GetTopDiscards(snapshot)
                : Array.Empty<DiscardRecommendation>();
        var callRecommendation = pendingCall is null ? null : recommendationEngine.RecommendCall(pendingCall);
        return new PluginFrame(analysis.SessionState, analysis, topDiscards, callRecommendation);
    }
}