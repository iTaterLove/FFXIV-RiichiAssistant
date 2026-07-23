using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Decision;

public record DiscardCandidateEvaluation(
    Tile Tile,
    int ResultingShanten,
    int UkeireCount,
    double ExpectedValue,
    double RiskPenalty,
    IReadOnlyList<Tile> MainUkeireTiles,
    string? StrategicNote = null);

public sealed record DiscardRecommendation(
    Tile Tile,
    int ResultingShanten,
    int UkeireCount,
    double ExpectedValue,
    string Reason,
    IReadOnlyList<Tile>? MainUkeireTiles = null,
    RiichiDamaComparison? RiichiDama = null);

public sealed record RiichiDamaComparison(
    double DamaExpectedValue,
    double RiichiExpectedValue,
    bool PreferRiichi,
    string Reason);

public sealed record CallRecommendationInput(
    CallType CallType,
    int ShantenDelta,
    double ExpectedHanAfterCall,
    bool MaintainsValueTarget,
    bool IsClosedHand,
    bool IsTenpai,
    bool IsWinningCall,
    bool HasClearKanUpside = false,
    double? DamaExpectedValue = null,
    double? RiichiExpectedValue = null,
    string? Notes = null);

public sealed record CallRecommendation(
    CallType CallType,
    bool ShouldCall,
    string Reason);

public interface IRecommendationEngine
{
    IReadOnlyList<DiscardRecommendation> GetTopDiscards(MahjongTableSnapshot snapshot, int maxCount = 3);

    IReadOnlyList<DiscardRecommendation> GetTopDiscards(IEnumerable<DiscardCandidateEvaluation> candidates, int maxCount = 3);

    CallRecommendation RecommendCall(CallRecommendationInput input);
}

public sealed class RecommendationEngine : IRecommendationEngine
{
    private readonly IShantenSolver shantenSolver;
    private readonly IHandScoringEngine handScoringEngine;

    public RecommendationEngine()
        : this(new ShantenSolver(), new HandScoringEngine())
    {
    }

    public RecommendationEngine(IShantenSolver shantenSolver, IHandScoringEngine handScoringEngine)
    {
        this.shantenSolver = shantenSolver;
        this.handScoringEngine = handScoringEngine;
    }

    public IReadOnlyList<DiscardRecommendation> GetTopDiscards(MahjongTableSnapshot snapshot, int maxCount = 3)
    {
        var localPlayer = snapshot.GetLocalPlayer();
        if (!snapshot.IsValidForRecommendations || localPlayer is null || snapshot.Hand.Count != 14)
        {
            return Array.Empty<DiscardRecommendation>();
        }

        return GetTopDiscards(BuildDiscardCandidates(snapshot, localPlayer), maxCount);
    }

    public IReadOnlyList<DiscardRecommendation> GetTopDiscards(IEnumerable<DiscardCandidateEvaluation> candidates, int maxCount = 3)
    {
        return candidates
            .OrderBy(candidate => candidate.ResultingShanten)
            .ThenByDescending(candidate => candidate.UkeireCount)
            .ThenByDescending(candidate => candidate.ExpectedValue)
            .ThenBy(candidate => candidate.RiskPenalty)
            .Take(maxCount)
            .Select(candidate => new DiscardRecommendation(
                candidate.Tile,
                candidate.ResultingShanten,
                candidate.UkeireCount,
                candidate.ExpectedValue,
                BuildDiscardReason(candidate),
                candidate.MainUkeireTiles,
                candidate is SnapshotDiscardCandidateEvaluation snapshotCandidate ? snapshotCandidate.RiichiDama : null))
            .ToArray();
    }

    public CallRecommendation RecommendCall(CallRecommendationInput input)
    {
        if (input.CallType is CallType.Ron or CallType.Tsumo || input.IsWinningCall)
        {
            return new CallRecommendation(input.CallType, true, "Take the win now; value preview should be surfaced immediately.");
        }

        return input.CallType switch
        {
            CallType.Riichi => RecommendRiichi(input),
            CallType.Kan => RecommendKan(input),
            CallType.Pon or CallType.Chi => RecommendSpeedCall(input),
            _ => new CallRecommendation(input.CallType, false, input.Notes ?? "No recommendation available."),
        };
    }

