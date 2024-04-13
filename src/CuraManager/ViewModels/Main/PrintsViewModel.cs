using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Services;
using CuraManager.Views;
using MaSch.Core.Observable.Collections;
using MaSch.Presentation;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf.Commands;
using MaSch.Presentation.Wpf.Controls;
using MaSch.Presentation.Wpf.MaterialDesign;
using MaSch.Presentation.Wpf.Views.SplitView;
using Microsoft.Win32;

namespace CuraManager.ViewModels.Main;

[ObservablePropertyDefinition]
internal interface IPrintsViewModel_Props
{
    IList<PrintElement> PrintElements { get; set; }
    PrintElement SelectedElement { get; set; }
    string FilterText { get; set; }
    bool ShowArchivedElements { get; set; }
    bool ShowNonArchivedElements { get; set; }
}

public partial class PrintsViewModel : SplitViewContentViewModel, IPrintsViewModel_Props
{
    private readonly ITranslationManager _translationManager;
    private readonly IPrintsService _printsService;
    private readonly ICuraService _curaService;
    private readonly ISettingsService _settingsService;
    private readonly IDownloadService _downloadService;
    private readonly ICachingService _cachingService;

    private string _currentPrintsPath;
    private MetadataCache _cache;
    public ObservableCollection<string> AvailableTags { get; }
    public ObservableCollection<string> VisibleTags { get; }

    [DependsOn(nameof(PrintElements))]
    public CollectionViewSource PrintElementsViewSource { get; }
    public CollectionViewSource AvailableTagsViewSource { get; }

    public ICommand NewProjectFromModelsCommand { get; }
    public ICommand NewProjectFromWebCommand { get; }
    public ICommand NewProjectFromArchiveCommand { get; }
    public ICommand NewProjectFromClipboardCommand { get; }
    public ICommand ReloadModelsCommand { get; }
    public ICommand RefreshFilterCommand { get; }

    public ICommand AddFilesToProjectCommand { get; }
    public ICommand NewCuraProjectCommand { get; }
    public ICommand OpenProjectFolderCommand { get; }
    public ICommand OpenProjectWebsiteCommand { get; }
    public ICommand DeleteProjectCommand { get; }
    public ICommand RenameProjectCommand { get; }
    public ICommand CreateTagCommand { get; }

    public ICommand OpenProjectFileCommand { get; }
    public ICommand DeleteProjectFileCommand { get; }
    public ICommand RenameProjectFileCommand { get; }
    public ICommand CopyProjectFileToCommand { get; }

