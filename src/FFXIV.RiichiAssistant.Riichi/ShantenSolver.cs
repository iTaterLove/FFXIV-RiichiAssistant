using FFXIV.RiichiAssistant.Core;

namespace FFXIV.RiichiAssistant.Riichi;

public sealed record UkeireEntry(Tile Tile, int RemainingCopies);

public sealed record ShantenResult(int Shanten, IReadOnlyList<UkeireEntry> UkeireTiles)
{
    public bool IsTenpai => Shanten == 0;

    public bool IsAgari => Shanten < 0;

    public int UkeireCount => UkeireTiles.Sum(entry => entry.RemainingCopies);
}

public interface IShantenSolver
{
    ShantenResult Analyze(IReadOnlyList<Tile> hand, IReadOnlyList<Tile>? visibleTiles = null, int openMeldCount = 0);
}

public sealed class ShantenSolver : IShantenSolver
{
    public ShantenResult Analyze(IReadOnlyList<Tile> hand, IReadOnlyList<Tile>? visibleTiles = null, int openMeldCount = 0)
    {
        var concealedCounts = TileEncoding.CountTiles(hand);
        var visibleCounts = TileEncoding.CountTiles(visibleTiles ?? Array.Empty<Tile>());
        var baseShanten = CalculateMinimumShanten(concealedCounts, openMeldCount);
        var ukeire = new List<UkeireEntry>();

        for (var tileIndex = 0; tileIndex < TileEncoding.TileTypeCount; tileIndex++)
        {
            if (concealedCounts[tileIndex] >= 4)
            {
                continue;
            }

            var remainingCopies = 4 - concealedCounts[tileIndex] - visibleCounts[tileIndex];
            if (remainingCopies <= 0)
            {
                continue;
            }

            concealedCounts[tileIndex]++;
            var nextShanten = CalculateMinimumShanten(concealedCounts, openMeldCount);
            concealedCounts[tileIndex]--;

            if (nextShanten < baseShanten)
            {
                ukeire.Add(new UkeireEntry(TileEncoding.FromIndex(tileIndex), remainingCopies));
            }
        }

        return new ShantenResult(
            baseShanten,
            ukeire
                .OrderByDescending(entry => entry.RemainingCopies)
                .ThenBy(entry => TileEncoding.ToIndex(entry.Tile))
                .ToArray());
    }

    internal static int CalculateMinimumShanten(int[] concealedCounts, int openMeldCount)
    {
        var standard = CalculateStandardShanten((int[])concealedCounts.Clone(), openMeldCount);
        if (openMeldCount > 0)
        {
            return standard;
        }

        var chiitoitsu = CalculateChiitoitsuShanten(concealedCounts);
        var kokushi = CalculateKokushiShanten(concealedCounts);
        return Math.Min(standard, Math.Min(chiitoitsu, kokushi));
    }

    private static int CalculateStandardShanten(int[] concealedCounts, int openMeldCount)
    {
        var best = 8;
        Search(concealedCounts, 0, openMeldCount, 0, 0, ref best);
        return best;
    }

    private static int CalculateChiitoitsuShanten(int[] concealedCounts)
    {
        var pairCount = 0;
        var distinctCount = 0;

        foreach (var count in concealedCounts)
        {
            if (count > 0)
            {
                distinctCount++;
            }

            if (count >= 2)
            {
                pairCount++;
            }
        }

        return 6 - pairCount + Math.Max(0, 7 - distinctCount);
    }

    private static int CalculateKokushiShanten(int[] concealedCounts)
    {
        var uniqueTerminalsAndHonors = 0;
        var hasPair = false;

        foreach (var terminalIndex in TileEncoding.TerminalAndHonorIndices)
        {
            if (concealedCounts[terminalIndex] > 0)
            {
                uniqueTerminalsAndHonors++;
            }

            if (concealedCounts[terminalIndex] >= 2)
            {
                hasPair = true;
            }
        }

        return 13 - uniqueTerminalsAndHonors - (hasPair ? 1 : 0);
    }

