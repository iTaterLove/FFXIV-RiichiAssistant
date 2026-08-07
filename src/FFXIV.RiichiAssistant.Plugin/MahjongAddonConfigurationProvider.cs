using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FFXIV.RiichiAssistant.Plugin;

internal sealed record MahjongLayoutProfilesDocument(
    string SelectedProfile,
    Dictionary<string, MahjongAddonConfigurationProfile> Profiles);

internal sealed record MahjongAddonConfigurationProfile(
    string InfoAddonName,
    IReadOnlyList<string> TableAddonNames,
    MahjongAddonValueMap ValueMap);

internal static class MahjongAddonConfigurationProvider
{
    private const string ProfileFileName = "mahjong-layout-profiles.json";
    private const string SafeFallbackProfileName = "safe-fallback";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static MahjongAddonConfiguration Load(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        var filePath = Path.Combine(pluginInterface.ConfigDirectory.FullName, ProfileFileName);
        var knownProfiles = BuildKnownProfiles();

        EnsureTemplateFileExists(filePath, knownProfiles, log);

        MahjongLayoutProfilesDocument? document;
        try
        {
            var raw = File.ReadAllText(filePath);
            document = JsonSerializer.Deserialize<MahjongLayoutProfilesDocument>(raw, JsonOptions);
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to parse Mahjong layout profile file; using safe fallback profile.");
            return ToRuntimeConfiguration(knownProfiles[SafeFallbackProfileName]);
        }

        if (document is null)
        {
            log.Warning("Mahjong layout profile file was empty; using safe fallback profile.");
            return ToRuntimeConfiguration(knownProfiles[SafeFallbackProfileName]);
        }

        var mergedProfiles = new Dictionary<string, MahjongAddonConfigurationProfile>(knownProfiles, StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        {
            mergedProfiles[profile.Key] = profile.Value;
        }

        if (!mergedProfiles.TryGetValue(document.SelectedProfile, out var selectedProfile))
        {
            log.Warning("Selected Mahjong profile '{ProfileName}' was not found. Falling back to '{FallbackProfile}'.", document.SelectedProfile, SafeFallbackProfileName);
            selectedProfile = mergedProfiles[SafeFallbackProfileName];
        }

        log.Information("Using Mahjong layout profile '{ProfileName}'.", document.SelectedProfile);
        return ToRuntimeConfiguration(selectedProfile);
    }

    private static void EnsureTemplateFileExists(string filePath, Dictionary<string, MahjongAddonConfigurationProfile> knownProfiles, IPluginLog log)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        var template = new MahjongLayoutProfilesDocument(SafeFallbackProfileName, knownProfiles);
        var json = JsonSerializer.Serialize(template, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, json);
        log.Information("Created Mahjong profile template at '{ProfilePath}'.", filePath);
    }

    private static MahjongAddonConfiguration ToRuntimeConfiguration(MahjongAddonConfigurationProfile profile)
    {
        return new MahjongAddonConfiguration(
            profile.InfoAddonName,
            profile.TableAddonNames,
            profile.ValueMap);
    }

    private static Dictionary<string, MahjongAddonConfigurationProfile> BuildKnownProfiles()
    {
        return new Dictionary<string, MahjongAddonConfigurationProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [SafeFallbackProfileName] = new MahjongAddonConfigurationProfile(
                "GSInfoEmj",
                ["Emj", "EmjL", "Mahjong", "GSMahjong", "GoldSaucerMahjong"],
                new MahjongAddonValueMap()),

            ["manual-template"] = new MahjongAddonConfigurationProfile(
                "GSInfoEmj",
                ["Emj", "EmjL", "Mahjong", "GSMahjong", "GoldSaucerMahjong"],
                new MahjongAddonValueMap(
                    ScoreIndices: [
                        -1,
                        -1,
                        -1,
                        -1
                    ],
                    RiichiFlagIndices: [
                        -1,
                        -1,
                        -1,
                        -1
                    ],
                    DiscardStartIndices: [
                        -1,
                        -1,
                        -1,
                        -1
                    ],
                    DiscardCountIndices: [
                        -1,
                        -1,
                        -1,
                        -1
                    ],
                    PendingCallCandidateStarts: [
                        -1,
                        -1,
                        -1,
                        -1
                    ],
                    PendingCallCandidateCounts: [
                        -1,
                        -1,
                        -1,
                        -1
                    ])),
        };
    }
}
