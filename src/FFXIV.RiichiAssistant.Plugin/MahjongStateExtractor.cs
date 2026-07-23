using Dalamud.Plugin.Services;
using FFXIV.RiichiAssistant.Core;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FFXIV.RiichiAssistant.Plugin;

public sealed record MahjongUiProbe(
    bool IsAddonVisible,
    bool IsPlayerSeated,
    bool HasStableHandStructure,
    bool HasStableVisibleTileStructures,
    bool IsRoundActive,
    bool IsResultScreenVisible);

public sealed record ExtractedMahjongState(
    PluginSessionState SessionState,
    MahjongTableSnapshot? Snapshot,
    IReadOnlyList<string> Warnings);

public sealed record MahjongAddonValueMap(
    int LocalPlayerIndex = -1,
    int RoundWind = -1,
    int SeatWind = -1,
    int Honba = -1,
    int RiichiSticks = -1,
    int RoundActive = -1,
    int RoundEnded = -1,
    int HandStart = -1,
    int HandCount = -1,
    int VisibleTilesStart = -1,
    int VisibleTilesCount = -1,
    int DoraStart = -1,
    int DoraCount = -1,
    IReadOnlyList<int>? ScoreIndices = null,
    IReadOnlyList<int>? RiichiFlagIndices = null,
    IReadOnlyList<int>? DiscardCountIndices = null)
{
    public IReadOnlyList<int> ScoreIndicesOrEmpty => ScoreIndices ?? Array.Empty<int>();

    public IReadOnlyList<int> RiichiFlagIndicesOrEmpty => RiichiFlagIndices ?? Array.Empty<int>();

    public IReadOnlyList<int> DiscardCountIndicesOrEmpty => DiscardCountIndices ?? Array.Empty<int>();
}

public sealed record MahjongAddonConfiguration(
    string InfoAddonName,
    IReadOnlyList<string> TableAddonNames,
    MahjongAddonValueMap ValueMap)
{
    public static MahjongAddonConfiguration Default { get; } = new(
        "GSInfoEmj",
        ["Emj", "Mahjong", "GSMahjong", "GoldSaucerMahjong"],
        new MahjongAddonValueMap());
}

public interface IDalamudMahjongUiSource
{
    MahjongUiProbe ReadProbe();

    MahjongTableSnapshot? TryReadSnapshot();
}

public interface IMahjongSessionDetector
{
    PluginSessionState Detect(MahjongUiProbe probe, MahjongTableSnapshot? snapshot);
}

public interface IMahjongStateExtractor
{
    ExtractedMahjongState Extract();
}

public interface IMahjongTileDecoder
{
    bool TryDecode(int rawValue, out Tile tile);
}

public interface IMahjongAddonSnapshotDecoder
{
    unsafe bool TryDecode(AddonGSInfoEmj* infoAddon, AtkUnitBase* tableAddon, MahjongAddonValueMap valueMap, out MahjongTableSnapshot? snapshot, out IReadOnlyList<string> warnings);
}

public sealed class MahjongSessionDetector : IMahjongSessionDetector
{
    public PluginSessionState Detect(MahjongUiProbe probe, MahjongTableSnapshot? snapshot)
    {
        if (!probe.IsAddonVisible || !probe.IsPlayerSeated)
        {
            return PluginSessionState.Inactive;
        }

        if (probe.IsResultScreenVisible || snapshot?.IsRoundEnded == true)
        {
            return PluginSessionState.RoundEnd;
        }

        if (probe.IsRoundActive && probe.HasStableHandStructure && probe.HasStableVisibleTileStructures && snapshot?.IsValidForRecommendations == true)
        {
            return PluginSessionState.InRound;
        }

        return PluginSessionState.WaitingForRoundStart;
    }
}

public sealed unsafe class DalamudMahjongUiSource : IDalamudMahjongUiSource
{
    private readonly IGameGui gameGui;
    private readonly IMahjongAddonSnapshotDecoder snapshotDecoder;
    private readonly MahjongAddonConfiguration configuration;

    public DalamudMahjongUiSource(IGameGui gameGui, IMahjongAddonSnapshotDecoder snapshotDecoder, MahjongAddonConfiguration? configuration = null)
    {
        this.gameGui = gameGui;
        this.snapshotDecoder = snapshotDecoder;
        this.configuration = configuration ?? MahjongAddonConfiguration.Default;
    }

