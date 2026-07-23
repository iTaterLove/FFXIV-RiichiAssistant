using FFXIV.RiichiAssistant.Core;

namespace FFXIV.RiichiAssistant.Riichi;

public enum WinType
{
    Ron,
    Tsumo,
}

public enum Yaku
{
    Riichi,
    MenzenTsumo,
    Tanyao,
    Pinfu,
    Yakuhai,
    Iipeiko,
    Toitoi,
    SanshokuDoujun,
    Ittsu,
    Chanta,
    Junchan,
    Honitsu,
    Chinitsu,
    Chiitoitsu,
    KokushiMusou,
    Dora,
    AkaDora,
}

public enum WaitType
{
    Unknown,
    Ryanmen,
    Kanchan,
    Penchan,
    Tanki,
    Shanpon,
}

public sealed record HandScoringContext(
    Wind RoundWind,
    Wind SeatWind,
    WinType WinType,
    bool IsRiichi,
    Tile WinningTile,
    IReadOnlyList<Tile> DoraIndicators);

public sealed record YakuValue(Yaku Yaku, int Han, string Reason);

public sealed record HandPointBreakdown(
    int RonPoints,
    int DealerTsumoPayment,
    int NonDealerTsumoPayment);

public sealed record HandScoreResult(
    bool IsWinningHand,
    int Han,
    int Fu,
    int DoraCount,
    int AkaDoraCount,
    WaitType WaitType,
    IReadOnlyList<YakuValue> YakuValues,
    HandPointBreakdown Points,
    IReadOnlyList<string> Warnings);

public sealed record RiichiDamaEvaluation(
    HandScoreResult Dama,
    HandScoreResult Riichi,
    bool PreferRiichi,
    string Reason);

public interface IHandScoringEngine
{
    HandScoreResult Evaluate(
        IReadOnlyList<Tile> concealedTiles,
        IReadOnlyList<Meld> openMelds,
        HandScoringContext context);

    RiichiDamaEvaluation EvaluateRiichiAndDama(
        IReadOnlyList<Tile> concealedTiles,
        IReadOnlyList<Meld> openMelds,
        HandScoringContext baseContext);
}

