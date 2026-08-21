using MaSch.Presentation.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CuraManager.Models;

[ObservablePropertyDefinition]
internal interface IAppSettings_Props
{
    string PrintsPath { get; set; }
    string CuraAppDataPath { get; set; }
    string CuraProgramFilesPath { get; set; }
    bool UpdateCuraProjectsOnOpen { get; set; }
    int? Language { get; set; }
    bool ShowWebDialogWhenAddingLink { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    DefaultTheme Theme { get; set; }

    int SettingsVersion { get; set; }
    string ActiveSlicerId { get; set; }
    IDictionary<string, SlicerSettings> Slicers { get; set; }

    /// <summary>
    /// Legacy Cura UI-automation project naming. Removed in 2.0.
    /// </summary>
    bool EnableLegacyCuraProjectNaming { get; set; }
}

public partial class AppSettings : ObservableChangeTrackingObject, IAppSettings_Props
{
    public AppSettings()
    {
        _updateCuraProjectsOnOpen = true;
        _showWebDialogWhenAddingLink = true;
        _theme = DefaultTheme.Dark;
        _slicers = new Dictionary<string, SlicerSettings>();
    }
}
