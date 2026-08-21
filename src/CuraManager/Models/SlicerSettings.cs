namespace CuraManager.Models;

[ObservablePropertyDefinition]
internal interface ISlicerSettings_Props
{
    bool IsEnabled { get; set; }
    string ProgramFilesPath { get; set; }
    string AppDataPath { get; set; }

    /// <summary>
    /// Ignored unless the owning provider reports <c>SupportsProfileUpdateOnOpen</c>.
    /// </summary>
    bool UpdateProjectsOnOpen { get; set; }
}

public partial class SlicerSettings : ObservableChangeTrackingObject, ISlicerSettings_Props
{
    public SlicerSettings()
    {
        _updateProjectsOnOpen = true;
    }
}