public sealed class HandScoringEngine : IHandScoringEngine
{
    public HandScoreResult Evaluate(
        IReadOnlyList<Tile> concealedTiles,
        IReadOnlyList<Meld> openMelds,
        HandScoringContext context)
    {
        var warnings = new List<string>();
        var closedHand = openMelds.Count == 0;
        var allTiles = concealedTiles.Concat(openMelds.SelectMany(meld => meld.Tiles)).ToArray();
        var doraCount = CountDora(context.DoraIndicators, allTiles);
        var akaDoraCount = allTiles.Count(tile => tile.IsRed);

        if (IsKokushi(concealedTiles))
        {
            var yakuman = new[] { new YakuValue(Yaku.KokushiMusou, 13, "Thirteen terminals and honors with a pair.") };
            var yakumanPoints = BuildPoints(context.SeatWind == Wind.East, 13, 0, true);
            return new HandScoreResult(true, 13, 0, doraCount, akaDoraCount, WaitType.Unknown, yakuman, yakumanPoints, warnings);
        }

        if (IsChiitoitsu(concealedTiles) && closedHand)
        {
            var chiitoitsuYaku = new List<YakuValue> { new(Yaku.Chiitoitsu, 2, "Seven distinct pairs.") };
            AddSharedYaku(chiitoitsuYaku, context, closedHand, doraCount, akaDoraCount);
            var chiitoitsuHan = chiitoitsuYaku.Sum(value => value.Han);
            var chiitoitsuPoints = BuildPoints(context.SeatWind == Wind.East, chiitoitsuHan, 25, false);
            return new HandScoreResult(true, chiitoitsuHan, 25, doraCount, akaDoraCount, WaitType.Tanki, chiitoitsuYaku, chiitoitsuPoints, warnings);
        }

        if (!StandardHandShape.TryCompose(concealedTiles, openMelds, context.WinningTile, out var composition))
        {
            warnings.Add("Standard hand decomposition is not available for this tile set yet.");
            return new HandScoreResult(false, 0, 0, doraCount, akaDoraCount, WaitType.Unknown, Array.Empty<YakuValue>(), new HandPointBreakdown(0, 0, 0), warnings);
        }

        var yaku = new List<YakuValue>();
        AddSharedYaku(yaku, context, closedHand, doraCount, akaDoraCount);

        if (IsTanyao(allTiles))
        {
            yaku.Add(new YakuValue(Yaku.Tanyao, 1, "All tiles are simples."));
        }

        AddRepeatedYakuhai(yaku, composition, context);

        if (IsPinfu(composition, context, closedHand))
        {
            yaku.Add(new YakuValue(Yaku.Pinfu, 1, "Closed hand with only sequences, non-value pair, and ryanmen wait."));
        }

        if (IsIipeiko(composition, closedHand))
        {
            yaku.Add(new YakuValue(Yaku.Iipeiko, 1, "One pair of identical sequences in a closed hand."));
        }

        if (IsToitoi(composition))
        {
            yaku.Add(new YakuValue(Yaku.Toitoi, 2, "All groups are triplets or kans."));
        }

        if (TryGetSanshokuDoujunHan(composition, closedHand, out var sanshokuHan))
        {
            yaku.Add(new YakuValue(Yaku.SanshokuDoujun, sanshokuHan, "Same sequence across all three suits."));
        }

        if (TryGetIttsuHan(composition, closedHand, out var ittsuHan))
        {
            yaku.Add(new YakuValue(Yaku.Ittsu, ittsuHan, "123, 456, and 789 in the same suit."));
        }

        if (TryGetChantaFamily(composition, closedHand, out var familyYaku, out var familyHan))
        {
            yaku.Add(new YakuValue(familyYaku, familyHan, familyYaku == Yaku.Junchan
                ? "Every set contains a terminal and the hand has no honors."
                : "Every set contains a terminal or honor."));
        }

        if (TryGetFlushHan(allTiles, closedHand, out var flushYaku, out var flushHan))
        {
            yaku.Add(new YakuValue(flushYaku, flushHan, flushYaku == Yaku.Chinitsu
                ? "All tiles are from one suit only."
                : "All tiles are from one suit plus honors."));
        }

        var fu = CalculateFu(composition, context, closedHand, yaku.Any(value => value.Yaku == Yaku.Pinfu));
        var han = yaku.Sum(value => value.Han);
        var points = BuildPoints(context.SeatWind == Wind.East, han, fu, false);
        return new HandScoreResult(true, han, fu, doraCount, akaDoraCount, composition.WaitType, yaku, points, warnings);
    }

    public RiichiDamaEvaluation EvaluateRiichiAndDama(
        IReadOnlyList<Tile> concealedTiles,
        IReadOnlyList<Meld> openMelds,
        HandScoringContext baseContext)
    {
        var dama = Evaluate(concealedTiles, openMelds, baseContext with { IsRiichi = false });
        var riichi = openMelds.Count == 0
            ? Evaluate(concealedTiles, openMelds, baseContext with { IsRiichi = true })
            : dama;
        var damaValue = EstimateTotalPoints(dama, baseContext.WinType);
        var riichiValue = EstimateTotalPoints(riichi, baseContext.WinType);
        var preferRiichi = openMelds.Count == 0 && riichiValue > damaValue;
        return new RiichiDamaEvaluation(
            dama,
            riichi,
            preferRiichi,
            preferRiichi
                ? $"Riichi projects {riichiValue} points versus dama {damaValue}."
                : $"Dama holds at {damaValue} points versus riichi {riichiValue}.");
    }

    private static int EstimateTotalPoints(HandScoreResult result, WinType winType)
    {
        return winType == WinType.Tsumo
            ? result.Points.DealerTsumoPayment + (result.Points.NonDealerTsumoPayment * 2)
            : result.Points.RonPoints;
    }