    public PrintsViewModel()
    {
        _showArchivedElements = true;
        _showNonArchivedElements = true;

        if (!IsInDesignMode)
        {
            ServiceContext.GetService(out _translationManager);
            ServiceContext.GetService(out _printsService);
            ServiceContext.GetService(out _curaService);
            ServiceContext.GetService(out _settingsService);
            ServiceContext.GetService(out _downloadService);
            ServiceContext.GetService(out _cachingService);
        }

        PrintElementsViewSource = new CollectionViewSource();
        PrintElementsViewSource.Filter += PrintElementsViewSource_OnFilter;
        PrintElementsViewSource.SortDescriptions.Add(
            new SortDescription(nameof(PrintElement.IsArchived), ListSortDirection.Ascending)
        );
        PrintElementsViewSource.SortDescriptions.Add(
            new SortDescription(nameof(PrintElement.Name), ListSortDirection.Ascending)
        );
        AvailableTags = new ObservableCollection<string>();
        AvailableTagsViewSource = new CollectionViewSource { Source = AvailableTags };
        AvailableTagsViewSource.SortDescriptions.Add(
            new SortDescription(null, ListSortDirection.Ascending)
        );
        VisibleTags = new ObservableCollection<string>();
        VisibleTags.CollectionChanged += (s, e) => PrintElementsViewSource.View.Refresh();

        var printElements = new FullyObservableCollection<PrintElement>();
        printElements.CollectionItemPropertyChanged += PrintElements_CollectionItemPropertyChanged;
        PrintElements = printElements;

        NewProjectFromModelsCommand = new AsyncDelegateCommand(ExecuteNewProjectFromModels);
        NewProjectFromWebCommand = new AsyncDelegateCommand(ExecuteNewProjectFromWeb);
        NewProjectFromArchiveCommand = new AsyncDelegateCommand(ExecuteNewProjectFromArchive);
        NewProjectFromClipboardCommand = new AsyncDelegateCommand(ExecuteNewProjectFromClipboard);
        ReloadModelsCommand = new AsyncDelegateCommand(ExecuteReloadModels);
        RefreshFilterCommand = new DelegateCommand(ExecuteRefreshFilter);

        AddFilesToProjectCommand = new AsyncDelegateCommand<PrintElement>(
            x => x != null,
            ExecuteAddFilesToProject
        );
        NewCuraProjectCommand = new AsyncDelegateCommand<PrintElement>(
            x => x != null,
            ExecuteNewCuraProject
        );
        OpenProjectFolderCommand = new DelegateCommand<PrintElement>(
            x => x != null,
            ExecuteOpenProjectFolder
        );
        OpenProjectWebsiteCommand = new DelegateCommand<PrintElement>(
            x => x != null && !string.IsNullOrEmpty(x.Metadata.Website),
            ExecuteOpenProjectWebsite
        );
        DeleteProjectCommand = new AsyncDelegateCommand<PrintElement>(
            x => x != null,
            ExecuteDeleteProject
        );
        RenameProjectCommand = new DelegateCommand<PrintElement>(
            x => x != null,
            ExecuteRenameProject
        );
        CreateTagCommand = new DelegateCommand<PrintElement>(x => x != null, ExecuteCreateTag);

        OpenProjectFileCommand = new DelegateCommand<PrintElementFile>(
            x => x != null,
            ExecuteOpenProjectFile
        );
        DeleteProjectFileCommand = new DelegateCommand<PrintElementFile>(
            x => x != null,
            ExecuteDeleteProjectFile
        );
        RenameProjectFileCommand = new DelegateCommand<PrintElementFile>(
            x => x != null,
            ExecuteRenameProjectFile
        );
        CopyProjectFileToCommand = new AsyncDelegateCommand<Tuple<PrintElementFile, string>>(
            x => x?.Item1 != null && !string.IsNullOrEmpty(x?.Item2),
            ExecuteCopyProjectFileTo
        );
    }

    #region Change Handlers
    partial void OnPrintElementsChanged(IList<PrintElement> previous, IList<PrintElement> value)
    {
        PrintElementsViewSource.Source = value;
        PrintElementsViewSource.View.Refresh();
    }