    public MahjongUiProbe ReadProbe()
    {
        var infoAddon = gameGui.GetAddonByName<AddonGSInfoEmj>(configuration.InfoAddonName);
        var tableAddon = ResolveTableAddon();
        var snapshot = TryReadSnapshotInternal(infoAddon, tableAddon, out _);
        var infoVisible = infoAddon is not null && infoAddon->AtkUnitBase.IsVisible && infoAddon->AtkUnitBase.IsReady;
        var tableVisible = tableAddon is not null && tableAddon->IsVisible && tableAddon->IsReady;

        return new MahjongUiProbe(
            IsAddonVisible: infoVisible || tableVisible,
            IsPlayerSeated: infoVisible || tableVisible,
            HasStableHandStructure: snapshot?.HasValidHandCount == true,
            HasStableVisibleTileStructures: snapshot is not null && snapshot.Players.Count == 4,
            IsRoundActive: snapshot?.IsRoundActive ?? false,
            IsResultScreenVisible: snapshot?.IsRoundEnded ?? false);
    }

    public MahjongTableSnapshot? TryReadSnapshot()
    {
        var infoAddon = gameGui.GetAddonByName<AddonGSInfoEmj>(configuration.InfoAddonName);
        var tableAddon = ResolveTableAddon();
        return TryReadSnapshotInternal(infoAddon, tableAddon, out _);
    }

    private MahjongTableSnapshot? TryReadSnapshotInternal(AddonGSInfoEmj* infoAddon, AtkUnitBase* tableAddon, out IReadOnlyList<string> warnings)
    {
        if (infoAddon is null && tableAddon is null)
        {
            warnings = ["No Mahjong-related addon pointers were available."];
            return null;
        }

        if (!snapshotDecoder.TryDecode(infoAddon, tableAddon, configuration.ValueMap, out var snapshot, out warnings))
        {
            return null;
        }

        return snapshot;
    }

    private AtkUnitBase* ResolveTableAddon()
    {
        foreach (var addonName in configuration.TableAddonNames)
        {
            var addon = gameGui.GetAddonByName<AtkUnitBase>(addonName);
            if (addon is not null && addon->IsVisible && addon->IsReady)
            {
                return addon;
            }
        }

        return null;
    }
}

public sealed class MahjongStateExtractor : IMahjongStateExtractor
{
    private readonly IDalamudMahjongUiSource uiSource;
    private readonly IMahjongSessionDetector sessionDetector;

    public MahjongStateExtractor(IDalamudMahjongUiSource uiSource, IMahjongSessionDetector sessionDetector)
    {
        this.uiSource = uiSource;
        this.sessionDetector = sessionDetector;
    }

    public ExtractedMahjongState Extract()
    {
        var probe = uiSource.ReadProbe();
        var snapshot = uiSource.TryReadSnapshot();
        var sessionState = sessionDetector.Detect(probe, snapshot);
        var warnings = new List<string>();

        if (probe.IsAddonVisible && !probe.HasStableHandStructure)
        {
            warnings.Add("Mahjong addon pointers are live, but the hand structure has not stabilized yet.");
        }

        if (probe.IsAddonVisible && !probe.HasStableVisibleTileStructures)
        {
            warnings.Add("Mahjong addon pointers are live, but visible-tile structures are still incomplete.");
        }

        if (sessionState == PluginSessionState.InRound && snapshot is null)
        {
            warnings.Add("Mahjong addons are visible, but the normalized table snapshot could not be decoded from the configured value map.");
        }

        return new ExtractedMahjongState(sessionState, snapshot, warnings);
    }
}

public sealed class SequentialMahjongTileDecoder : IMahjongTileDecoder
{
    public bool TryDecode(int rawValue, out Tile tile)
    {
        tile = default;

        if (rawValue is >= 0 and < TileEncoding.TileTypeCount)
        {
            tile = TileEncoding.FromIndex(rawValue);
            return true;
        }

        tile = rawValue switch
        {
            34 => new Tile(TileSuit.Man, 5, true),
            35 => new Tile(TileSuit.Pin, 5, true),
            36 => new Tile(TileSuit.Sou, 5, true),
            11 when true => new Tile(TileSuit.Man, 1),
            _ => tile,
        };

        if (!Equals(tile, default(Tile)))
        {
            return true;
        }

        var suit = rawValue / 10;
        var rank = rawValue % 10;
        if (rank is < 1 or > 9)
        {
            return false;
        }

        tile = suit switch
        {
            1 => new Tile(TileSuit.Man, rank),
            2 => new Tile(TileSuit.Pin, rank),
            3 => new Tile(TileSuit.Sou, rank),
            4 when rank <= 7 => new Tile(TileSuit.Honor, rank),
            _ => default,
        };

        return !Equals(tile, default(Tile));
    }
}