    private static void AddSharedYaku(List<YakuValue> yaku, HandScoringContext context, bool closedHand, int doraCount, int akaDoraCount)
    {
        if (context.IsRiichi && closedHand)
        {
            yaku.Add(new YakuValue(Yaku.Riichi, 1, "Closed tenpai declaration."));
        }

        if (context.WinType == WinType.Tsumo && closedHand)
        {
            yaku.Add(new YakuValue(Yaku.MenzenTsumo, 1, "Closed self-draw win."));
        }

        if (doraCount > 0)
        {
            yaku.Add(new YakuValue(Yaku.Dora, doraCount, $"{doraCount} dora from indicators."));
        }

        if (akaDoraCount > 0)
        {
            yaku.Add(new YakuValue(Yaku.AkaDora, akaDoraCount, $"{akaDoraCount} red dora."));
        }
    }

    private static void AddRepeatedYakuhai(List<YakuValue> yaku, StandardHandShape composition, HandScoringContext context)
    {
        var yakuhaiCount = CountYakuhai(composition, context);
        for (var index = 0; index < yakuhaiCount; index++)
        {
            yaku.Add(new YakuValue(Yaku.Yakuhai, 1, "Value honor triplet or kan."));
        }
    }

    private static bool IsTanyao(IEnumerable<Tile> allTiles)
    {
        return allTiles.All(tile => !tile.IsHonor && !tile.IsTerminal);
    }

    private static int CountYakuhai(StandardHandShape composition, HandScoringContext context)
    {
        return composition.AllMelds.Count(meld =>
        {
            if (meld.Type == MeldType.Sequence)
            {
                return false;
            }

            var tile = Normalize(meld.Tiles[0]);
            if (tile.Suit != TileSuit.Honor)
            {
                return false;
            }

            return tile.Rank is 5 or 6 or 7 ||
                   tile.Rank == WindToHonorRank(context.RoundWind) ||
                   tile.Rank == WindToHonorRank(context.SeatWind);
        });
    }

    private static bool IsPinfu(StandardHandShape composition, HandScoringContext context, bool closedHand)
    {
        if (!closedHand || composition.AllMelds.Any(meld => meld.Type != MeldType.Sequence))
        {
            return false;
        }

        return composition.WaitType == WaitType.Ryanmen && GetPairValueFu(composition.Pair, context) == 0;
    }

    private static bool IsIipeiko(StandardHandShape composition, bool closedHand)
    {
        if (!closedHand)
        {
            return false;
        }

        return composition.ConcealedMelds
            .Where(meld => meld.Type == MeldType.Sequence)
            .GroupBy(meld => string.Join('-', meld.Tiles.Select(tile => Normalize(tile).ToString())))
            .Any(group => group.Count() >= 2);
    }

    private static bool IsToitoi(StandardHandShape composition)
    {
        return composition.AllMelds.All(meld => meld.Type is MeldType.Triplet or MeldType.Kan);
    }

    private static bool TryGetSanshokuDoujunHan(StandardHandShape composition, bool closedHand, out int han)
    {
        var sequences = composition.AllMelds
            .Where(meld => meld.Type == MeldType.Sequence)
            .Select(meld => meld.Tiles.OrderBy(tile => tile.Rank).ToArray())
            .ToArray();

        foreach (var start in Enumerable.Range(1, 7))
        {
            var suits = sequences
                .Where(tiles => tiles[0].Rank == start)
                .Select(tiles => Normalize(tiles[0]).Suit)
                .Distinct()
                .ToArray();
            if (suits.Contains(TileSuit.Man) && suits.Contains(TileSuit.Pin) && suits.Contains(TileSuit.Sou))
            {
                han = closedHand ? 2 : 1;
                return true;
            }
        }

        han = 0;
        return false;
    }

    private static bool TryGetIttsuHan(StandardHandShape composition, bool closedHand, out int han)
    {
        var sequences = composition.AllMelds.Where(meld => meld.Type == MeldType.Sequence).ToArray();
        foreach (var suit in new[] { TileSuit.Man, TileSuit.Pin, TileSuit.Sou })
        {
            var starts = sequences
                .Where(meld => Normalize(meld.Tiles[0]).Suit == suit)
                .Select(meld => meld.Tiles.Min(tile => tile.Rank))
                .ToHashSet();
            if (starts.Contains(1) && starts.Contains(4) && starts.Contains(7))
            {
                han = closedHand ? 2 : 1;
                return true;
            }
        }

        han = 0;
        return false;
    }

