using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using CuraManager.Extensions;
using MaSch.Native.Windows.Explorer;

namespace CuraManager.Services
{
    public class FileIconCache : IFileIconCache
    {
        private readonly object _cacheLock = new object();
        private readonly IDictionary<string, ImageSource> _cache = new Dictionary<string, ImageSource>();

        public ImageSource GetFileIcon(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLower() ?? string.Empty;

            ImageSource result;
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(extension, out result))
                {
                    var icon = MaSch.Native.Windows.Explorer.FileInfo.GetIconFromFile(filePath, IconSize.Jumbo);
                    result = icon.ToImageSource();
                    _cache.Add(extension, result);
                }
            }

            return result;
        }
    }
}
