using CuraManager.Models;
using Newtonsoft.Json.Linq;

namespace CuraManager.Services;

/// <summary>
/// Upgrades persisted settings from the pre-multi-slicer schema.
/// </summary>
public static class SettingsMigration
{
    public const int CurrentVersion = 1;
    public const string CuraProviderId = "cura";

    /// <summary>
    /// Loads settings from raw JSON, migrating if needed.
    /// </summary>
    /// <param name="json">The settings file contents, or <see langword="null"/> if no file exists.</param>
    /// <returns>The settings, and whether a migration was performed and therefore needs saving.</returns>
    public static (AppSettings Settings, bool Migrated) Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            // Fresh install: the new behaviour is the default.
            return (new AppSettings { SettingsVersion = CurrentVersion }, false);
        }

        var raw = JObject.Parse(json);
        if (raw.Value<int?>(nameof(AppSettings.SettingsVersion)) >= CurrentVersion)
            return (raw.ToObject<AppSettings>(), false);

        var settings = raw.ToObject<AppSettings>();
        settings.SettingsVersion = CurrentVersion;

        // An existing file means an upgrading user, who keeps today's behaviour.
        settings.EnableLegacyCuraProjectNaming = true;

        var programFiles = raw.Value<string>("CuraProgramFilesPath");
        var appData = raw.Value<string>("CuraAppDataPath");
        if (!string.IsNullOrEmpty(programFiles) || !string.IsNullOrEmpty(appData))
        {
            settings.Slicers[CuraProviderId] = new SlicerSettings
            {
                IsEnabled = true,
                ProgramFilesPath = programFiles,
                AppDataPath = appData,
                UpdateProjectsOnOpen = raw.Value<bool?>("UpdateCuraProjectsOnOpen") ?? true,
            };
            settings.ActiveSlicerId = CuraProviderId;
        }

        return (settings, true);
    }
}
