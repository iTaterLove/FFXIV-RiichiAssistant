using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Decision;

public sealed record OpponentThreatAssessment(
    int RiichiOpponents,
    int OpenMeldPressure,
    ThreatLevel ThreatLevel,
    string Reason);

public sealed record StrategicTurnPlan(
    OpponentThreatAssessment Threat,
    bool ShouldPush,
    string PushFoldReason,
    IReadOnlyList<DiscardRecommendation> TopDiscards,
    DiscardRecommendation? BestDiscard,
    CallRecommendation? PendingCallRecommendation);

public interface IStrategicPolicyEngine
{
    StrategicTurnPlan Evaluate(MahjongTableSnapshot snapshot, int maxDiscards = 3);
}

public sealed class StrategicPolicyEngine : IStrategicPolicyEngine
{
    private readonly IRecommendationEngine recommendationEngine;
    private readonly IShantenSolver shantenSolver;

    public StrategicPolicyEngine()
        : this(new RecommendationEngine(), new ShantenSolver())
    {
    }

    public StrategicPolicyEngine(IRecommendationEngine recommendationEngine, IShantenSolver shantenSolver)
    {
        this.recommendationEngine = recommendationEngine;
        this.shantenSolver = shantenSolver;
    }

    public StrategicTurnPlan Evaluate(MahjongTableSnapshot snapshot, int maxDiscards = 3)
    {
        var localPlayer = snapshot.GetLocalPlayer();
        if (!snapshot.IsValidForRecommendations || localPlayer is null)
        {
            var idleThreat = new OpponentThreatAssessment(0, 0, ThreatLevel.Low, "No stable round state yet.");
            return new StrategicTurnPlan(idleThreat, false, "Waiting for a stable round state.", Array.Empty<DiscardRecommendation>(), null, null);
        }

        var shanten = shantenSolver.Analyze(snapshot.Hand, snapshot.VisibleTiles, localPlayer.OpenMelds.Count);
        var threat = AssessThreat(snapshot, localPlayer.PlayerIndex);
        var shouldPush = DeterminePushDecision(threat.ThreatLevel, shanten.Shanten);
        var pushFoldReason = BuildPushFoldReason(threat, shanten.Shanten, shouldPush);

        var candidates = BuildRiskAwareCandidates(snapshot, localPlayer, shanten, threat.ThreatLevel);
        var topDiscards = recommendationEngine.GetTopDiscards(candidates, maxDiscards);
        var bestDiscard = topDiscards.FirstOrDefault();

        var callRecommendation = snapshot.PendingCallOpportunity is null
            ? null
            : RecommendCall(snapshot, shanten, threat.ThreatLevel, shouldPush);

        return new StrategicTurnPlan(threat, shouldPush, pushFoldReason, topDiscards, bestDiscard, callRecommendation);
    }

    private static OpponentThreatAssessment AssessThreat(MahjongTableSnapshot snapshot, int localPlayerIndex)
    {
        var opponents = snapshot.Players.Where(player => player.PlayerIndex != localPlayerIndex).ToArray();
        var riichiOpponents = opponents.Count(player => player.IsRiichi);
        var openMeldPressure = opponents.Sum(player => player.OpenMelds.Count);

        var level = riichiOpponents switch
        {
            >= 2 => ThreatLevel.High,
            1 => ThreatLevel.Medium,
            _ when openMeldPressure >= 5 => ThreatLevel.Medium,
            _ => ThreatLevel.Low,
        };

        var reason = level switch
        {
            ThreatLevel.High => $"{riichiOpponents} opponents are in riichi; shift to defensive tile value.",
            ThreatLevel.Medium when riichiOpponents > 0 => "At least one opponent is in riichi; balance offense and defense.",
            ThreatLevel.Medium => "Opponents have multiple open melds; expect faster hand completion pressure.",
            _ => "No immediate opponent pressure detected; optimize for speed and value.",
        };

        return new OpponentThreatAssessment(riichiOpponents, openMeldPressure, level, reason);
    }

    private static bool DeterminePushDecision(ThreatLevel threatLevel, int shanten)
    {
        if (shanten <= 0)
        {
            return true;
        }

        return threatLevel switch
        {
            ThreatLevel.High => shanten <= 0,
            ThreatLevel.Medium => shanten <= 1,
            _ => true,
        };
    }

    private static string BuildPushFoldReason(OpponentThreatAssessment threat, int shanten, bool shouldPush)
    {
        if (shouldPush)
        {
            return threat.ThreatLevel switch
            {
                ThreatLevel.High => "Push is still justified because the hand is at or near completion.",
                ThreatLevel.Medium => "Push is acceptable while preserving safer discard options.",
                _ => "Push for efficiency: current table pressure is low.",
            };
        }

        return $"Fold posture recommended: threat {threat.ThreatLevel} with shanten {shanten} is too far from completion.";
    }

