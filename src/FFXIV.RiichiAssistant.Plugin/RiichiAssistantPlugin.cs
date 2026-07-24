using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Riichi;

namespace FFXIV.RiichiAssistant.Plugin;

public sealed record PluginRuntimeState(
    DateTimeOffset UpdatedAtUtc,
    ExtractedMahjongState ExtractedState,
    PluginFrame Frame,
    IReadOnlyList<string> Warnings);

public sealed class RiichiAssistantPlugin : IDalamudPlugin
{
    private const string CommandName = "/riichiassistant";
    private static readonly string[] CallTypeLabels = Enum.GetNames<CallType>();
    private static readonly TimeSpan MinimumUpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IPluginLog pluginLog;
    private readonly IRecommendationEngine recommendationEngine;
    private readonly IUiBuilder uiBuilder;
    private readonly IMahjongStateExtractor stateExtractor;
    private readonly PluginCoordinator coordinator;
    private DateTimeOffset lastUpdateUtc;
    private bool isDebugWindowOpen;
    private int previewCallTypeIndex = Array.IndexOf(CallTypeLabels, nameof(CallType.Chi));
    private int previewShantenDelta;
    private float previewExpectedHanAfterCall = 1;
    private bool previewMaintainsValueTarget = true;
    private bool previewIsClosedHand = true;
    private bool previewIsTenpai;
    private bool previewIsWinningCall;
    private bool previewHasClearKanUpside;
    private bool previewUseDamaExpectedValue;
    private float previewDamaExpectedValue = 3900;
    private bool previewUseRiichiExpectedValue;
    private float previewRiichiExpectedValue = 5200;