    private static bool TryGetChantaFamily(StandardHandShape composition, bool closedHand, out Yaku yaku, out int han)
    {
        var pair = Normalize(composition.Pair);
        var allContainTerminalOrHonor = composition.AllMelds.All(MeldContainsTerminalOrHonor) && (pair.IsHonor || pair.IsTerminal);
        var allContainTerminalOnly = composition.AllMelds.All(MeldContainsTerminalOnly) && pair.IsTerminal && !pair.IsHonor;

        if (allContainTerminalOnly)
        {
            yaku = Yaku.Junchan;
            han = closedHand ? 3 : 2;
            return true;
        }

        if (allContainTerminalOrHonor)
        {
            yaku = Yaku.Chanta;
            han = closedHand ? 2 : 1;
            return true;
        }

        yaku = default;
        han = 0;
        return false;
    }

    private static bool MeldContainsTerminalOrHonor(Meld meld)
    {
        return meld.Tiles.Any(tile => Normalize(tile).IsHonor || Normalize(tile).IsTerminal);
    }

    private static bool MeldContainsTerminalOnly(Meld meld)
    {
        return meld.Tiles.Any(tile => Normalize(tile).IsTerminal) && meld.Tiles.All(tile => !Normalize(tile).IsHonor);
    }

    private static bool TryGetFlushHan(IReadOnlyList<Tile> allTiles, bool closedHand, out Yaku yaku, out int han)
    {
        var suits = allTiles.Select(Normalize).Where(tile => !tile.IsHonor).Select(tile => tile.Suit).Distinct().ToArray();
        var hasHonors = allTiles.Any(tile => Normalize(tile).IsHonor);
        if (suits.Length != 1)
        {
            yaku = default;
            han = 0;
            return false;
        }

        if (hasHonors)
        {
            yaku = Yaku.Honitsu;
            han = closedHand ? 3 : 2;
            return true;
        }

        yaku = Yaku.Chinitsu;
        han = closedHand ? 6 : 5;
        return true;
    }

    private static int CalculateFu(StandardHandShape composition, HandScoringContext context, bool closedHand, bool isPinfu)
    {
        if (isPinfu)
        {
            return context.WinType == WinType.Tsumo ? 20 : 30;
        }

        var fu = 20;
        if (context.WinType == WinType.Ron && closedHand)
        {
            fu += 10;
        }

        if (context.WinType == WinType.Tsumo)
        {
            fu += 2;
        }

        fu += GetPairValueFu(composition.Pair, context);
        fu += composition.AllMelds.Sum(GetMeldFu);
        fu += GetWaitFu(composition.WaitType);

        if (!closedHand && fu == 20)
        {
            fu = 30;
        }

        return RoundUpToNearestTen(fu);
    }

    private static int GetWaitFu(WaitType waitType)
    {
        return waitType is WaitType.Kanchan or WaitType.Penchan or WaitType.Tanki ? 2 : 0;
    }

    private static int GetPairValueFu(Tile pairTile, HandScoringContext context)
    {
        var normalized = Normalize(pairTile);
        if (normalized.Suit != TileSuit.Honor)
        {
            return 0;
        }

        var fu = normalized.Rank is 5 or 6 or 7 ? 2 : 0;
        if (normalized.Rank == WindToHonorRank(context.RoundWind))
        {
            fu += 2;
        }

        if (normalized.Rank == WindToHonorRank(context.SeatWind))
        {
            fu += 2;
        }

        return fu;
    }

    private static int GetMeldFu(Meld meld)
    {
        if (meld.Type == MeldType.Sequence)
        {
            return 0;
        }

        var tile = Normalize(meld.Tiles[0]);
        var terminalOrHonor = tile.IsHonor || tile.IsTerminal;

        return meld.Type switch
        {
            MeldType.Triplet when meld.IsOpen && terminalOrHonor => 4,
            MeldType.Triplet when meld.IsOpen => 2,
            MeldType.Triplet when terminalOrHonor => 8,
            MeldType.Triplet => 4,
            MeldType.Kan when meld.IsOpen && terminalOrHonor => 16,
            MeldType.Kan when meld.IsOpen => 8,
            MeldType.Kan when terminalOrHonor => 32,
            MeldType.Kan => 16,
            _ => 0,
        };
    }

