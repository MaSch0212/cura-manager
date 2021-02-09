using MaSch.Core.Attributes;
using MaSch.Core.Observable;
using MaSch.Presentation.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Diagnostics.CodeAnalysis;

namespace CuraManager.Models
{
    [ObservablePropertyDefinition]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "This is the property definition for CuraManagerSettings.")]
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
}
