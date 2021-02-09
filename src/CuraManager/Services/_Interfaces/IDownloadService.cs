using CuraManager.Services.WebProviders;
using System;

namespace CuraManager.Services
{
    public interface IDownloadService : IWebProvider
    {
        bool IsLinkSupported(Uri webAddress);
    }
}
