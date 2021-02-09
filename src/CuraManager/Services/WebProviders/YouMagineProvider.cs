using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CuraManager.Models;
using CuraManager.Views;
using MaSch.Core.Extensions;
using Newtonsoft.Json.Linq;

namespace CuraManager.Services.WebProviders
{
    public class YouMagineProvider : IWebProvider
    {
        private readonly WebClient _webClient = new WebClient();
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
            await _webClient.DownloadFileTaskAsync(fileUri, zipFilePath);

            if (Path.GetExtension(fileUri.ToString()).ToLower() == ".zip")
            {
                using (var zipFile = ZipFile.OpenRead(zipFilePath))
                {
                    await CreateProjectFromArchiveDialog.GetFilesToExtract(zipFile, printElement.DirectoryLocation)
                        .Where(x => !x.entry.Name.ToLower().In("license.html", "readme.pdf") && !x.entry.Name.ToLower().EndsWith("-attribution.pdf"))
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
            var json = await _httpClient.GetStringAsync(GetDownloadDataUri(webAddress));
            var jObject = JObject.Parse(json);
            return new Uri(jObject["url"].ToObject<string>());
        }

        private Uri GetDownloadDataUri(Uri webAddress)
        {
            var builder = new UriBuilder(webAddress);

            builder.Path += "/download";

            return builder.Uri;
        }
    }
}