    public RiichiAssistantPlugin(ICommandManager commandManager, IFramework framework, IGameGui gameGui, IPluginLog pluginLog, IUiBuilder uiBuilder)
    {
        this.commandManager = commandManager;
        this.framework = framework;
        this.pluginLog = pluginLog;
        this.uiBuilder = uiBuilder;

        var snapshotDecoder = new MahjongAddonSnapshotDecoder();
        var uiSource = new DalamudMahjongUiSource(gameGui, snapshotDecoder);
        var sessionDetector = new MahjongSessionDetector();
        stateExtractor = new MahjongStateExtractor(uiSource, sessionDetector);
        recommendationEngine = new RecommendationEngine();
        coordinator = new PluginCoordinator(new RiichiAnalysisEngine(), recommendationEngine);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Riichi Assistant debug window.",
            ShowInHelp = true,
        });
        framework.Update += OnFrameworkUpdate;
        uiBuilder.Draw += OnDraw;
        uiBuilder.OpenMainUi += OpenDebugWindow;
        uiBuilder.OpenConfigUi += OpenDebugWindow;
    }

    public string Name => "FFXIV Riichi Assistant";

    public PluginRuntimeState? CurrentState { get; private set; }

    public void Dispose()
    {
        commandManager.RemoveHandler(CommandName);
        framework.Update -= OnFrameworkUpdate;
        uiBuilder.Draw -= OnDraw;
        uiBuilder.OpenMainUi -= OpenDebugWindow;
        uiBuilder.OpenConfigUi -= OpenDebugWindow;
    }

    private void OnCommand(string command, string arguments)
    {
        isDebugWindowOpen = !isDebugWindowOpen;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastUpdateUtc < MinimumUpdateInterval)
        {
            return;
        }

        lastUpdateUtc = now;

        try
        {
            var extractedState = stateExtractor.Extract();
            var frame = coordinator.Update(extractedState.Snapshot);
            var warnings = extractedState.Warnings
                .Concat(frame.Analysis.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            CurrentState = new PluginRuntimeState(now, extractedState, frame, warnings);
        }
        catch (Exception exception)
        {
            pluginLog.Error(exception, "Failed to refresh Mahjong plugin state.");
        }
    }

    private void OpenDebugWindow()
    {
        isDebugWindowOpen = true;
    }

    private void OnDraw()
    {
        if (!isDebugWindowOpen)
        {
            return;
        }

        if (!ImGui.Begin("FFXIV Riichi Assistant", ref isDebugWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Manual test surface for CurrentState.");
        ImGui.Separator();

        if (CurrentState is null)
        {
            ImGui.TextUnformatted("No state has been captured yet.");
            ImGui.End();
            return;
        }

        DrawOverview(CurrentState);
        DrawPlayers(CurrentState.ExtractedState.Snapshot);
        DrawWarnings(CurrentState.Warnings);
        DrawRecommendations(CurrentState.Frame);
        DrawCallPreview();
        ImGui.End();
    }

    private static void DrawOverview(PluginRuntimeState state)
    {
        ImGui.TextUnformatted($"Updated: {state.UpdatedAtUtc:HH:mm:ss} UTC");
        ImGui.TextUnformatted($"Session: {state.Frame.SessionState}");

        var snapshot = state.ExtractedState.Snapshot;
        if (snapshot is null)
        {
            ImGui.TextUnformatted("Snapshot: unavailable");
            return;
        }

        ImGui.TextUnformatted($"Round: {snapshot.RoundWind} / Seat: {snapshot.SeatWind}");
        ImGui.TextUnformatted($"Hand: {FormatTiles(snapshot.Hand)}");
        ImGui.TextUnformatted($"Dora: {FormatTiles(snapshot.DoraIndicators)}");
        ImGui.TextUnformatted($"Visible tiles: {snapshot.VisibleTiles.Count}");

        var shanten = state.Frame.Analysis.Shanten;
        ImGui.TextUnformatted(shanten is null
            ? "Shanten: unavailable"
            : $"Shanten: {shanten.Shanten} | Ukeire: {shanten.UkeireCount}");
    }

    private static void DrawPlayers(MahjongTableSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Players.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Players");
        foreach (var player in snapshot.Players)
        {
            var localMarker = player.PlayerIndex == snapshot.LocalPlayerIndex ? " (You)" : string.Empty;
            ImGui.BulletText($"P{player.PlayerIndex}{localMarker}: score {player.Score}, riichi {(player.IsRiichi ? "yes" : "no")}, discards {player.DiscardCount}, melds {player.OpenMelds.Count}");
        }
    }

    private static void DrawWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Warnings");
        foreach (var warning in warnings)
        {
            ImGui.BulletText(warning);
        }
    }

    private static void DrawRecommendations(PluginFrame frame)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Top discards");
        if (frame.TopDiscards.Count == 0)
        {
            ImGui.TextUnformatted("No discard recommendations yet.");
        }
        else
        {
            foreach (var recommendation in frame.TopDiscards)
            {
                ImGui.BulletText($"{recommendation.Tile}: shanten {recommendation.ResultingShanten}, ukeire {recommendation.UkeireCount}, EV {recommendation.ExpectedValue:F0}");
            }
        }

        if (frame.PendingCallRecommendation is not null)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Call: {frame.PendingCallRecommendation.CallType}");
            ImGui.TextWrapped(frame.PendingCallRecommendation.Reason);
        }
    }

    private void DrawCallPreview()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Live call preview");
        ImGui.Combo("Call type", ref previewCallTypeIndex, CallTypeLabels, CallTypeLabels.Length);
        ImGui.SliderInt("Shanten delta", ref previewShantenDelta, -2, 2);
        ImGui.SliderFloat("Expected han", ref previewExpectedHanAfterCall, 0, 13, "%.1f");
        ImGui.Checkbox("Maintains value target", ref previewMaintainsValueTarget);
        ImGui.Checkbox("Closed hand", ref previewIsClosedHand);
        ImGui.Checkbox("Tenpai", ref previewIsTenpai);
        ImGui.Checkbox("Winning call", ref previewIsWinningCall);
        ImGui.Checkbox("Clear kan upside", ref previewHasClearKanUpside);
        ImGui.Checkbox("Use dama EV", ref previewUseDamaExpectedValue);
        if (previewUseDamaExpectedValue)
        {
            ImGui.SliderFloat("Dama EV", ref previewDamaExpectedValue, 0, 24000, "%.0f");
        }

        ImGui.Checkbox("Use riichi EV", ref previewUseRiichiExpectedValue);
        if (previewUseRiichiExpectedValue)
        {
            ImGui.SliderFloat("Riichi EV", ref previewRiichiExpectedValue, 0, 24000, "%.0f");
        }

        var previewInput = new CallRecommendationInput(
            CallType: Enum.Parse<CallType>(CallTypeLabels[previewCallTypeIndex]),
            ShantenDelta: previewShantenDelta,
            ExpectedHanAfterCall: previewExpectedHanAfterCall,
            MaintainsValueTarget: previewMaintainsValueTarget,
            IsClosedHand: previewIsClosedHand,
            IsTenpai: previewIsTenpai,
            IsWinningCall: previewIsWinningCall,
            HasClearKanUpside: previewHasClearKanUpside,
            DamaExpectedValue: previewUseDamaExpectedValue ? previewDamaExpectedValue : null,
            RiichiExpectedValue: previewUseRiichiExpectedValue ? previewRiichiExpectedValue : null,
            Notes: "Manual debug preview");
        var recommendation = recommendationEngine.RecommendCall(previewInput);
        ImGui.TextUnformatted($"Recommendation: {(recommendation.ShouldCall ? "Call" : "Skip")} {recommendation.CallType}");
        ImGui.TextWrapped(recommendation.Reason);
    }

    private static string FormatTiles(IReadOnlyList<Tile> tiles)
    {
        return tiles.Count == 0 ? "-" : string.Join(" ", tiles.Select(tile => tile.ToString()));
    }
}