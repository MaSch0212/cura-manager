using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CuraManager.Common;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Views;
using MaSch.Core;
using MaSch.Core.Extensions;
using MaSch.Presentation.Translation;

namespace CuraManager.Services.WebProviders
{
    public class ThingiverseProvider : IWebProvider
    {
        private static readonly Regex LinkRegex = new Regex(@"https?:\/\/(www\.)?thingiverse\.com\/thing:[0-9]+", RegexOptions.Compiled);
        private readonly WebClient _webClient = new WebClient();

        public ICollection<string> SupportedHosts { get; } = new[] { "www.thingiverse.com" };

        public async Task<string> GetProjectName(Uri webAddress)
        {
            var request = WebRequest.Create(GetDownloadUrl(webAddress));
            request.Method = "HEAD";
            using (var response = await request.GetResponseAsync())
            {
                return Path.GetFileNameWithoutExtension(response.ResponseUri.ToString())
                    .Replace('_', ' ')
                    .Replace('+', ' ');
            }
        }

        public async Task DownloadFiles(Uri webAddress, PrintElement printElement)
        {
            var zipFilePath = Path.GetTempFileName();
            await _webClient.DownloadFileTaskAsync(GetDownloadUrl(webAddress), zipFilePath);

            using (var zipFile = ZipFile.OpenRead(zipFilePath))
            {
                await CreateProjectFromArchiveDialog.GetFilesToExtract(zipFile, printElement.DirectoryLocation)
                    .ForEachAsync(x => Task.Run(() => x.entry.ExtractToFile(x.targetPath)));
            }

            File.Delete(zipFilePath);
        }

        private Uri GetDownloadUrl(Uri webAddress)
        {
            var match = LinkRegex.Match(webAddress.AbsoluteUri);
            if (!match.Success)
                throw new WebProviderException(ServiceContext.GetService<ITranslationManager>().GetTranslation(nameof(StringTable.Msg_WrongThingiverseUrl)));

            var builder = new UriBuilder(match.Value);

            builder.Path += "/zip";

            return builder.Uri;
        }
    }
}
