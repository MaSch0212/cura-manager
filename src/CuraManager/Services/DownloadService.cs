using CuraManager.Models;
using CuraManager.Services.WebProviders;

namespace CuraManager.Services;

public class DownloadService : IDownloadService
{
    private readonly IDictionary<string, IWebProvider> _providers;

    public ICollection<string> SupportedHosts => _providers.Keys;

    public DownloadService(IEnumerable<IWebProvider> providers)
    {
        _providers = (
            from p in providers
            from h in p.SupportedHosts
            group p by h into g
            select (key: g.Key.ToLower(), value: g.First())
        ).ToDictionary(x => x.key, x => x.value);
    }

    public Task<string> GetProjectName(Uri webAddress)
    {
        var provider = GetProvider(webAddress);
        return provider.GetProjectName(webAddress);
    }

    public Task DownloadFiles(Uri webAddress, PrintElement printElement)
    {
        var provider = GetProvider(webAddress);
        return provider.DownloadFiles(webAddress, printElement);
    }

    public bool IsLinkSupported(Uri webAddress)
    {
        return _providers.ContainsKey(webAddress.Host.ToLower());
    }

    private IWebProvider GetProvider(Uri webAddress)
    {
        if (!_providers.TryGetValue(webAddress.Host.ToLower(), out var provider))
            throw new NotSupportedException($"The host \"{webAddress.Host}\" is not supported.");
        return provider;
    }
}
