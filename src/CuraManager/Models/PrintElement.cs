using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using CuraManager.Services.Slicers;
using Newtonsoft.Json;

namespace CuraManager.Models;

/// <summary>
/// The high-level category a file in a print element's directory falls into.
/// </summary>
public enum PrintElementFileCategory
{
    /// <summary>A 3D model file, e.g. <c>.stl</c>, <c>.obj</c>, or <c>.x3d</c>.</summary>
    Model,

    /// <summary>
    /// A file whose extension is used by slicer project files (e.g. <c>.3mf</c>) but which needs
    /// further inspection to determine which slicer, if any, produced it.
    /// </summary>
    MaybeSlicerProject,

    /// <summary>Any other file.</summary>
    Other,
}

[ObservablePropertyDefinition]
internal interface IPrintElement_Props
{
    bool IsInitializing { get; set; }
    PrintElementMetadata Metadata { get; set; }
    IList<SlicerFileGroup> SlicerProjectFiles { get; set; }
    IList<PrintElementFile> ModelFiles { get; set; }
    IList<PrintElementFile> OtherFiles { get; set; }
}

public sealed partial class PrintElement : ObservableObject, IDisposable, IPrintElement_Props
{
    private readonly object _initializationLock = new();
    private FileSystemWatcher _fileSystemWatcher;
    private bool _isInitialized = false;
    private bool _tagsRaiseEvent = true;

    public string DirectoryLocation { get; }
    public string Name => Path.GetFileName(DirectoryLocation);
    public DateTime CreationTime => Directory.GetCreationTime(DirectoryLocation);

    partial void OnMetadataChanged(PrintElementMetadata previous, PrintElementMetadata value)
    {
        _tagsRaiseEvent = false;
        Tags.Set(value.Tags);
        _tagsRaiseEvent = true;
    }

    public IEnumerable<PrintElementFile> AllFiles =>
        SlicerProjectFiles.SelectMany(x => x.Files).Concat(ModelFiles).Concat(OtherFiles);
    public ObservableCollection<string> Tags { get; }
    public string TagsDisplay => string.Join(", ", Tags.OrderBy(x => x));

    [DependsOn(nameof(Metadata))]
    public bool IsArchived
    {
        get => Metadata.IsArchived;
        set
        {
            Metadata.IsArchived = value;
            SaveMetadata();
            NotifyPropertyChanged();
        }
    }

