using CuraManager.Models;

namespace CuraManager.Services;

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);

    AppGuiSettings LoadGuiSettings();
    void SaveGuiSettings(AppGuiSettings settings);
}
