using CuraManager.Services;
using MaSch.Presentation.Wpf.Common;
using System.IO;

namespace CuraManager.Views;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsService = ServiceContext.GetService<ISettingsService>();

        Loaded += (s, e) =>
        {
            var settings = settingsService.LoadGuiSettings();
            WindowPosition.ApplyToWindow(settings.WindowPositions, this);
            if (!settings.IsMainViewMenuExpanded)
                MainSplitView.CollapseMenu(true);
            Activate();
        };
        Closing += (s, e) =>
        {
            var settings = settingsService.LoadGuiSettings();
            WindowPosition.AddWindowToList(settings.WindowPositions, this);
            settings.IsMainViewMenuExpanded = MainSplitView.IsExpanded;
            settingsService.SaveGuiSettings(settings);
        };

        MainSplitView.UseAnimations = false;
        try
        {
            var settings = settingsService.LoadSettings();
            if (!Directory.Exists(settings.PrintsPath))
                SettingsPage.IsSelected = true;
            else
                PrintProjectsPage.IsSelected = true;
        }
        finally
        {
            MainSplitView.UseAnimations = true;
        }
    }
}
