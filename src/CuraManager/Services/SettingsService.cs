using System.IO;
using CuraManager.Models;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf;
using Newtonsoft.Json;

namespace CuraManager.Services;

public class SettingsService : ISettingsService
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MaSch",
        "CuraManager"
    );
    private static readonly string SettingFilePath = Path.Combine(AppDataPath, "settings.json");
    private static readonly string SettingsBackupFilePath = Path.Combine(
        AppDataPath,
        "settings.json.bak"
    );
    private static readonly string GuiSettingsFilePath = Path.Combine(
        AppDataPath,
        "settings.gui.json"
    );

    public AppSettings LoadSettings()
    {
        var json = File.Exists(SettingFilePath) ? File.ReadAllText(SettingFilePath) : null;
        var (result, migrated) = SettingsMigration.Load(json);

        if (migrated)
        {
            // Keep the pre-migration file recoverable, once.
            if (!File.Exists(SettingsBackupFilePath))
                File.Copy(SettingFilePath, SettingsBackupFilePath);
            SaveSettings(result);
        }

        result.ResetChangeTracking();
        return result;
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataPath);
        File.WriteAllText(
            SettingFilePath,
            JsonConvert.SerializeObject(settings, Formatting.Indented)
        );
        settings.ResetChangeTracking();

        var transMan = ServiceContext.Instance.GetService<ITranslationManager>();
        var c = settings.Language.HasValue
            ? CultureInfo.GetCultureInfo(settings.Language.Value)
            : null;
        if (transMan.CurrentLanguage.LCID != c?.LCID)
            transMan.CurrentLanguage = c;

        var themeManager = ServiceContext.Instance.GetService<IThemeManager>();
        themeManager.LoadTheme(Theme.FromDefaultTheme(settings.Theme));
    }

    public AppGuiSettings LoadGuiSettings()
    {
        AppGuiSettings result;

        if (!File.Exists(GuiSettingsFilePath))
            result = new AppGuiSettings();
        else
            result = JsonConvert.DeserializeObject<AppGuiSettings>(
                File.ReadAllText(GuiSettingsFilePath)
            );

        return result;
    }

    public void SaveGuiSettings(AppGuiSettings settings)
    {
        Directory.CreateDirectory(AppDataPath);
        File.WriteAllText(
            GuiSettingsFilePath,
            JsonConvert.SerializeObject(settings, Formatting.Indented)
        );
    }
}
