﻿using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Services;
using MaSch.Core;
using MaSch.Core.Attributes;
using MaSch.Core.Extensions;
using MaSch.Core.Observable;
using MaSch.Presentation;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf.Commands;
using MaSch.Presentation.Wpf.Views.SplitView;
using Application = System.Windows.Application;

namespace CuraManager.ViewModels.Main
{
    [ObservablePropertyDefinition]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "Property definition")]
    internal interface ISettingsViewModel_Props
    {
        CuraManagerSettings Settings { get; set; }
    }

    public partial class SettingsViewModel : SplitViewContentViewModel, ISettingsViewModel_Props
    {
        private readonly ISettingsService _settingsService;
        private readonly ITranslationManager _translationManager;

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
            }

            UndoCommand = new DelegateCommand(ExecuteUndo);
            SaveCommand = new DelegateCommand(ExecuteSave);
            BrowseDirectoryCommand = new DelegateCommand<string>(ExecuteBrowseDirectory);
        }

        public override async Task OnOpen(CancelEventArgs e)
        {
            RebuildAvailableLanguages();

            Settings = _settingsService.LoadSettings();

            await base.OnOpen(e);
        }

        public override async Task OnClose(CancelEventArgs e)
        {
            await base.OnClose(e);

            var result = AlertResult.No;
            if (Settings.HasChanges)
                result = MessageBox.Show(_translationManager.GetTranslation(nameof(StringTable.Msg_UnsavedChanges)), "CuraManager", AlertButton.YesNoCancel, AlertImage.Warning);

            if (result == AlertResult.Yes)
            {
                TrySaveSettings();
            }
            else if (result == AlertResult.Cancel)
            {
                e.Cancel = true;
            }
        }

        #region Command Handlers
        private void ExecuteUndo()
        {
            Settings = _settingsService.LoadSettings();
            RaiseOnMessage(_translationManager.GetTranslation(nameof(StringTable.Suc_UndoChanges)), MessageType.Success);
        }

        private void ExecuteSave()
        {
            TrySaveSettings();
        }

        private void ExecuteBrowseDirectory(string settingName)
        {
            var property = Settings.GetType().GetProperty(settingName);
            if (property == null)
                throw new ArgumentException($"A setting with the name \"{settingName}\" does not exist.", nameof(settingName));

            var fbd = new FolderBrowserDialog { SelectedPath = (string)property.GetValue(Settings) };
            NativeWindow owner = null;
            if (Application.Current.MainWindow != null)
            {
                owner = new NativeWindow();
                owner.AssignHandle(new WindowInteropHelper(Application.Current.MainWindow).Handle);
            }

            if (fbd.ShowDialog(owner) == DialogResult.OK)
            {
                property.SetValue(Settings, fbd.SelectedPath);
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
                var nonNeutral = languages.Where(x => x.Parent.LCID != CultureInfo.InvariantCulture.LCID).GroupBy(x => x.Parent).ToList();
                languages.Remove(nonNeutral.Where(x => x.Count() <= 1).SelectMany(x => x));
                languages.Remove(nonNeutral.Where(x => x.Count() > 1).Select(x => x.Key));
                AvailableLanguages = new[] { ObservableTuple.Create((int?)null, osLangEntry) }
                    .Concat(languages.Where(x => x.LCID != CultureInfo.InvariantCulture.LCID).Select(x => ObservableTuple.Create((int?)x.LCID, x.NativeName)).OrderBy(x => x.Item2)).ToArray();
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
                _translationManager.GetTranslation(nameof(StringTable.Fail_SaveChanges)));
        }
    }
}