    partial void OnSelectedElementChanged(PrintElement previous, PrintElement value)
    {
        Task.Run(() =>
        {
            Thread.Sleep(100);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (value?.Initialize() == true)
                    AvailableTags.AddIfNotExists(value.Tags);
            });
        });
    }

    partial void OnFilterTextChanged(string previous, string value)
    {
        Task.Run(() =>
        {
            Thread.Sleep(200);
            if (_filterText == value)
                Application.Current.Dispatcher.Invoke(() => PrintElementsViewSource.View.Refresh());
        });
    }

    partial void OnShowArchivedElementsChanged(bool previous, bool value)
    {
        PrintElementsViewSource.View.Refresh();
    }

    partial void OnShowNonArchivedElementsChanged(bool previous, bool value)
    {
        PrintElementsViewSource.View.Refresh();
    }
    #endregion

    #region Public Methods
    public override async Task OnOpen(CancelEventArgs e)
    {
        await base.OnOpen(e);

        var settings = _settingsService.LoadSettings();
        if (!Directory.Exists(settings.PrintsPath))
        {
            MessageBox.Show(
                _translationManager.GetTranslation(
                    nameof(StringTable.Msg_PrintProjectsFolderNotConfigured)
                ),
                "CuraManager",
                AlertButton.Ok,
                AlertImage.Information
            );
            e.Cancel = true;
        }
        else
        {
            _cache ??= _cachingService.LoadCache(settings.PrintsPath);
            if (_currentPrintsPath != settings.PrintsPath)
            {
                _currentPrintsPath = settings.PrintsPath;
                PrintElements.ForEach(x => x.Dispose());
                PrintElements.Clear();
            }

            var newElements = await _printsService
                .GetNewPrintElements(_cache, PrintElements)
                .ToListAsync();
            newElements.ForEach(x => AvailableTags.AddIfNotExists(x.Tags));
            PrintElements.Add(newElements);
            PrintElementsViewSource.View.Refresh();
        }
    }

    public override async Task OnClose(CancelEventArgs e)
    {
        await RebuildCache();

        await base.OnClose(e);
    }

    public async Task AddFilesToProject(PrintElement project, ICollection<string> filesToAdd)
    {
        if (filesToAdd.Count == 0)
            return;

        var progressMessage = _translationManager.GetTranslation(
            filesToAdd.Count > 1
                ? nameof(StringTable.Prog_AddMultipleFilesToProject)
                : nameof(StringTable.Prog_AddOneFileToProject)
        );
        var failedMessage = _translationManager.GetTranslation(
            filesToAdd.Count > 1
                ? nameof(StringTable.Fail_AddMultipleFilesToProject)
                : nameof(StringTable.Fail_AddOneFileToProject)
        );
        var successMessage = _translationManager.GetTranslation(
            filesToAdd.Count > 1
                ? nameof(StringTable.Suc_AddMultipleFilesToProject)
                : nameof(StringTable.Suc_AddOneFileToProject)
        );
        await ExecuteLoadingAction(
            progressMessage,
            async () =>
            {
                foreach (var file in filesToAdd.Where(File.Exists))
                {
                    var targetPath = Path.Combine(
                        project.DirectoryLocation,
                        Path.GetFileName(file) ?? string.Empty
                    );
                    for (int i = 2; File.Exists(targetPath); i++)
                    {
                        targetPath = Path.Combine(
                            project.DirectoryLocation,
                            Path.GetFileNameWithoutExtension(file)
                                + $" ({i})"
                                + Path.GetExtension(file)
                        );
                    }

                    await Task.Run(() => File.Copy(file, targetPath));
                }
            },
            successMessage,
            failedMessage
        );
    }

    public async Task CreateProjectFromFileDrop(string[] files)
    {
        if (files.Length == 0)
            return;
        if (
            files.Length == 1
            && string.Equals(
                Path.GetExtension(files[0]),
                ".zip",
                StringComparison.OrdinalIgnoreCase
            )
        )
            await CreateProjectFromArchive(files[0]);
        else
            await CreateProjectFromModels(files);
    }

    public async Task CreateProjectFromStringDrop(string drop)
    {
        if (string.IsNullOrWhiteSpace(drop))
            return;
        var firstLine = drop.Split('\r', '\n')[0];
        if (!Uri.TryCreate(firstLine, UriKind.Absolute, out Uri uri))
            return;

        if (uri.Scheme.ToLower().In("file"))
            await CreateProjectFromFileDrop(new[] { uri.LocalPath });
        else if (uri.Scheme.ToLower().In("http", "https"))
            await CreateProjectFromWeb(uri.AbsoluteUri);
    }
    #endregion

    #region Command Handlers
    private async Task ExecuteNewProjectFromModels()
    {
        await CreateProjectFromModels(null);
    }

    private async Task ExecuteNewProjectFromWeb()
    {
        await CreateProjectFromWeb(null);
    }

    private async Task ExecuteNewProjectFromArchive()
    {
        await CreateProjectFromArchive(null);
    }

    private async Task ExecuteNewProjectFromClipboard()
    {
        if (
            Clipboard.ContainsText()
            && Uri.TryCreate(Clipboard.GetText(), UriKind.Absolute, out var link)
            && _downloadService.IsLinkSupported(link)
        )
            await CreateProjectFromWeb(link.ToString());
        else if (Clipboard.ContainsFileDropList())
            await CreateProjectFromFileDrop(Clipboard.GetFileDropList().OfType<string>().ToArray());
        else
            MessageBox.Show(
                _translationManager.GetTranslation(nameof(StringTable.Msg_NothingValidInClipboard)),
                "CuraManager",
                AlertButton.Ok,
                AlertImage.Information
            );
    }

    private async Task ExecuteAddFilesToProject(PrintElement project)
    {
        var ofd = new OpenFileDialog { Multiselect = true, };
        if (ofd.ShowDialog(Application.Current.MainWindow) == true)
        {
            await AddFilesToProject(project, ofd.FileNames);
        }
    }

    private async Task ExecuteNewCuraProject(PrintElement project)
    {
        var settings = _settingsService.LoadSettings();
        if (!_curaService.AreCuraPathsCorrect(settings))
        {
            MessageBox.Show(
                _translationManager.GetTranslation(nameof(StringTable.Msg_CuraPathsNotConfigured)),
                "CuraManager",
                AlertButton.Ok,
                AlertImage.Warning
            );
        }
        else
        {
            await ExecuteLoadingAction(
                _translationManager.GetTranslation(nameof(StringTable.Prog_CreateCuraProject)),
                async () => await _curaService.CreateCuraProject(project),
                _translationManager.GetTranslation(nameof(StringTable.Suc_CreateCuraProject)),
                _translationManager.GetTranslation(nameof(StringTable.Fail_CreateCuraProject))
            );
        }
    }

    private void ExecuteOpenProjectFolder(PrintElement project)
    {
        Process.Start(new ProcessStartInfo(project.DirectoryLocation) { UseShellExecute = true, });
    }

    private void ExecuteOpenProjectWebsite(PrintElement project)
    {
        if (!string.IsNullOrEmpty(project.Metadata.Website))
        {
            Process.Start(
                new ProcessStartInfo(project.Metadata.Website) { UseShellExecute = true, }
            );
        }
    }

    private async Task ExecuteDeleteProject(PrintElement project)
    {
        if (
            MessageBox.Show(
                string.Format(
                    _translationManager.GetTranslation(
                        nameof(StringTable.Msg_ConfirmProjectDeletion)
                    ),
                    project.Name
                ),
                "CuraManager",
                AlertButton.YesNo,
                AlertImage.Question
            ) == AlertResult.No
        )
            return;

        await ExecuteLoadingAction(
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Prog_DeleteProject)),
                project.Name
            ),
            async () =>
            {
                project.Dispose();
                PrintElements.Remove(project);
                await Task.Run(() => Directory.Delete(project.DirectoryLocation, true));
                if (SelectedElement == project)
                    SelectedElement = null;
            },
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Suc_DeleteProject)),
                project.Name
            ),
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Fail_DeleteProject)),
                project.Name
            )
        );
    }

    private void ExecuteRenameProject(PrintElement project)
    {
        string Validation(string newName)
        {
            if (string.Equals(project.Name, newName, StringComparison.OrdinalIgnoreCase))
                return null;

            var path = Path.Combine(Path.GetDirectoryName(project.DirectoryLocation), newName);
            if (Directory.Exists(path))
                return string.Format(
                    _translationManager.GetTranslation(
                        nameof(StringTable.Msg_ProjectAlreadyExists)
                    ),
                    newName
                );

            return null;
        }

        var dialog = new RenameDialog(
            _translationManager.GetTranslation(nameof(StringTable.Title_RenameProject)),
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Desc_RenameProject)),
                project.Name
            ),
            project.Name,
            Validation
        )
        {
            Owner = Application.Current.MainWindow,
        };

        if (dialog.ShowDialog() == true)
        {
            var newPath = Path.Combine(
                Path.GetDirectoryName(project.DirectoryLocation),
                dialog.NewName
            );

            SelectedElement = null;
            PrintElements.Remove(project);
            project.Dispose();

            Directory.Move(project.DirectoryLocation, newPath);

            var newProject = new PrintElement(newPath);
            PrintElements.Add(newProject);
            SelectedElement = newProject;
        }
    }

    private void ExecuteCreateTag(PrintElement project)
    {
        var dialog = new RenameDialog(
            _translationManager.GetTranslation(nameof(StringTable.Title_CreateTag)),
            _translationManager.GetTranslation(nameof(StringTable.Desc_CreateTag)),
            string.Empty,
            s => null
        )
        {
            CustomIcon = new IconPresenter
            {
                Icon = new MaterialDesignIcon(MaterialDesignIconCode.Plus)
            },
            SubmitButtonContent = _translationManager.GetTranslation(nameof(StringTable.Create)),
        };
        if (dialog.ShowDialog() == true)
        {
            AvailableTags.AddIfNotExists(dialog.NewName);
            project.Tags.AddIfNotExists(dialog.NewName);
        }
    }

    private void ExecuteOpenProjectFile(PrintElementFile file)
    {
        if (PrintElement.IsCuraProjectFile(file.FilePath))
        {
            _curaService.OpenCuraProject(file.FilePath);
        }
        else
        {
            Process.Start(new ProcessStartInfo(file.FilePath) { UseShellExecute = true, });
        }
    }

    private void ExecuteDeleteProjectFile(PrintElementFile file)
    {
        if (
            MessageBox.Show(
                string.Format(
                    _translationManager.GetTranslation(nameof(StringTable.Msg_ConfirmFileDeletion)),
                    $"{file.FileName}{file.FileExtension}"
                ),
                "CuraManager",
                AlertButton.YesNo,
                AlertImage.Question
            ) == AlertResult.No
        )
        {
            return;
        }

        File.Delete(file.FilePath);
    }

    private void ExecuteRenameProjectFile(PrintElementFile file)
    {
        string Validation(string newName)
        {
            if (string.Equals(file.FileName, newName, StringComparison.OrdinalIgnoreCase))
                return null;

            var path = Path.Combine(
                Path.GetDirectoryName(file.FilePath),
                newName + file.FileExtension
            );
            if (File.Exists(path))
                return string.Format(
                    _translationManager.GetTranslation(nameof(StringTable.Msg_FileAlreadyExists)),
                    $"{newName}{file.FileExtension}"
                );

            return null;
        }

        var dialog = new RenameDialog(
            _translationManager.GetTranslation(nameof(StringTable.Title_RenameFile)),
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Desc_RenameFile)),
                $"{file.FileName}{file.FileExtension}"
            ),
            file.FileName,
            Validation
        )
        {
            Owner = Application.Current.MainWindow,
        };

        if (dialog.ShowDialog() == true)
        {
            var newPath = Path.Combine(
                Path.GetDirectoryName(file.FilePath),
                dialog.NewName + file.FileExtension
            );
            File.Move(file.FilePath, newPath);
        }
    }

    private async Task ExecuteCopyProjectFileTo(Tuple<PrintElementFile, string> data)
    {
        var targetFile = Path.Combine(
            data.Item2,
            Path.GetFileName(data.Item1.FilePath) ?? Guid.NewGuid().ToString()
        );
        await ExecuteLoadingAction(
            _translationManager.GetTranslation(nameof(StringTable.Prog_CopyFile)),
            () => Task.Run(() => File.Copy(data.Item1.FilePath, targetFile, true)),
            _translationManager.GetTranslation(nameof(StringTable.Suc_CopyFile)),
            _translationManager.GetTranslation(nameof(StringTable.Fail_CopyFile))
        );
    }

    private async Task ExecuteReloadModels()
    {
        await ExecuteLoadingAction(
            _translationManager.GetTranslation(nameof(StringTable.Prog_ReloadModels)),
            new Func<Task>(async () =>
            {
                await Task.Run(() =>
                {
                    PrintElements.Add(_printsService.GetNewPrintElements(_cache, PrintElements));
                    PrintElements.ForEach(x => x.InitializeMetadata());
                });
                PrintElementsViewSource.View.Refresh();
            }),
            _translationManager.GetTranslation(nameof(StringTable.Suc_ReloadModels)),
            _translationManager.GetTranslation(nameof(StringTable.Fail_ReloadModels))
        );
    }

    private void ExecuteRefreshFilter()
    {
        PrintElementsViewSource.View.Refresh();
    }
    #endregion

    #region Private Methods
    private async Task CreateProjectFromModels(IEnumerable<string> files)
    {
        var settings = _settingsService.LoadSettings();
        var dialog = new CreateProjectFromFilesDialog(settings.PrintsPath, files)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            await ExecuteLoadingAction(
                string.Format(
                    _translationManager.GetTranslation(nameof(StringTable.Prog_CreateProject)),
                    dialog.ProjectName
                ),
                async () =>
                {
                    var project = await dialog.CreateProject();
                    PrintElements.Add(project);
                    SelectedElement = project;
                },
                _translationManager.GetTranslation(nameof(StringTable.Suc_CreateProject)),
                _translationManager.GetTranslation(nameof(StringTable.Fail_CreateProject))
            );
        }
    }

    private async Task CreateProjectFromWeb(string url)
    {
        var settings = _settingsService.LoadSettings();

        Func<Task<PrintElement>> getProjectFunc;
        string projectName;

        if (settings.ShowWebDialogWhenAddingLink)
        {
            var dialog = new CreateProjectFromWebDialog(settings.PrintsPath, url)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true)
            {
                projectName = dialog.ProjectName;
                getProjectFunc = dialog.CreateProject;
            }
            else
            {
                return;
            }
        }
        else
        {
            projectName = string.Empty;
            await ExecuteLoadingAction(
                _translationManager.GetTranslation(nameof(StringTable.Prog_LoadProjectData)),
                async () => projectName = await _downloadService.GetProjectName(new Uri(url)),
                null,
                null
            );
            getProjectFunc = () =>
                CreateProjectFromWebDialog.CreateProject(settings.PrintsPath, url, projectName);
        }

        await ExecuteLoadingAction(
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Prog_CreateProject)),
                projectName
            ),
            async () =>
            {
                var project = await getProjectFunc();
                if (project != null)
                {
                    PrintElements.Add(project);
                    SelectedElement = project;
                }

                return project != null;
            },
            _translationManager.GetTranslation(nameof(StringTable.Suc_CreateProject)),
            _translationManager.GetTranslation(nameof(StringTable.Fail_CreateProject))
        );
    }

    private async Task CreateProjectFromArchive(string archivePath)
    {
        var settings = _settingsService.LoadSettings();
        var dialog = new CreateProjectFromArchiveDialog(settings.PrintsPath, archivePath)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            await ExecuteLoadingAction(
                string.Format(
                    _translationManager.GetTranslation(nameof(StringTable.Prog_CreateProject)),
                    dialog.ProjectName
                ),
                async () =>
                {
                    var project = await dialog.CreateProject();
                    PrintElements.Add(project);
                    SelectedElement = project;
                },
                _translationManager.GetTranslation(nameof(StringTable.Suc_CreateProject)),
                _translationManager.GetTranslation(nameof(StringTable.Fail_CreateProject))
            );
        }
    }

    private async Task RebuildCache()
    {
        _cache = new MetadataCache
        {
            PrintsPath = _currentPrintsPath,
            PrintElements = await PrintElements.ToDictionaryAsync(
                x => x.Name,
                x => new PrintElementMetadataCache
                {
                    IsArchived = x.IsArchived,
                    Tags = x.Tags.ToList(),
                }
            ),
        };
        await Task.Run(() => _cachingService.UpdateCache(_cache));
    }

    private void PrintElementsViewSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is PrintElement element)
        {
            e.Accepted =
                (
                    element
                        .Name.ToLower()
                        .Contains(FilterText ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    || element.Tags.Any(x =>
                        x.Contains(FilterText ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    )
                )
                && (
                    (ShowArchivedElements && element.Metadata.IsArchived)
                    || (ShowNonArchivedElements && !element.Metadata.IsArchived)
                )
                && (VisibleTags.Count == 0 || element.Tags.Any(x => VisibleTags.Contains(x)));
        }
        else
        {
            e.Accepted = false;
        }
    }

    private void PrintElements_CollectionItemPropertyChanged(
        object sender,
        CollectionItemPropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(PrintElement.IsArchived))
        {
            if (
                ShowArchivedElements
                && ShowNonArchivedElements
                && Application.Current.Dispatcher.Thread.ManagedThreadId
                    == Environment.CurrentManagedThreadId
            )
                PrintElementsViewSource.View.Refresh();
        }
    }
    #endregion
}
