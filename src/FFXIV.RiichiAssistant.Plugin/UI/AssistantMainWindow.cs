using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Policy.Abstractions;

namespace FFXIV.RiichiAssistant.Plugin.UI;

internal enum AssistantMode
{
    Off,
    Hints,
    AutoPlay,
}

internal sealed class AssistantMainWindow
{
    private static readonly string[] CallTypeLabels = Enum.GetNames<CallType>();

    private readonly IRecommendationEngine recommendationEngine;

    private AssistantMode assistantMode = AssistantMode.Hints;
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

    public AssistantMainWindow(IRecommendationEngine recommendationEngine)
    {
        this.recommendationEngine = recommendationEngine;
    }

    public void Draw(ref bool isWindowOpen, PluginRuntimeState? currentState)
    {
        if (!isWindowOpen)
        {
            return;
        }

        if (!ImGui.Begin("FFXIV Riichi Assistant", ref isWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Manual test surface for CurrentState.");
        ImGui.Separator();

        if (currentState is null)
        {
            ImGui.TextUnformatted("No state has been captured yet.");
            ImGui.End();
            return;
        }

        DrawModePanel(currentState.Frame.SessionState);
        DrawOverview(currentState);
        DrawPlayers(currentState.ExtractedState.Snapshot);
        DrawWarnings(currentState.Warnings);
        if (assistantMode == AssistantMode.Off)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Assistant mode is Off. Solver recommendations are hidden.");
        }
        else
        {
            DrawRecommendations(currentState.Frame);
            DrawStrategicSummary(currentState.Frame);
            DrawPolicySummary(currentState.PolicyDecision);
            DrawCallPreview();

            if (assistantMode == AssistantMode.AutoPlay)
            {
                ImGui.Separator();
                ImGui.TextWrapped("Auto-play mode is reserved for future interaction wiring. The current build only provides hints and strategy guidance.");
            }
        }

        ImGui.End();
    }

    private void DrawModePanel(PluginSessionState sessionState)
    {
        ImGui.TextUnformatted("Mode");
        ImGui.SameLine();
        ImGui.TextUnformatted(sessionState == PluginSessionState.InRound ? "in match" : "idle");

        DrawModeButton(AssistantMode.Off, "Off", "Do nothing");
        ImGui.SameLine();
        DrawModeButton(AssistantMode.Hints, "Hints", "Highlight best move");
        ImGui.SameLine();
        DrawModeButton(AssistantMode.AutoPlay, "Auto-play", "Reserved for future automation");
        ImGui.Separator();
    }

    private void DrawModeButton(AssistantMode mode, string title, string description)
    {
        var isSelected = assistantMode == mode;
        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, 0xFF3A4A68);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xFF4B5F85);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0xFF5B739E);
        }

        if (ImGui.Button($"{title}\n{description}", new Vector2(170, 48)))
        {
            assistantMode = mode;
        }

        if (isSelected)
        {
            ImGui.PopStyleColor(3);
        }
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
        ImGui.TextUnformatted("Best move");
        if (frame.TopDiscards.Count == 0)
        {
            ImGui.TextUnformatted("No discard recommendations yet.");
        }
        else
        {
            var best = frame.TopDiscards[0];
            ImGui.TextUnformatted($"Discard {best.Tile}");
            ImGui.TextWrapped(best.Reason);

            if (frame.TopDiscards.Count > 1)
            {
                ImGui.TextUnformatted("Alternatives");
                foreach (var recommendation in frame.TopDiscards.Skip(1))
                {
                    ImGui.BulletText($"{recommendation.Tile}: shanten {recommendation.ResultingShanten}, ukeire {recommendation.UkeireCount}, EV {recommendation.ExpectedValue:F0}");
                }
            }
        }

        if (frame.PendingCallRecommendation is not null)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Call: {frame.PendingCallRecommendation.CallType}");
            ImGui.TextWrapped(frame.PendingCallRecommendation.Reason);
        }
    }

    private static void DrawStrategicSummary(PluginFrame frame)
    {
        if (frame.Strategy is null)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Strategic posture");
        ImGui.TextUnformatted($"Threat: {frame.Strategy.Threat.ThreatLevel} (riichi threats: {frame.Strategy.Threat.RiichiOpponents})");
        ImGui.TextWrapped(frame.Strategy.Threat.Reason);
        ImGui.TextUnformatted(frame.Strategy.ShouldPush ? "Plan: push" : "Plan: fold");
        ImGui.TextWrapped(frame.Strategy.PushFoldReason);
    }

    private static void DrawPolicySummary(PolicyDecision? policyDecision)
    {
        if (policyDecision is null)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Policy layer");
        ImGui.TextUnformatted(policyDecision.PushFold.ShouldPush
            ? $"Push/fold: push ({policyDecision.PushFold.ThreatLevel})"
            : $"Push/fold: fold ({policyDecision.PushFold.ThreatLevel})");
        ImGui.TextWrapped(policyDecision.PushFold.Reason);

        if (policyDecision.Call is not null)
        {
            ImGui.TextUnformatted($"Call policy: {(policyDecision.Call.ShouldCall ? "accept" : "decline")} {policyDecision.Call.CallType}");
            ImGui.TextWrapped(policyDecision.Call.Reason);
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