    private static CallRecommendation RecommendRiichi(CallRecommendationInput input)
    {
        var explicitEdge = input.RiichiExpectedValue.GetValueOrDefault() - input.DamaExpectedValue.GetValueOrDefault();
        var shouldRiichi = input.IsTenpai && input.IsClosedHand &&
            ((input.RiichiExpectedValue.HasValue && explicitEdge > 0) ||
             (!input.RiichiExpectedValue.HasValue && (input.ExpectedHanAfterCall >= 1 || input.MaintainsValueTarget)));
        var reason = shouldRiichi
            ? input.RiichiExpectedValue.HasValue
                ? $"Riichi is favored because the closed tenpai hand gains about {explicitEdge:F0} expected points over dama."
                : "Riichi is favored because the hand is closed, tenpai, and meets the current value target."
            : input.RiichiExpectedValue.HasValue
                ? "Hold dama for now because riichi does not currently improve the expected value line."
                : "Hold dama for now because riichi does not currently improve the practical value tradeoff.";
        return new CallRecommendation(CallType.Riichi, shouldRiichi, reason);
    }

    private static CallRecommendation RecommendKan(CallRecommendationInput input)
    {
        var shouldKan = input.HasClearKanUpside && input.MaintainsValueTarget;
        var reason = shouldKan
            ? "Kan has clear upside here and does not undermine the current value target."
            : "Skip kan for now; the MVP policy stays conservative without a clear upside.";
        return new CallRecommendation(CallType.Kan, shouldKan, reason);
    }

    private static CallRecommendation RecommendSpeedCall(CallRecommendationInput input)
    {
        var shouldCall = input.ShantenDelta < 0 && input.MaintainsValueTarget;
        var reason = shouldCall
            ? "Take the call because it improves speed while preserving the value line."
            : "Skip the call because the speed gain is not strong enough to justify the value loss.";
        return new CallRecommendation(input.CallType, shouldCall, reason);
    }

    private static string BuildDiscardReason(DiscardCandidateEvaluation candidate)
    {
        var ukeireLabel = candidate.MainUkeireTiles.Count == 0
            ? "no primary ukeire groups identified yet"
            : $"ukeire led by {string.Join(", ", candidate.MainUkeireTiles.Select(tile => tile.ToString()))}";

        var rationale = candidate.StrategicNote ?? "best current balance of speed and value";
        return $"Keeps shanten at {candidate.ResultingShanten}, with ukeire {candidate.UkeireCount}; {ukeireLabel}; {rationale}.";
    }

    private IReadOnlyList<DiscardCandidateEvaluation> BuildDiscardCandidates(MahjongTableSnapshot snapshot, PlayerSnapshot localPlayer)
    {
        var candidates = new List<DiscardCandidateEvaluation>();
        var uniqueTiles = snapshot.Hand
            .Select((tile, index) => new { tile, index })
            .GroupBy(entry => entry.tile)
            .Select(group => group.First())
            .OrderBy(entry => TileEncoding.ToIndex(entry.tile))
            .ToArray();

        foreach (var entry in uniqueTiles)
        {
            var remainingHand = snapshot.Hand.Where((_, index) => index != entry.index).ToArray();
            var shanten = shantenSolver.Analyze(remainingHand, snapshot.VisibleTiles, localPlayer.OpenMelds.Count);
            var riichiDama = localPlayer.OpenMelds.Count == 0 && shanten.IsTenpai
                ? BuildRiichiDamaComparison(snapshot, localPlayer, remainingHand, shanten)
                : null;
            var expectedValue = EstimateExpectedValue(snapshot, localPlayer, remainingHand, shanten, riichiDama);
            var mainUkeire = shanten.UkeireTiles.Take(3).Select(tile => tile.Tile).ToArray();

            candidates.Add(new SnapshotDiscardCandidateEvaluation(
                entry.tile,
                shanten.Shanten,
                shanten.UkeireCount,
                expectedValue,
                0,
                mainUkeire,
                BuildStrategicNote(entry.tile, shanten, riichiDama),
                riichiDama));
        }

        return candidates;
    }