public sealed unsafe class MahjongAddonSnapshotDecoder : IMahjongAddonSnapshotDecoder
{
    private readonly IMahjongTileDecoder tileDecoder;

    public MahjongAddonSnapshotDecoder()
        : this(new SequentialMahjongTileDecoder())
    {
    }

    public MahjongAddonSnapshotDecoder(IMahjongTileDecoder tileDecoder)
    {
        this.tileDecoder = tileDecoder;
    }

    public bool TryDecode(AddonGSInfoEmj* infoAddon, AtkUnitBase* tableAddon, MahjongAddonValueMap valueMap, out MahjongTableSnapshot? snapshot, out IReadOnlyList<string> warnings)
    {
        var warningList = new List<string>();
        var infoValues = infoAddon is null ? ReadOnlySpan<AtkValue>.Empty : infoAddon->AtkUnitBase.AtkValuesSpan;
        var tableValues = tableAddon is null ? ReadOnlySpan<AtkValue>.Empty : tableAddon->AtkValuesSpan;

        if (infoValues.IsEmpty && tableValues.IsEmpty)
        {
            warnings = ["No AtkValue arrays were available for Mahjong addon decoding."];
            snapshot = null;
            return false;
        }

        if (valueMap.HandStart < 0 || valueMap.HandCount < 0)
        {
            warningList.Add("Mahjong value map is not configured with hand indices yet.");
            warnings = warningList;
            snapshot = null;
            return false;
        }

        var localPlayerIndex = ReadInt(tableValues, infoValues, valueMap.LocalPlayerIndex, 0, warningList);
        var hand = ReadTiles(tableValues, infoValues, valueMap.HandStart, valueMap.HandCount, warningList);
        var visibleTiles = ReadTiles(tableValues, infoValues, valueMap.VisibleTilesStart, valueMap.VisibleTilesCount, warningList);
        var doraIndicators = ReadTiles(tableValues, infoValues, valueMap.DoraStart, valueMap.DoraCount, warningList);
        var roundWind = ReadWind(tableValues, infoValues, valueMap.RoundWind, Wind.East, warningList);
        var seatWind = ReadWind(tableValues, infoValues, valueMap.SeatWind, Wind.East, warningList);
        var roundActive = ReadBool(tableValues, infoValues, valueMap.RoundActive, hand.Count is 13 or 14, warningList);
        var roundEnded = ReadBool(tableValues, infoValues, valueMap.RoundEnded, false, warningList);
        var honba = ReadInt(tableValues, infoValues, valueMap.Honba, 0, warningList);
        var riichiSticks = ReadInt(tableValues, infoValues, valueMap.RiichiSticks, 0, warningList);
        var players = ReadPlayers(tableValues, infoValues, valueMap, warningList);

        snapshot = new MahjongTableSnapshot(
            IsMahjongContentActive: true,
            IsStructureUpdateObserved: hand.Count > 0,
            IsRoundActive: roundActive,
            IsRoundEnded: roundEnded,
            LocalPlayerIndex: Math.Clamp(localPlayerIndex, 0, 3),
            RoundWind: roundWind,
            SeatWind: seatWind,
            Honba: honba,
            RiichiSticks: riichiSticks,
            Hand: hand,
            DoraIndicators: doraIndicators,
            VisibleTiles: visibleTiles,
            Players: players);
        warnings = warningList;
        return true;
    }