    private static bool IsChiitoitsu(IReadOnlyList<Tile> concealedTiles)
    {
        if (concealedTiles.Count != 14)
        {
            return false;
        }

        var counts = TileEncoding.CountTiles(concealedTiles);
        return counts.Count(count => count == 2) == 7;
    }

    private static bool IsKokushi(IReadOnlyList<Tile> concealedTiles)
    {
        if (concealedTiles.Count != 14)
        {
            return false;
        }

        var counts = TileEncoding.CountTiles(concealedTiles);
        var distinctCount = 0;
        var hasPair = false;

        foreach (var index in TileEncoding.TerminalAndHonorIndices)
        {
            if (counts[index] > 0)
            {
                distinctCount++;
            }

            if (counts[index] >= 2)
            {
                hasPair = true;
            }
        }

        return distinctCount == 13 && hasPair;
    }

    private static int CountDora(IEnumerable<Tile> indicators, IEnumerable<Tile> tiles)
    {
        var doraTiles = indicators.Select(GetDoraFromIndicator).ToArray();
        return tiles.Count(tile => doraTiles.Contains(Normalize(tile)));
    }

    private static Tile Normalize(Tile tile)
    {
        return tile.IsRed ? tile with { IsRed = false } : tile;
    }

    private static Tile GetDoraFromIndicator(Tile indicator)
    {
        if (indicator.Suit == TileSuit.Honor)
        {
            return indicator.Rank switch
            {
                1 => new Tile(TileSuit.Honor, 2),
                2 => new Tile(TileSuit.Honor, 3),
                3 => new Tile(TileSuit.Honor, 4),
                4 => new Tile(TileSuit.Honor, 1),
                5 => new Tile(TileSuit.Honor, 6),
                6 => new Tile(TileSuit.Honor, 7),
                7 => new Tile(TileSuit.Honor, 5),
                _ => Normalize(indicator),
            };
        }

        return new Tile(indicator.Suit, indicator.Rank == 9 ? 1 : indicator.Rank + 1);
    }

    private static int WindToHonorRank(Wind wind)
    {
        return wind switch
        {
            Wind.East => 1,
            Wind.South => 2,
            Wind.West => 3,
            Wind.North => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(wind)),
        };
    }

    private static HandPointBreakdown BuildPoints(bool isDealer, int han, int fu, bool isYakuman)
    {
        var basicPoints = isYakuman ? 8000 : CalculateBasicPoints(han, fu);
        if (basicPoints == 0)
        {
            return new HandPointBreakdown(0, 0, 0);
        }

        if (isDealer)
        {
            return new HandPointBreakdown(
                RoundUpToNearestHundred(basicPoints * 6),
                RoundUpToNearestHundred(basicPoints * 2),
                RoundUpToNearestHundred(basicPoints * 2));
        }

        return new HandPointBreakdown(
            RoundUpToNearestHundred(basicPoints * 4),
            RoundUpToNearestHundred(basicPoints * 2),
            RoundUpToNearestHundred(basicPoints));
    }

    private static int CalculateBasicPoints(int han, int fu)
    {
        if (han <= 0)
        {
            return 0;
        }

        if (han >= 13)
        {
            return 8000;
        }

        if (han >= 11)
        {
            return 6000;
        }

        if (han >= 8)
        {
            return 4000;
        }

        if (han >= 6)
        {
            return 3000;
        }

        if (han >= 5 || (han == 4 && fu >= 40) || (han == 3 && fu >= 70))
        {
            return 2000;
        }

        return Math.Min(fu * (1 << (han + 2)), 2000);
    }

    private static int RoundUpToNearestTen(int value)
    {
        return (int)(Math.Ceiling(value / 10.0) * 10);
    }

    private static int RoundUpToNearestHundred(int value)
    {
        return (int)(Math.Ceiling(value / 100.0) * 100);
    }
}

