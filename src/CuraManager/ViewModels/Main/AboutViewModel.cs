using MaSch.Presentation.Wpf.Views.SplitView;

namespace CuraManager.ViewModels.Main;

public class AboutViewModel : SplitViewContentViewModel
{
    public static string CurrentVersion => App.GetCurrentVersion(true);
    public static string Copyright =>
        FileVersionInfo.GetVersionInfo(typeof(App).Assembly.Location).LegalCopyright;

    public AboutViewModel() { }
}
