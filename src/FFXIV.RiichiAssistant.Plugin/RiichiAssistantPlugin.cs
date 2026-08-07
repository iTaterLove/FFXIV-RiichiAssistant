using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIV.RiichiAssistant.Decision;
using FFXIV.RiichiAssistant.Core;
using FFXIV.RiichiAssistant.Policy;
using FFXIV.RiichiAssistant.Policy.Abstractions;
using FFXIV.RiichiAssistant.Riichi;
using FFXIV.RiichiAssistant.Plugin.UI;

namespace FFXIV.RiichiAssistant.Plugin;

public sealed record PluginRuntimeState(
    DateTimeOffset UpdatedAtUtc,
    ExtractedMahjongState ExtractedState,
    PluginFrame Frame,
    PolicyDecision? PolicyDecision,
    IReadOnlyList<string> Warnings);

public sealed class RiichiAssistantPlugin : IDalamudPlugin
{
    private const string CommandName = "/riichiassistant";
    private static readonly TimeSpan MinimumUpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IPluginLog pluginLog;
    private readonly IRecommendationEngine recommendationEngine;
    private readonly IStrategicPolicyEngine strategicPolicyEngine;
    private readonly IPolicy policy;
    private readonly IUiBuilder uiBuilder;
    private readonly IMahjongStateExtractor stateExtractor;
    private readonly PluginCoordinator coordinator;
    private readonly AssistantMainWindow mainWindow;
    private DateTimeOffset lastUpdateUtc;
    private bool isDebugWindowOpen;

    public RiichiAssistantPlugin(ICommandManager commandManager, IFramework framework, IGameGui gameGui, IPluginLog pluginLog, IUiBuilder uiBuilder, IDalamudPluginInterface pluginInterface)
    {
        this.commandManager = commandManager;
        this.framework = framework;
        this.pluginLog = pluginLog;
        this.uiBuilder = uiBuilder;

        var snapshotDecoder = new MahjongAddonSnapshotDecoder();
        var addonConfiguration = MahjongAddonConfigurationProvider.Load(pluginInterface, pluginLog);
        var uiSource = new DalamudMahjongUiSource(gameGui, snapshotDecoder, addonConfiguration);
        var sessionDetector = new MahjongSessionDetector();
        stateExtractor = new MahjongStateExtractor(uiSource, sessionDetector);
        recommendationEngine = new RecommendationEngine();
        strategicPolicyEngine = new StrategicPolicyEngine(recommendationEngine, new ShantenSolver());
        policy = new RiichiAssistantPolicy(
            new StrategyDrivenDiscardPolicy(strategicPolicyEngine),
            new StrategyDrivenCallPolicy(strategicPolicyEngine),
            new StrategyDrivenRiichiPolicy(strategicPolicyEngine),
            new StrategyDrivenPushFoldPolicy(strategicPolicyEngine));
        mainWindow = new AssistantMainWindow(recommendationEngine);
        coordinator = new PluginCoordinator(new RiichiAnalysisEngine(), recommendationEngine, strategicPolicyEngine);

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
            var policyDecision = extractedState.Snapshot is { IsValidForRecommendations: true }
                ? policy.Evaluate(extractedState.Snapshot)
                : null;
            var warnings = extractedState.Warnings
                .Concat(frame.Analysis.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            CurrentState = new PluginRuntimeState(now, extractedState, frame, policyDecision, warnings);
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
        mainWindow.Draw(ref isDebugWindowOpen, CurrentState);
    }
}