    private IReadOnlyList<DiscardCandidateEvaluation> BuildRiskAwareCandidates(
        MahjongTableSnapshot snapshot,
        PlayerSnapshot localPlayer,
        ShantenResult currentShanten,
        ThreatLevel threatLevel)
    {
        var candidates = new List<DiscardCandidateEvaluation>();
        var uniqueEntries = snapshot.Hand
            .Select((tile, index) => new { Tile = tile, Index = index })
            .GroupBy(entry => entry.Tile)
            .Select(group => group.First())
            .ToArray();

        var defenseWeight = threatLevel switch
        {
            ThreatLevel.High => 900.0,
            ThreatLevel.Medium => 350.0,
            _ => 0.0,
        };

        foreach (var entry in uniqueEntries)
        {
            var reducedHand = snapshot.Hand.Where((_, index) => index != entry.Index).ToArray();
            var result = shantenSolver.Analyze(reducedHand, snapshot.VisibleTiles, localPlayer.OpenMelds.Count);
            var rawExpectedValue = EstimateExpectedValue(result, snapshot.DoraIndicators.Count, localPlayer.OpenMelds.Count, currentShanten.Shanten);
            var riskPenalty = EstimateTileRisk(entry.Tile, snapshot, localPlayer.PlayerIndex, threatLevel);
            var adjustedExpectedValue = Math.Max(0, rawExpectedValue - riskPenalty * defenseWeight);
            var mainUkeire = result.UkeireTiles.Take(3).Select(item => item.Tile).ToArray();
            var note = BuildStrategicNote(entry.Tile, result, threatLevel, riskPenalty, adjustedExpectedValue, rawExpectedValue);

            candidates.Add(new DiscardCandidateEvaluation(
                entry.Tile,
                result.Shanten,
                result.UkeireCount,
                adjustedExpectedValue,
                riskPenalty,
                mainUkeire,
                note));
        }

        return candidates;
    }

    private static double EstimateExpectedValue(ShantenResult result, int doraIndicatorCount, int openMeldCount, int currentShanten)
    {
        var doraBonus = doraIndicatorCount * 100;
        var speedGain = Math.Max(-1, currentShanten - result.Shanten);
        var shapeValue = (4 - Math.Max(0, result.Shanten)) * 700;
        var ukeireValue = result.UkeireCount * 60;
        var opennessPenalty = openMeldCount > 0 ? 250 : 0;
        return Math.Max(100, shapeValue + ukeireValue + doraBonus + speedGain * 500 - opennessPenalty);
    }

    private static double EstimateTileRisk(Tile tile, MahjongTableSnapshot snapshot, int localPlayerIndex, ThreatLevel threatLevel)
    {
        var opponents = snapshot.Players
            .Where(player => player.PlayerIndex != localPlayerIndex)
            .ToArray();
        if (opponents.Length == 0)
        {
            return 0.05;
        }

        var threatOpponents = SelectThreatOpponents(opponents, threatLevel);
        var knownTiles = BuildKnownTiles(snapshot);
        var normalizedTile = Normalize(tile);

        var highestRisk = 0.05;
        foreach (var opponent in threatOpponents)
        {
            var riskVsOpponent = EstimateRiskAgainstOpponent(normalizedTile, opponent, knownTiles);
            if (riskVsOpponent > highestRisk)
            {
                highestRisk = riskVsOpponent;
            }
        }

        return Math.Clamp(highestRisk, 0.02, 1.0);
    }

    private static IReadOnlyList<PlayerSnapshot> SelectThreatOpponents(IReadOnlyList<PlayerSnapshot> opponents, ThreatLevel threatLevel)
    {
        var riichiOpponents = opponents.Where(player => player.IsRiichi).ToArray();
        if (riichiOpponents.Length > 0)
        {
            return riichiOpponents;
        }

        return threatLevel == ThreatLevel.Low
            ? opponents
            : opponents.Where(player => player.OpenMelds.Count > 0).DefaultIfEmpty(opponents[0]).ToArray();
    }

    private static int[] BuildKnownTiles(MahjongTableSnapshot snapshot)
    {
        var knownTiles = snapshot.VisibleTiles
            .Concat(snapshot.Hand)
            .Concat(snapshot.Players.SelectMany(player => player.Discards))
            .Concat(snapshot.Players.SelectMany(player => player.OpenMelds.SelectMany(meld => meld.Tiles)))
            .Select(Normalize)
            .ToArray();
        return TileEncoding.CountTiles(knownTiles);
    }

    private static double EstimateRiskAgainstOpponent(Tile tile, PlayerSnapshot opponent, int[] knownTiles)
    {
        if (IsGenbutsu(tile, opponent.Discards))
        {
            return 0.02;
        }

        var tileIndex = TileEncoding.ToIndex(tile);
        var baseRisk = tile.IsHonor
            ? 0.68
            : tile.IsTerminal
                ? 0.62
                : 0.82;

        if (opponent.IsRiichi)
        {
            baseRisk += 0.12;
        }

        if (opponent.OpenMelds.Count >= 2)
        {
            baseRisk += 0.05;
        }

        if (IsSujiSafe(tile, opponent.Discards))
        {
            baseRisk -= 0.24;
        }

        if (IsKabeSafe(tile, knownTiles))
        {
            baseRisk -= 0.18;
        }

        if (tile.IsHonor && knownTiles[tileIndex] >= 3)
        {
            baseRisk -= 0.35;
        }

        if (knownTiles[tileIndex] >= 3)
        {
            baseRisk -= 0.12;
        }

        return Math.Clamp(baseRisk, 0.05, 1.0);
    }

