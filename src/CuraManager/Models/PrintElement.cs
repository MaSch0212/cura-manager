using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Windows;

namespace CuraManager.Models;

[ObservablePropertyDefinition]
internal interface IPrintElement_Props
{
    bool IsInitializing { get; set; }
    PrintElementMetadata Metadata { get; set; }
    IList<PrintElementFile> CuraProjectFiles { get; set; }
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

    public IEnumerable<PrintElementFile> AllFiles => CuraProjectFiles.Concat(ModelFiles).Concat(OtherFiles);
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
        CuraProjectFiles = new ObservableCollection<PrintElementFile>();
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
        CuraProjectFiles.Clear();
        ModelFiles.Clear();
        OtherFiles.Clear();
        foreach (var file in Directory.GetFiles(DirectoryLocation, "*", SearchOption.TopDirectoryOnly))
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
        var newList = GetCorrectListForFile(e.FullPath);
        if (file != null && newList != null)
        {
            file.RefreshFilePath(e.FullPath);
            if (!ReferenceEquals(list, newList))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    list.Remove(file);
                    newList.Add(file);
                });
            }
        }
        else
        {
            Application.Current.Dispatcher.Invoke(() => newList.Add(new PrintElementFile(e.FullPath)));
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        var file = GetFile(e.FullPath, out var list);
        if (file != null)
            Application.Current.Dispatcher.Invoke(() => list.Remove(file));
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() => GetCorrectListForFile(e.FullPath)?.Add(new PrintElementFile(e.FullPath)));
    }

    #endregion

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

        bool Predicate(PrintElementFile x) => x.FileName == fileName && x.FileExtension == extension;
        if (CuraProjectFiles.TryFirst(Predicate, out var result))
        {
            list = CuraProjectFiles;
        }
        else if (ModelFiles.TryFirst(Predicate, out result))
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
        var ext = Path.GetExtension(filePath);
        if (IsExt(".stl", ".obj", ".x3d"))
            return ModelFiles;
        if (IsExt(".3mf"))
            return IsCuraProjectFile(filePath) ? CuraProjectFiles : ModelFiles;
        return string.Equals(Path.GetFileName(filePath), "metadata.json", StringComparison.OrdinalIgnoreCase) ? null : OtherFiles;

        bool IsExt(params string[] e) => e.Any(x => string.Equals(ext, x, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsCuraProjectFile(string filePath)
    {
        if (Path.GetExtension(filePath) != ".3mf" || !File.Exists(filePath))
            return false;

        ZipArchive zip = null;
        try
        {
            zip = ZipFile.OpenRead(filePath);
            return zip.Entries.Any(x => x.FullName.StartsWith("Cura/", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            var lockingProcesses = Waiter.Retry(() => MaSch.Native.Windows.Explorer.FileInfo.WhoIsLocking(filePath), new RetryOptions { ThrowException = false });
            return lockingProcesses?.Any(x => x.ProcessName == "Cura") == true;
        }
        finally
        {
            zip?.Dispose();
        }
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
