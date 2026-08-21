using System.Collections.ObjectModel;
using CuraManager.Resources;
using CuraManager.Services.Slicers;
using MaSch.Presentation.Translation;

namespace CuraManager.Models;

/// <summary>
/// The project files in one print element that belong to a single slicer.
/// </summary>
public class SlicerFileGroup
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SlicerFileGroup"/> class.
    /// </summary>
    /// <param name="provider">The slicer provider this group's files belong to.</param>
    public SlicerFileGroup(ISlicerProvider provider)
    {
        Provider = provider;
        Files = new ObservableCollection<PrintElementFile>();
        Header = string.Format(
            ServiceContext
                .GetService<ITranslationManager>()
                .GetTranslation(nameof(StringTable.SlicerProjectFiles)),
            provider.DisplayName
        );
    }

    /// <summary>Gets the slicer provider this group's files belong to.</summary>
    public ISlicerProvider Provider { get; }

    /// <summary>Gets the display header for this group, e.g. "Cura Project Files".</summary>
    public string Header { get; }

    /// <summary>Gets the project files belonging to this group's slicer.</summary>
    public ObservableCollection<PrintElementFile> Files { get; }
}
