using CuraManager.Models;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf;
using Newtonsoft.Json;
using System.IO;

namespace CuraManager.Services;

public class SettingsService : ISettingsService
{
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MaSch", "CuraManager");
    private static readonly string SettingFilePath = Path.Combine(AppDataPath, "settings.json");
    private static readonly string GuiSettingsFilePath = Path.Combine(AppDataPath, "settings.gui.json");

    public CuraManagerSettings LoadSettings()
    {
        CuraManagerSettings result;

        if (!File.Exists(SettingFilePath))
            result = new CuraManagerSettings();
        else
            result = JsonConvert.DeserializeObject<CuraManagerSettings>(File.ReadAllText(SettingFilePath));

        result.ResetChangeTracking();
        return result;
    }

    public void SaveSettings(CuraManagerSettings settings)
    {
        Directory.CreateDirectory(AppDataPath);
        File.WriteAllText(SettingFilePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        settings.ResetChangeTracking();

        var transMan = ServiceContext.Instance.GetService<ITranslationManager>();
        var c = settings.Language.HasValue ? CultureInfo.GetCultureInfo(settings.Language.Value) : null;
        if (transMan.CurrentLanguage.LCID != c?.LCID)
            transMan.CurrentLanguage = c;

        var themeManager = ServiceContext.Instance.GetService<IThemeManager>();
        themeManager.LoadTheme(Theme.FromDefaultTheme(settings.Theme));
    }

    public CuraManagerGuiSettings LoadGuiSettings()
    {
        CuraManagerGuiSettings result;

        if (!File.Exists(GuiSettingsFilePath))
            result = new CuraManagerGuiSettings();
        else
            result = JsonConvert.DeserializeObject<CuraManagerGuiSettings>(File.ReadAllText(GuiSettingsFilePath));

        return result;
    }

    public void SaveGuiSettings(CuraManagerGuiSettings settings)
    {
        Directory.CreateDirectory(AppDataPath);
        File.WriteAllText(GuiSettingsFilePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
    }
}
