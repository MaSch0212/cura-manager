using MaSch.Presentation.Wpf.Commands;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace CuraManager.ViewModels;

[ObservablePropertyDefinition]
public interface IMainWindowModel_Props
{
    string LatestVersion { get; set; }
    bool IsUpdateAvailable { get; set; }
}

public partial class MainWindowModel : ObservableObject, IMainWindowModel_Props
{
    public string Version { get; set; }
    public bool IsInDebugMode { get; set; }

    public ICommand OpenLatestGithubReleaseCommand { get; }

    public MainWindowModel()
    {
        Version = App.GetCurrentVersion(true);
        OpenLatestGithubReleaseCommand = new DelegateCommand(ExecuteOpenLatestGithubReleaseCommand);
        Task.Run(SearchForUpdateAsync);

#if DEBUG
        IsInDebugMode = true;
#endif
    }

    private void ExecuteOpenLatestGithubReleaseCommand()
    {
        Process.Start(
            new ProcessStartInfo("https://github.com/MaSch0212/cura-manager/releases/latest")
            {  
                UseShellExecute = true,
            });
    }

    private async Task SearchForUpdateAsync()
    {
        var clientHandler = new HttpClientHandler { AllowAutoRedirect = false };
        var httpClient = new HttpClient(clientHandler);

        var response = await httpClient.GetAsync("https://github.com/MaSch0212/cura-manager/releases/latest");
        var location = response.Headers.Location?.ToString();

        if (location is null)
            return;

        var match = Regex.Match(location, @"release-(?<version>[0-9]+(\.[0-9]+){0,3})$");
        if (!match.Success)
            return;

        var current = Normalize(Version);
        var latest = Normalize(match.Groups["version"].Value);

        Application.Current.Dispatcher.Invoke(() =>
        {
            LatestVersion = latest.ToString(latest.Revision > 0 ? 4 : 3);
            IsUpdateAvailable = latest > current;
        });
    }

    private Version Normalize(string version)
    {
        if (!System.Version.TryParse(version, out var v))
            return null;

        return new Version(
            Math.Max(v.Major, 0),
            Math.Max(v.Minor, 0),
            Math.Max(v.Build, 0),
            Math.Max(v.Revision, 0));
    }
}
