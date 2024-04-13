using CuraManager.Services.WebProviders;

namespace CuraManager.Services;

public interface IDownloadService : IWebProvider
{
    bool IsLinkSupported(Uri webAddress);
}
