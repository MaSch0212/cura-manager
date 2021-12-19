using CuraManager.Models;

namespace CuraManager.Services.WebProviders;

public interface IWebProvider
{
    ICollection<string> SupportedHosts { get; }

    Task<string> GetProjectName(Uri webAddress);
    Task DownloadFiles(Uri webAddress, PrintElement printElement);
}
