using MaSch.Core.Observable;

namespace CuraManager.ViewModels
{
    public class MainWindowModel : ObservableObject
    {
        public string Version { get; set; }
        public bool IsInDebugMode { get; set; }

        public MainWindowModel()
        {
            Version = App.GetCurrentVersion(true);

#if DEBUG
            IsInDebugMode = true;
#endif
        }
    }
}
