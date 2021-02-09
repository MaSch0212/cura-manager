using MaSch.Presentation.Wpf.Views.SplitView;

namespace CuraManager.ViewModels.Main
{
    public class AboutViewModel : SplitViewContentViewModel
    {
        public static string CurrentVersion => App.GetCurrentVersion(true);

        public AboutViewModel()
        {
        }
    }
}