    private static void Search(int[] counts, int tileIndex, int melds, int taatsu, int pairs, ref int best)
    {
        while (tileIndex < TileEncoding.TileTypeCount && counts[tileIndex] == 0)
        {
            tileIndex++;
        }

        if (tileIndex >= TileEncoding.TileTypeCount)
        {
            UpdateBest(melds, taatsu, pairs, ref best);
            return;
        }

        UpdateBest(melds, taatsu, pairs, ref best);

        if (counts[tileIndex] >= 3)
        {
            counts[tileIndex] -= 3;
            Search(counts, tileIndex, melds + 1, taatsu, pairs, ref best);
            counts[tileIndex] += 3;
        }

        if (TileEncoding.CanMakeSequence(tileIndex, counts))
        {
            counts[tileIndex]--;
            counts[tileIndex + 1]--;
            counts[tileIndex + 2]--;
            Search(counts, tileIndex, melds + 1, taatsu, pairs, ref best);
            counts[tileIndex]++;
            counts[tileIndex + 1]++;
            counts[tileIndex + 2]++;
        }

        if (counts[tileIndex] >= 2)
        {
            counts[tileIndex] -= 2;

            if (pairs == 0)
            {
                Search(counts, tileIndex, melds, taatsu, 1, ref best);
            }

            Search(counts, tileIndex, melds, taatsu + 1, pairs, ref best);
            counts[tileIndex] += 2;
        }

        if (TileEncoding.CanMakeRyanmen(tileIndex, counts))
        {
            counts[tileIndex]--;
            counts[tileIndex + 1]--;
            Search(counts, tileIndex, melds, taatsu + 1, pairs, ref best);
            counts[tileIndex]++;
            counts[tileIndex + 1]++;
        }

        if (TileEncoding.CanMakeKanchan(tileIndex, counts))
        {
            counts[tileIndex]--;
            counts[tileIndex + 2]--;
            Search(counts, tileIndex, melds, taatsu + 1, pairs, ref best);
            counts[tileIndex]++;
            counts[tileIndex + 2]++;
        }

        counts[tileIndex]--;
        Search(counts, tileIndex, melds, taatsu, pairs, ref best);
        counts[tileIndex]++;
    }

    private static void UpdateBest(int melds, int taatsu, int pairs, ref int best)
    {
        if (melds > 4)
        {
            melds = 4;
        }

        if (taatsu > 4 - melds)
        {
            taatsu = 4 - melds;
        }

        var candidate = 8 - (melds * 2) - taatsu - Math.Min(pairs, 1);
        if (candidate < best)
        {
            best = candidate;
        }
    }
}

public static class TileEncoding
{
    public const int TileTypeCount = 34;

    internal static readonly int[] TerminalAndHonorIndices =
    [
        0, 8, 9, 17, 18, 26,
        27, 28, 29, 30, 31, 32, 33,
    ];

    public static int[] CountTiles(IEnumerable<Tile> tiles)
    {
        var counts = new int[TileTypeCount];
        foreach (var tile in tiles)
        {
            counts[ToIndex(tile)]++;
        }

        return counts;
    }

    public static int ToIndex(Tile tile)
    {
        var rankOffset = tile.Rank - 1;
        return tile.Suit switch
        {
            TileSuit.Man => rankOffset,
            TileSuit.Pin => 9 + rankOffset,
            TileSuit.Sou => 18 + rankOffset,
            TileSuit.Honor => 27 + rankOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(tile)),
        };
    }

    public static Tile FromIndex(int index)
    {
        return index switch
        {
            >= 0 and <= 8 => new Tile(TileSuit.Man, index + 1),
            >= 9 and <= 17 => new Tile(TileSuit.Pin, index - 8),
            >= 18 and <= 26 => new Tile(TileSuit.Sou, index - 17),
            >= 27 and <= 33 => new Tile(TileSuit.Honor, index - 26),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    public static bool CanMakeSequence(int tileIndex, int[] counts)
    {
        return IsNumberTile(tileIndex) &&
               tileIndex % 9 <= 6 &&
               counts[tileIndex] > 0 &&
               counts[tileIndex + 1] > 0 &&
               counts[tileIndex + 2] > 0;
    }

    public static bool CanMakeRyanmen(int tileIndex, int[] counts)
    {
        return IsNumberTile(tileIndex) &&
               tileIndex % 9 <= 7 &&
               counts[tileIndex] > 0 &&
               counts[tileIndex + 1] > 0;
    }

    public static bool CanMakeKanchan(int tileIndex, int[] counts)
    {
        return IsNumberTile(tileIndex) &&
               tileIndex % 9 <= 6 &&
               counts[tileIndex] > 0 &&
               counts[tileIndex + 2] > 0;
    }

    private static bool IsNumberTile(int tileIndex)
    {
        return tileIndex < 27;
    }
}