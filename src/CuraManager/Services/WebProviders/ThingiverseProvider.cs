using CuraManager.Common;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Views;
using MaSch.Presentation.Translation;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace CuraManager.Services.WebProviders;

public class ThingiverseProvider : IWebProvider
{
    private static readonly Regex LinkRegex = new(@"https?:\/\/(www\.)?thingiverse\.com\/thing:[0-9]+", RegexOptions.Compiled);
    private readonly HttpClient _httpClient = new();

    public ICollection<string> SupportedHosts { get; } = new[] { "www.thingiverse.com" };

    public async Task<string> GetProjectName(Uri webAddress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, GetDownloadUrl(webAddress));
        using var response = await _httpClient.SendAsync(request);
        return Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(response.RequestMessage.RequestUri.ToString()))
            .Replace('_', ' ')
            .Replace('+', ' ');
    }

    public async Task DownloadFiles(Uri webAddress, PrintElement printElement)
    {
        var zipFilePath = Path.GetTempFileName();
        using (var response = await _httpClient.GetAsync(GetDownloadUrl(webAddress)))
        using (var fs = new FileStream(zipFilePath, FileMode.Create))
            await response.Content.CopyToAsync(fs);

        using (var zipFile = ZipFile.OpenRead(zipFilePath))
        {
            await CreateProjectFromArchiveDialog.GetFilesToExtract(zipFile, printElement.DirectoryLocation)
                .ForEachAsync(x => Task.Run(() => x.Entry.ExtractToFile(x.TargetPath)));
        }

        File.Delete(zipFilePath);
    }

    private static Uri GetDownloadUrl(Uri webAddress)
    {
        var match = LinkRegex.Match(webAddress.AbsoluteUri);
        if (!match.Success)
            throw new WebProviderException(ServiceContext.GetService<ITranslationManager>().GetTranslation(nameof(StringTable.Msg_WrongThingiverseUrl)));

        var builder = new UriBuilder(match.Value);

        builder.Path += "/zip";

        return builder.Uri;
    }
}
