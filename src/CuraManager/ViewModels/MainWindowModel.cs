using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using CuraManager.Extensions;
using MaSch.Presentation.Wpf.Commands;

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
            }
        );
    }

    private async Task SearchForUpdateAsync()
    {
        var clientHandler = new HttpClientHandler { AllowAutoRedirect = false };
        var httpClient = new HttpClient(clientHandler);

        var response = await httpClient.GetAsync(
            "https://github.com/MaSch0212/cura-manager/releases/latest"
        );
        var location = response.Headers.Location?.ToString();

        if (location is null)
            return;

        var match = RegularExpressions.ExtractVersionFromReleaseTag().Match(location);
        if (!match.Success)
            return;

        var current = VersionExtensions.SafeParse(Version);
        var latest = VersionExtensions.SafeParse(match.Groups["version"].Value);

        Application.Current.Dispatcher.Invoke(() =>
        {
            LatestVersion = latest.ToString(latest.Revision > 0 ? 4 : 3);
            IsUpdateAvailable = latest > current;
        });
    }
}