    private IReadOnlyList<PlayerSnapshot> ReadPlayers(ReadOnlySpan<AtkValue> tableValues, ReadOnlySpan<AtkValue> infoValues, MahjongAddonValueMap valueMap, List<string> warnings)
    {
        var players = new List<PlayerSnapshot>(4);
        for (var playerIndex = 0; playerIndex < 4; playerIndex++)
        {
            var scoreIndex = playerIndex < valueMap.ScoreIndicesOrEmpty.Count ? valueMap.ScoreIndicesOrEmpty[playerIndex] : -1;
            var riichiIndex = playerIndex < valueMap.RiichiFlagIndicesOrEmpty.Count ? valueMap.RiichiFlagIndicesOrEmpty[playerIndex] : -1;
            var discardIndex = playerIndex < valueMap.DiscardCountIndicesOrEmpty.Count ? valueMap.DiscardCountIndicesOrEmpty[playerIndex] : -1;

            players.Add(new PlayerSnapshot(
                playerIndex,
                ReadInt(tableValues, infoValues, scoreIndex, 25000, warnings),
                ReadBool(tableValues, infoValues, riichiIndex, false, warnings),
                ReadInt(tableValues, infoValues, discardIndex, 0, warnings),
                Array.Empty<Tile>(),
                Array.Empty<Meld>()));
        }

        return players;
    }

    private IReadOnlyList<Tile> ReadTiles(ReadOnlySpan<AtkValue> primary, ReadOnlySpan<AtkValue> secondary, int startIndex, int countIndex, List<string> warnings)
    {
        if (startIndex < 0 || countIndex < 0)
        {
            return Array.Empty<Tile>();
        }

        var count = ReadInt(primary, secondary, countIndex, 0, warnings);
        if (count <= 0)
        {
            return Array.Empty<Tile>();
        }

        var tiles = new List<Tile>(count);
        for (var offset = 0; offset < count; offset++)
        {
            if (!TryGetValue(primary, secondary, startIndex + offset, out var value))
            {
                warnings.Add($"Missing AtkValue for Mahjong tile index {startIndex + offset}.");
                continue;
            }

            var rawTile = ReadInt(value, warnings);
            if (!tileDecoder.TryDecode(rawTile, out var tile))
            {
                warnings.Add($"Unrecognized Mahjong tile code {rawTile} at value index {startIndex + offset}.");
                continue;
            }

            tiles.Add(tile);
        }

        return tiles;
    }

    private static bool TryGetValue(ReadOnlySpan<AtkValue> primary, ReadOnlySpan<AtkValue> secondary, int index, out AtkValue value)
    {
        if (index >= 0 && index < primary.Length)
        {
            value = primary[index];
            return true;
        }

        if (index >= 0 && index < secondary.Length)
        {
            value = secondary[index];
            return true;
        }

        value = default;
        return false;
    }

    private static int ReadInt(ReadOnlySpan<AtkValue> primary, ReadOnlySpan<AtkValue> secondary, int index, int fallback, List<string> warnings)
    {
        if (!TryGetValue(primary, secondary, index, out var value))
        {
            return fallback;
        }

        return ReadInt(value, warnings);
    }

    private static int ReadInt(AtkValue value, List<string> warnings)
    {
        return value.Type switch
        {
            AtkValueType.Int => value.Int,
            AtkValueType.UInt => unchecked((int)value.UInt),
            AtkValueType.Bool => value.Bool ? 1 : 0,
            _ => ReadNumericFallback(value, warnings),
        };
    }

    private static int ReadNumericFallback(AtkValue value, List<string> warnings)
    {
        warnings.Add($"Unsupported AtkValue type {value.Type} encountered during Mahjong snapshot decoding; treating as zero.");
        return 0;
    }

    private static bool ReadBool(ReadOnlySpan<AtkValue> primary, ReadOnlySpan<AtkValue> secondary, int index, bool fallback, List<string> warnings)
    {
        if (!TryGetValue(primary, secondary, index, out var value))
        {
            return fallback;
        }

        return value.Type switch
        {
            AtkValueType.Bool => value.Bool,
            AtkValueType.Int => value.Int != 0,
            AtkValueType.UInt => value.UInt != 0,
            _ => fallback,
        };
    }

    private static Wind ReadWind(ReadOnlySpan<AtkValue> primary, ReadOnlySpan<AtkValue> secondary, int index, Wind fallback, List<string> warnings)
    {
        var value = ReadInt(primary, secondary, index, (int)fallback, warnings);
        return Enum.IsDefined(typeof(Wind), value) ? (Wind)value : fallback;
    }
}