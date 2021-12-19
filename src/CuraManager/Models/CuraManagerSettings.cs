using MaSch.Presentation.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CuraManager.Models;

[ObservablePropertyDefinition]
internal interface ICuraManagerSettings_Props
{
    string PrintsPath { get; set; }
    string CuraAppDataPath { get; set; }
    string CuraProgramFilesPath { get; set; }
    bool UpdateCuraProjectsOnOpen { get; set; }
    int? Language { get; set; }
    bool ShowWebDialogWhenAddingLink { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    DefaultTheme Theme { get; set; }
}

public partial class CuraManagerSettings : ObservableChangeTrackingObject, ICuraManagerSettings_Props
{
    public CuraManagerSettings()
    {
        _updateCuraProjectsOnOpen = true;
        _showWebDialogWhenAddingLink = true;
        _theme = DefaultTheme.Dark;
    }
}
