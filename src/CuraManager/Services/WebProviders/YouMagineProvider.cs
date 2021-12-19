using CuraManager.Models;
using CuraManager.Views;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CuraManager.Services.WebProviders;

public class YouMagineProvider : IWebProvider
{
    private readonly HttpClient _httpClient;

    public YouMagineProvider()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public ICollection<string> SupportedHosts { get; } = new[] { "www.youmagine.com" };

    public async Task<string> GetProjectName(Uri webAddress)
    {
        return Path.GetFileNameWithoutExtension((await GetResponseUri(webAddress)).ToString());
    }

    public async Task DownloadFiles(Uri webAddress, PrintElement printElement)
    {
        var zipFilePath = Path.GetTempFileName();
        var fileUri = await GetResponseUri(webAddress);
        using (var response = await _httpClient.GetAsync(fileUri))
        using (var fs = new FileStream(zipFilePath, FileMode.Create))
            await response.Content.CopyToAsync(fs);

        if (Path.GetExtension(fileUri.ToString()).ToLower() == ".zip")
        {
            using var zipFile = ZipFile.OpenRead(zipFilePath);
            await CreateProjectFromArchiveDialog.GetFilesToExtract(zipFile, printElement.DirectoryLocation)
                .Where(x => !x.Entry.Name.ToLower().In("license.html", "readme.pdf") && !x.Entry.Name.ToLower().EndsWith("-attribution.pdf"))
                .ForEachAsync(x => Task.Run(() => x.Entry.ExtractToFile(x.TargetPath)));
        }
        else
        {
            File.Copy(zipFilePath, Path.Combine(printElement.DirectoryLocation, Path.GetFileName(fileUri.ToString())));
        }

        File.Delete(zipFilePath);
    }

    private async Task<Uri> GetResponseUri(Uri webAddress)
    {
        var json = await _httpClient.GetStringAsync(GetDownloadDataUri(webAddress));
        var jObject = JObject.Parse(json);
        return new Uri(new Uri("https://www.youmagine.com/"), jObject["url"].ToObject<string>());
    }

    private static Uri GetDownloadDataUri(Uri webAddress)
    {
        var builder = new UriBuilder(webAddress);

        builder.Path += "/download";

        return builder.Uri;
    }
}