    public PrintElement(string location)
    {
        DirectoryLocation = location;
        SlicerProjectFiles = new ObservableCollection<SlicerFileGroup>();
        ModelFiles = new ObservableCollection<PrintElementFile>();
        OtherFiles = new ObservableCollection<PrintElementFile>();
        Tags = new ObservableCollection<string>();
        Metadata = new PrintElementMetadata();
        Tags.CollectionChanged += (s, e) =>
        {
            if (!_tagsRaiseEvent)
                return;

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                Metadata.Tags.Clear();
            }
            else
            {
                if (e.OldItems != null)
                    Metadata.Tags.RemoveWhere(x => e.OldItems.Contains(x));
                if (e.NewItems != null)
                    Metadata.Tags.AddIfNotExists(e.NewItems.OfType<string>());
            }

            SaveMetadata();
            NotifyPropertyChanged(nameof(TagsDisplay));
        };
    }

    public bool Initialize()
    {
        lock (_initializationLock)
        {
            if (_isInitialized)
                return false;
            try
            {
                IsInitializing = true;
                FillInformation();
                _fileSystemWatcher = new FileSystemWatcher(DirectoryLocation)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName,
                };
                _fileSystemWatcher.Created += OnFileCreated;
                _fileSystemWatcher.Deleted += OnFileDeleted;
                _fileSystemWatcher.Renamed += OnFileRenamed;
                _fileSystemWatcher.EnableRaisingEvents = true;
                _isInitialized = true;
                return true;
            }
            finally
            {
                IsInitializing = false;
            }
        }
    }

    public void InitializeMetadata()
    {
        Metadata = LoadMetadata();
    }

    private void FillInformation()
    {
        SlicerProjectFiles.Clear();
        ModelFiles.Clear();
        OtherFiles.Clear();
        foreach (
            var file in Directory.GetFiles(DirectoryLocation, "*", SearchOption.TopDirectoryOnly)
        )
        {
            GetCorrectListForFile(file)?.Add(new PrintElementFile(file));
        }

        Metadata = LoadMetadata();
    }

    public void AddFile(string filePath)
    {
        GetCorrectListForFile(filePath)?.Add(new PrintElementFile(filePath));
    }

    #region Event Handlers

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var file = GetFile(e.OldFullPath, out var list);

        Application.Current.Dispatcher.Invoke(() =>
        {
            // GetCorrectListForFile can create a group, which mutates the bound
            // SlicerProjectFiles collection, so it has to run on the dispatcher
            // thread -- same reason OnFileCreated calls it inside the Invoke.
            var newList = GetCorrectListForFile(e.FullPath);
            if (newList == null)
                return;

            if (file != null)
            {
                file.RefreshFilePath(e.FullPath);
                if (!ReferenceEquals(list, newList))
                {
                    list.Remove(file);
                    newList.Add(file);
                    RemoveEmptyGroup();
                }
            }
            else
            {
                newList.Add(new PrintElementFile(e.FullPath));
            }
        });
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        var file = GetFile(e.FullPath, out var list);
        if (file != null)
            Application.Current.Dispatcher.Invoke(() =>
            {
                list.Remove(file);
                RemoveEmptyGroup();
            });
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
            GetCorrectListForFile(e.FullPath)?.Add(new PrintElementFile(e.FullPath))
        );
    }

    #endregion

    /// <summary>
    /// Removes the first slicer group that has become empty, if any. A group is created lazily
    /// the first time a file for its provider is seen (see <see cref="GetOrCreateGroup"/>) and
    /// must be dropped again once its last file is removed or moved out, so the UI's
    /// <c>ItemsControl</c> over <see cref="SlicerProjectFiles"/> never renders an empty group.
    /// </summary>
    private void RemoveEmptyGroup()
    {
        var emptyGroup = SlicerProjectFiles.FirstOrDefault(x => x.Files.Count == 0);
        if (emptyGroup != null)
            SlicerProjectFiles.Remove(emptyGroup);
    }

    public void Dispose()
    {
        _fileSystemWatcher.EnableRaisingEvents = false;
        _fileSystemWatcher.Created -= OnFileCreated;
        _fileSystemWatcher.Deleted -= OnFileDeleted;
        _fileSystemWatcher.Renamed -= OnFileRenamed;
        _fileSystemWatcher.Dispose();
    }

    private PrintElementFile GetFile(string filePath, out IList<PrintElementFile> list)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        bool Predicate(PrintElementFile x) =>
            x.FileName == fileName && x.FileExtension == extension;

        PrintElementFile result;
        foreach (var group in SlicerProjectFiles)
        {
            if (group.Files.TryFirst(Predicate, out result))
            {
                list = group.Files;
                return result;
            }
        }

        if (ModelFiles.TryFirst(Predicate, out result))
        {
            list = ModelFiles;
        }
        else if (OtherFiles.TryFirst(Predicate, out result))
        {
            list = OtherFiles;
        }
        else
        {
            result = null;
            list = null;
        }

        return result;
    }

    private IList<PrintElementFile> GetCorrectListForFile(string filePath)
    {
        if (
            string.Equals(
                Path.GetFileName(filePath),
                "metadata.json",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return null;

        switch (CategorizeByExtension(Path.GetExtension(filePath)))
        {
            case PrintElementFileCategory.Model:
                return ModelFiles;
            case PrintElementFileCategory.MaybeSlicerProject:
                var provider = ServiceContext
                    .GetService<ISlicerRegistry>()
                    .FindProviderForFile(filePath);
                return provider == null ? ModelFiles : GetOrCreateGroup(provider);
            default:
                return OtherFiles;
        }
    }

    private IList<PrintElementFile> GetOrCreateGroup(ISlicerProvider provider)
    {
        var group = SlicerProjectFiles.FirstOrDefault(x => x.Provider.Id == provider.Id);
        if (group == null)
        {
            group = new SlicerFileGroup(provider);
            SlicerProjectFiles.Add(group);
        }

        return group.Files;
    }

    /// <summary>
    /// Classifies a file extension into the broad category used to decide which list a print
    /// element file belongs to. Extracted as a pure, static method so it can be unit tested
    /// without constructing a <see cref="PrintElement"/> (which requires a real directory and a
    /// live <see cref="FileSystemWatcher"/>).
    /// </summary>
    /// <param name="extension">The file extension, including the leading dot (e.g. <c>".stl"</c>).</param>
    /// <returns>The category the extension falls into.</returns>
    internal static PrintElementFileCategory CategorizeByExtension(string extension)
    {
        if (IsExt(".stl", ".obj", ".x3d"))
            return PrintElementFileCategory.Model;
        if (IsExt(".3mf"))
            return PrintElementFileCategory.MaybeSlicerProject;
        return PrintElementFileCategory.Other;

        bool IsExt(params string[] e) =>
            e.Any(x => string.Equals(extension, x, StringComparison.OrdinalIgnoreCase));
    }

    private PrintElementMetadata LoadMetadata()
    {
        var metadataFile = Path.Combine(DirectoryLocation, "metadata.json");
        if (!File.Exists(metadataFile))
            return new PrintElementMetadata();
        return JsonConvert.DeserializeObject<PrintElementMetadata>(File.ReadAllText(metadataFile));
    }

    public void SaveMetadata()
    {
        var metadataFile = Path.Combine(DirectoryLocation, "metadata.json");
        File.WriteAllText(metadataFile, JsonConvert.SerializeObject(Metadata, Formatting.Indented));
    }
}
