using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
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

        // Each group owns its own Files collection, so CollectionViewSource.GetDefaultView
        // returns a view scoped to this instance rather than one shared across groups. Binding
        // the ItemsControl to this view (instead of a CollectionViewSource declared in
        // DataTemplate.Resources) keeps the sort working: resources in a DataTemplate don't
        // inherit the surrounding DataContext, so a CollectionViewSource placed there can never
        // resolve `{Binding Files}` and would render nothing.
        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.SortDescriptions.Add(
            new SortDescription(nameof(PrintElementFile.FileName), ListSortDirection.Ascending)
        );
    }

    /// <summary>Gets the slicer provider this group's files belong to.</summary>
    public ISlicerProvider Provider { get; }

    /// <summary>Gets the display header for this group, e.g. "Cura Project Files".</summary>
    public string Header { get; }

    /// <summary>Gets the project files belonging to this group's slicer.</summary>
    public ObservableCollection<PrintElementFile> Files { get; }

    /// <summary>Gets a live view over <see cref="Files"/>, sorted ascending by file name.</summary>
    public ICollectionView FilesView { get; }
}
