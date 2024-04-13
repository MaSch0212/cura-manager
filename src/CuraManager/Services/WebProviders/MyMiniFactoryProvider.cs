using System.IO;
using System.IO.Compression;
using System.Net;
using CuraManager.Models;
using CuraManager.Views;

// Will not fix this because MyMiniFactory Provider is currently not used
#pragma warning disable SYSLIB0014 // Type or member is obsolete

namespace CuraManager.Services.WebProviders;

public class MyMiniFactoryProvider : IWebProvider
{
    private readonly WebClient _webClient = new();

    public ICollection<string> SupportedHosts { get; } = new[] { "www.myminifactory.com" };

    public async Task<string> GetProjectName(Uri webAddress)
    {
        return Path.GetFileNameWithoutExtension((await GetResponseUri(webAddress)).ToString());
    }

    public async Task DownloadFiles(Uri webAddress, PrintElement printElement)
    {
        var zipFilePath = Path.GetTempFileName();
        await _webClient.DownloadFileTaskAsync(GetDownloadUrl(webAddress), zipFilePath);

        var fileUri = await GetResponseUri(webAddress);

        if (Path.GetExtension(fileUri.ToString()).ToLower() == ".zip")
        {
            using (var zipFile = ZipFile.OpenRead(zipFilePath))
            {
                await CreateProjectFromArchiveDialog
                    .GetFilesToExtract(zipFile, printElement.DirectoryLocation)
                    .ForEachAsync(x => Task.Run(() => x.Entry.ExtractToFile(x.TargetPath)));
            }
        }
        else
        {
            File.Copy(
                zipFilePath,
                Path.Combine(printElement.DirectoryLocation, Path.GetFileName(fileUri.ToString()))
            );
        }

        File.Delete(zipFilePath);
    }

    private async Task<Uri> GetResponseUri(Uri webAddress)
    {
        var request = WebRequest.Create(GetDownloadUrl(webAddress));
        request.Method = "HEAD";
        using (var response = await request.GetResponseAsync())
            return response.ResponseUri;
    }

    private Uri GetDownloadUrl(Uri webAddress)
    {
        var builder = new UriBuilder(webAddress);

        var id = RegularExpressions
            .MyMiniFactoryId()
            .Match(webAddress.AbsoluteUri)
            .Groups["id"]
            .Value;
        builder.Path = $"/download/{id}";

        return builder.Uri;
    }
}