internal sealed record StandardHandShape(
    Tile Pair,
    WaitType WaitType,
    IReadOnlyList<Meld> ConcealedMelds,
    IReadOnlyList<Meld> OpenMelds)
{
    public IReadOnlyList<Meld> AllMelds => ConcealedMelds.Concat(OpenMelds).ToArray();

    public static bool TryCompose(IReadOnlyList<Tile> concealedTiles, IReadOnlyList<Meld> openMelds, Tile winningTile, out StandardHandShape composition)
    {
        var counts = TileEncoding.CountTiles(concealedTiles);
        for (var pairIndex = 0; pairIndex < TileEncoding.TileTypeCount; pairIndex++)
        {
            if (counts[pairIndex] < 2)
            {
                continue;
            }

            counts[pairIndex] -= 2;
            var concealedMelds = new List<Meld>();
            if (TryExtractMelds(counts, concealedMelds))
            {
                var pairTile = TileEncoding.FromIndex(pairIndex);
                composition = new StandardHandShape(
                    pairTile,
                    DetermineWaitType(pairTile, concealedMelds, winningTile),
                    concealedMelds.ToArray(),
                    openMelds.ToArray());
                counts[pairIndex] += 2;
                return true;
            }

            counts[pairIndex] += 2;
        }

        composition = new StandardHandShape(default, WaitType.Unknown, Array.Empty<Meld>(), Array.Empty<Meld>());
        return false;
    }

    private static WaitType DetermineWaitType(Tile pairTile, IReadOnlyList<Meld> concealedMelds, Tile winningTile)
    {
        var normalizedWinningTile = winningTile.IsRed ? winningTile with { IsRed = false } : winningTile;
        var normalizedPairTile = pairTile.IsRed ? pairTile with { IsRed = false } : pairTile;
        if (normalizedPairTile == normalizedWinningTile)
        {
            return WaitType.Tanki;
        }

        foreach (var meld in concealedMelds)
        {
            var tiles = meld.Tiles.Select(tile => tile.IsRed ? tile with { IsRed = false } : tile).OrderBy(tile => tile.Rank).ToArray();
            if (!tiles.Contains(normalizedWinningTile))
            {
                continue;
            }

            if (meld.Type == MeldType.Triplet)
            {
                return WaitType.Shanpon;
            }

            if (meld.Type == MeldType.Sequence)
            {
                if (tiles[1].Rank == normalizedWinningTile.Rank)
                {
                    return WaitType.Kanchan;
                }

                if (tiles[0].Rank == 1 && normalizedWinningTile.Rank == 3)
                {
                    return WaitType.Penchan;
                }

                if (tiles[2].Rank == 9 && normalizedWinningTile.Rank == 7)
                {
                    return WaitType.Penchan;
                }

                return WaitType.Ryanmen;
            }
        }

        return WaitType.Unknown;
    }

    private static bool TryExtractMelds(int[] counts, List<Meld> melds)
    {
        var tileIndex = Array.FindIndex(counts, count => count > 0);
        if (tileIndex < 0)
        {
            return true;
        }

        if (counts[tileIndex] >= 3)
        {
            counts[tileIndex] -= 3;
            melds.Add(new Meld(MeldType.Triplet, [TileEncoding.FromIndex(tileIndex), TileEncoding.FromIndex(tileIndex), TileEncoding.FromIndex(tileIndex)], false));
            if (TryExtractMelds(counts, melds))
            {
                return true;
            }

            melds.RemoveAt(melds.Count - 1);
            counts[tileIndex] += 3;
        }

        if (TileEncoding.CanMakeSequence(tileIndex, counts))
        {
            counts[tileIndex]--;
            counts[tileIndex + 1]--;
            counts[tileIndex + 2]--;
            melds.Add(new Meld(MeldType.Sequence, [TileEncoding.FromIndex(tileIndex), TileEncoding.FromIndex(tileIndex + 1), TileEncoding.FromIndex(tileIndex + 2)], false));
            if (TryExtractMelds(counts, melds))
            {
                return true;
            }

            melds.RemoveAt(melds.Count - 1);
            counts[tileIndex]++;
            counts[tileIndex + 1]++;
            counts[tileIndex + 2]++;
        }

        return false;
    }
}