    private static bool IsGenbutsu(Tile tile, IReadOnlyList<Tile> discards)
    {
        var normalized = Normalize(tile);
        return discards.Select(Normalize).Contains(normalized);
    }

    private static bool IsSujiSafe(Tile tile, IReadOnlyList<Tile> discards)
    {
        if (tile.IsHonor)
        {
            return false;
        }

        var normalizedDiscards = discards.Select(Normalize).ToHashSet();
        var lowerAnchor = tile.Rank - 3;
        var upperAnchor = tile.Rank + 3;

        var hasLowerAnchor = lowerAnchor >= 1 && normalizedDiscards.Contains(new Tile(tile.Suit, lowerAnchor));
        var hasUpperAnchor = upperAnchor <= 9 && normalizedDiscards.Contains(new Tile(tile.Suit, upperAnchor));
        return hasLowerAnchor || hasUpperAnchor;
    }

    private static bool IsKabeSafe(Tile tile, int[] knownTiles)
    {
        if (tile.IsHonor)
        {
            return knownTiles[TileEncoding.ToIndex(tile)] >= 3;
        }

        if (tile.Rank <= 2)
        {
            return KnownCopies(tile.Suit, tile.Rank + 1, knownTiles) >= 4;
        }

        if (tile.Rank >= 8)
        {
            return KnownCopies(tile.Suit, tile.Rank - 1, knownTiles) >= 4;
        }

        var leftWall = KnownCopies(tile.Suit, tile.Rank - 1, knownTiles) >= 4;
        var rightWall = KnownCopies(tile.Suit, tile.Rank + 1, knownTiles) >= 4;
        return leftWall || rightWall;
    }

    private static int KnownCopies(TileSuit suit, int rank, int[] knownTiles)
    {
        if (rank is < 1 or > 9)
        {
            return 0;
        }

        var index = TileEncoding.ToIndex(new Tile(suit, rank));
        return knownTiles[index];
    }

    private static Tile Normalize(Tile tile)
    {
        return tile.IsRed ? tile with { IsRed = false } : tile;
    }

    private static string BuildStrategicNote(
        Tile tile,
        ShantenResult result,
        ThreatLevel threatLevel,
        double riskPenalty,
        double adjustedExpectedValue,
        double rawExpectedValue)
    {
        var riskLabel = riskPenalty switch
        {
            < 0.20 => "very safe",
            < 0.45 => "moderately safe",
            < 0.70 => "risky",
            _ => "very risky",
        };

        var defenseLabel = threatLevel switch
        {
            ThreatLevel.High => "defense-weighted",
            ThreatLevel.Medium => "balanced",
            _ => "offense-weighted",
        };

        return $"{defenseLabel}; tile {tile} is {riskLabel}; shanten {result.Shanten}, ukeire {result.UkeireCount}, EV {adjustedExpectedValue:F0} (raw {rawExpectedValue:F0}).";
    }

    private CallRecommendation RecommendCall(
        MahjongTableSnapshot snapshot,
        ShantenResult shanten,
        ThreatLevel threatLevel,
        bool shouldPush)
    {
        var opportunity = snapshot.PendingCallOpportunity;
        if (opportunity is null)
        {
            return new CallRecommendation(CallType.Chi, false, "No call opportunity is currently active.");
        }

        var localPlayer = snapshot.GetLocalPlayer();
        if (localPlayer is null)
        {
            return new CallRecommendation(opportunity.CallType, false, "Unable to evaluate call without local player context.");
        }

        var isWinningCall = opportunity.CallType is CallType.Ron or CallType.Tsumo;
        var expectedHan = Math.Max(1.0, snapshot.DoraIndicators.Count * 0.5 + (localPlayer.OpenMelds.Count == 0 ? 1.0 : 0.5));
        var shantenDelta = opportunity.CallType switch
        {
            CallType.Pon => -1,
            CallType.Chi => -1,
            CallType.Kan => 0,
            _ => 0,
        };

        var recommendation = recommendationEngine.RecommendCall(new CallRecommendationInput(
            CallType: opportunity.CallType,
            ShantenDelta: shantenDelta,
            ExpectedHanAfterCall: expectedHan,
            MaintainsValueTarget: expectedHan >= 1,
            IsClosedHand: localPlayer.OpenMelds.Count == 0,
            IsTenpai: shanten.Shanten <= 0,
            IsWinningCall: isWinningCall,
            HasClearKanUpside: opportunity.CallType == CallType.Kan && shanten.Shanten <= 1,
            Notes: "Strategic policy evaluation"));

        if (!isWinningCall && (!shouldPush || threatLevel == ThreatLevel.High) && opportunity.CallType is CallType.Pon or CallType.Chi or CallType.Kan)
        {
            return recommendation with
            {
                ShouldCall = false,
                Reason = $"Defensive override: {threatLevel} threat and fold posture make {opportunity.CallType} too risky."
            };
        }

        return recommendation;
    }
}
