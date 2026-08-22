using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using MaSch.Presentation;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf.Commands;
using MaSch.Presentation.Wpf.Views.SplitView;
using Application = System.Windows.Application;

namespace CuraManager.ViewModels.Main;

[ObservablePropertyDefinition]
internal interface ISettingsViewModel_Props
{
    AppSettings Settings { get; set; }
    SlicerSettingsViewModel[] Slicers { get; set; }
}

public partial class SettingsViewModel : SplitViewContentViewModel, ISettingsViewModel_Props
{
    private readonly ISettingsService _settingsService;
    private readonly ITranslationManager _translationManager;
    private readonly ISlicerRegistry _slicerRegistry;

    private Task _slicersReloadTask = Task.CompletedTask;

    public ObservableTuple<int?, string>[] AvailableLanguages { get; set; }

    [DependsOn(nameof(AvailableLanguages), nameof(Settings))]
    public ObservableTuple<int?, string> CurrentLanguage
    {
        get => AvailableLanguages?.SingleOrDefault(x => x.Item1 == Settings?.Language);
        set
        {
            if (value != null)
            {
                Settings.Language = value.Item1;
            }
        }
    }

    public ICommand UndoCommand { get; }
    public ICommand SaveCommand { get; }

    public ICommand BrowseDirectoryCommand { get; }

    public SettingsViewModel()
    {
        if (!IsInDesignMode)
        {
            ServiceContext.GetService(out _settingsService);
            ServiceContext.GetService(out _translationManager);
            ServiceContext.GetService(out _slicerRegistry);
        }

        UndoCommand = new DelegateCommand(ExecuteUndo);
        SaveCommand = new DelegateCommand(ExecuteSave);
        BrowseDirectoryCommand = new DelegateCommand(ExecuteBrowsePrintsPath);
    }

    public override async Task OnOpen(CancelEventArgs e)
    {
        RebuildAvailableLanguages();
        Settings = _settingsService.LoadSettings();
        await _slicersReloadTask;
        await base.OnOpen(e);
    }

    public override async Task OnClose(CancelEventArgs e)
    {
        await base.OnClose(e);

        var result = AlertResult.No;
        if (Settings.HasChanges)
            result = MessageBox.Show(
                _translationManager.GetTranslation(nameof(StringTable.Msg_UnsavedChanges)),
                "CuraManager",
                AlertButton.YesNoCancel,
                AlertImage.Warning
            );

        if (result == AlertResult.Yes)
        {
            TrySaveSettings();
        }
        else if (result == AlertResult.Cancel)
        {
            e.Cancel = true;
        }
    }

    partial void OnSettingsChanged(AppSettings previous, AppSettings value)
    {
        Slicers = _slicerRegistry
            .AllProviders.Select(x => new SlicerSettingsViewModel(
                x,
                _slicerRegistry.GetSettings(value, x)
            ))
            .ToArray();

        // Fresh SlicerSettingsViewModels start with AvailableInstallations == null, so every
        // reassignment of Settings (OnOpen's initial load, and ExecuteUndo below) needs to
        // repopulate the version dropdowns. OnSettingsChanged is a synchronous partial void, so
        // this can't be awaited here; OnOpen awaits this same task afterwards instead of starting
        // a second, redundant scan, and ExecuteUndo (which cannot await from a DelegateCommand)
        // relies on this fire-and-forget kickoff alone. ReloadInstallationsAsync cannot throw out
        // of the task it returns, so there is nothing to observe here.
        _slicersReloadTask = ReloadAllInstallationsAsync();
    }

    private Task ReloadAllInstallationsAsync() =>
        Task.WhenAll(Slicers.Select(x => x.ReloadInstallationsAsync(false)));

    #region Command Handlers
    private void ExecuteUndo()
    {
        Settings = _settingsService.LoadSettings();
        RaiseOnMessage(
            _translationManager.GetTranslation(nameof(StringTable.Suc_UndoChanges)),
            MessageType.Success
        );
    }

    private void ExecuteSave()
    {
        TrySaveSettings();
    }

    private void ExecuteBrowsePrintsPath()
    {
        var fbd = new FolderBrowserDialog { SelectedPath = Settings.PrintsPath };

        NativeWindow owner = null;
        if (Application.Current.MainWindow != null)
        {
            owner = new NativeWindow();
            owner.AssignHandle(new WindowInteropHelper(Application.Current.MainWindow).Handle);
        }

        if (fbd.ShowDialog(owner) == DialogResult.OK)
        {
            Settings.PrintsPath = fbd.SelectedPath;
        }
    }
    #endregion

    private void RebuildAvailableLanguages()
    {
        var osLangEntry = _translationManager.GetTranslation("UseSystemLanguage");
        if (AvailableLanguages == null)
        {
            var languages = _translationManager.GetAvailableLanguages().ToList();
            if (languages.TryRemove(CultureInfo.InvariantCulture))
                languages.AddIfNotExists(CultureInfo.GetCultureInfo("en"));
            var nonNeutral = languages
                .Where(x => x.Parent.LCID != CultureInfo.InvariantCulture.LCID)
                .GroupBy(x => x.Parent)
                .ToList();
            languages.Remove(nonNeutral.Where(x => x.Count() <= 1).SelectMany(x => x));
            languages.Remove(nonNeutral.Where(x => x.Count() > 1).Select(x => x.Key));
            AvailableLanguages = new[] { ObservableTuple.Create((int?)null, osLangEntry) }
                .Concat(
                    languages
                        .Where(x => x.LCID != CultureInfo.InvariantCulture.LCID)
                        .Select(x => ObservableTuple.Create((int?)x.LCID, x.NativeName))
                        .OrderBy(x => x.Item2)
                )
                .ToArray();
            NotifyPropertyChanged(nameof(AvailableLanguages));
            NotifyPropertyChanged(nameof(CurrentLanguage));
        }
        else
        {
            AvailableLanguages[0].Item2 = osLangEntry;
        }
    }

    private void TrySaveSettings()
    {
        ExecuteLoadingAction(
            () => _settingsService.SaveSettings(Settings),
            _translationManager.GetTranslation(nameof(StringTable.Suc_SaveChanges)),
            _translationManager.GetTranslation(nameof(StringTable.Fail_SaveChanges))
        );
    }
}
