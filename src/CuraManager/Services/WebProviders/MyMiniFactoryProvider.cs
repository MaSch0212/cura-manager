using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CuraManager.Models;
using CuraManager.Views;
using MaSch.Core.Extensions;

namespace CuraManager.Services.WebProviders
{
    public class MyMiniFactoryProvider : IWebProvider
    {
        private static readonly Regex IdRegex = new Regex(@"(?<id>[0-9]+)\/?\Z", RegexOptions.Compiled);
        private readonly WebClient _webClient = new WebClient();

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
                    await CreateProjectFromArchiveDialog.GetFilesToExtract(zipFile, printElement.DirectoryLocation)
                        .ForEachAsync(x => Task.Run(() => x.entry.ExtractToFile(x.targetPath)));
                }
            }
            else
            {
                File.Copy(zipFilePath, Path.Combine(printElement.DirectoryLocation, Path.GetFileName(fileUri.ToString())));
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

            var id = IdRegex.Match(webAddress.AbsoluteUri).Groups["id"].Value;
            builder.Path = $"/download/{id}";

            return builder.Uri;
        }
    }
}