    private double EstimateExpectedValue(
        MahjongTableSnapshot snapshot,
        PlayerSnapshot localPlayer,
        IReadOnlyList<Tile> thirteenTileHand,
        ShantenResult shanten,
        RiichiDamaComparison? riichiDama)
    {
        if (shanten.UkeireTiles.Count == 0)
        {
            return shanten.Shanten switch
            {
                < 0 => 8000,
                0 => 1500 + CountDoraBonus(snapshot, localPlayer),
                _ => Math.Max(0, (4 - shanten.Shanten) * 250 + CountDoraBonus(snapshot, localPlayer)),
            };
        }

        double total = 0;
        foreach (var ukeire in shanten.UkeireTiles)
        {
            var simulatedHand = thirteenTileHand.Concat([ukeire.Tile]).ToArray();
            var winningContext = new HandScoringContext(
                snapshot.RoundWind,
                snapshot.SeatWind,
                WinType.Ron,
                false,
                ukeire.Tile,
                snapshot.DoraIndicators);
            var score = handScoringEngine.Evaluate(simulatedHand, localPlayer.OpenMelds, winningContext);

            if (score.IsWinningHand && score.Han > 0)
            {
                var weighted = score.Points.RonPoints;
                if (localPlayer.OpenMelds.Count == 0)
                {
                    var comparison = handScoringEngine.EvaluateRiichiAndDama(simulatedHand, localPlayer.OpenMelds, winningContext);
                    weighted = Math.Max(weighted, comparison.PreferRiichi ? comparison.Riichi.Points.RonPoints : comparison.Dama.Points.RonPoints);
                }

                total += weighted * ukeire.RemainingCopies;
                continue;
            }

            total += EstimateIntermediateValue(shanten.Shanten, ukeire.RemainingCopies, riichiDama);
        }

        return total / shanten.UkeireTiles.Sum(entry => entry.RemainingCopies);
    }

    private static double EstimateIntermediateValue(int shanten, int remainingCopies, RiichiDamaComparison? riichiDama)
    {
        double baseValue = shanten switch
        {
            <= 0 => 1800,
            1 => 900,
            2 => 500,
            _ => 250,
        };

        if (riichiDama is not null)
        {
            baseValue += Math.Max(riichiDama.DamaExpectedValue, riichiDama.RiichiExpectedValue) / 10.0;
        }

        return baseValue * remainingCopies;
    }

    private RiichiDamaComparison BuildRiichiDamaComparison(
        MahjongTableSnapshot snapshot,
        PlayerSnapshot localPlayer,
        IReadOnlyList<Tile> thirteenTileHand,
        ShantenResult shanten)
    {
        var weightedDama = 0.0;
        var weightedRiichi = 0.0;

        foreach (var ukeire in shanten.UkeireTiles)
        {
            var winningHand = thirteenTileHand.Concat([ukeire.Tile]).ToArray();
            var context = new HandScoringContext(snapshot.RoundWind, snapshot.SeatWind, WinType.Ron, false, ukeire.Tile, snapshot.DoraIndicators);
            var comparison = handScoringEngine.EvaluateRiichiAndDama(winningHand, localPlayer.OpenMelds, context);
            weightedDama += comparison.Dama.Points.RonPoints * ukeire.RemainingCopies;
            weightedRiichi += comparison.Riichi.Points.RonPoints * ukeire.RemainingCopies;
        }

        var totalCopies = Math.Max(1, shanten.UkeireTiles.Sum(entry => entry.RemainingCopies));
        weightedDama /= totalCopies;
        weightedRiichi /= totalCopies;
        var preferRiichi = weightedRiichi > weightedDama;
        return new RiichiDamaComparison(
            weightedDama,
            weightedRiichi,
            preferRiichi,
            preferRiichi
                ? $"riichi preview {weightedRiichi:F0} beats dama {weightedDama:F0}"
                : $"dama preview {weightedDama:F0} holds over riichi {weightedRiichi:F0}");
    }

    private static string BuildStrategicNote(Tile discardedTile, ShantenResult shanten, RiichiDamaComparison? riichiDama)
    {
        if (riichiDama is not null)
        {
            return riichiDama.PreferRiichi
                ? $"discarding {discardedTile} keeps a strong riichi line"
                : $"discarding {discardedTile} preserves dama value while keeping speed";
        }

        return shanten.Shanten switch
        {
            < 0 => "already complete after the simulated discard line",
            0 => "reaches tenpai with the broadest current acceptance",
            1 => "keeps the hand closest to tenpai with solid ukeire",
            _ => "improves shape while keeping future value paths open",
        };
    }

    private static double CountDoraBonus(MahjongTableSnapshot snapshot, PlayerSnapshot localPlayer)
    {
        var ownedTiles = snapshot.Hand.Concat(localPlayer.OpenMelds.SelectMany(meld => meld.Tiles));
        return snapshot.DoraIndicators.Count * 150 + ownedTiles.Count(tile => tile.IsRed) * 200;
    }

    private sealed record SnapshotDiscardCandidateEvaluation(
        Tile Tile,
        int ResultingShanten,
        int UkeireCount,
        double ExpectedValue,
        double RiskPenalty,
        IReadOnlyList<Tile> MainUkeireTiles,
        string? StrategicNote,
        RiichiDamaComparison? RiichiDama)
        : DiscardCandidateEvaluation(Tile, ResultingShanten, UkeireCount, ExpectedValue, RiskPenalty, MainUkeireTiles, StrategicNote);
}