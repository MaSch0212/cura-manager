using CuraManager.Models;

namespace CuraManager.Services;

public interface ISettingsService
{
    CuraManagerSettings LoadSettings();
    void SaveSettings(CuraManagerSettings settings);

    CuraManagerGuiSettings LoadGuiSettings();
    void SaveGuiSettings(CuraManagerGuiSettings settings);
}
