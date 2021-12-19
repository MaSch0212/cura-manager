using CuraManager.Models;
using Newtonsoft.Json;
using System.IO;

namespace CuraManager.Services;

public class CachingService : ICachingService
{
    public CachingService()
    {
    }

    public MetadataCache LoadCache(string printsPath)
    {
        MetadataCache result;
        var cacheFilePath = GetCacheFilePath(printsPath);

        if (!File.Exists(cacheFilePath))
            result = new MetadataCache();
        else
            result = JsonConvert.DeserializeObject<MetadataCache>(File.ReadAllText(cacheFilePath));
        result.PrintsPath = printsPath;

        return result;
    }

    public void UpdateCache(MetadataCache cache)
    {
        var cacheFilePath = GetCacheFilePath(cache.PrintsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath));
        File.WriteAllText(cacheFilePath, JsonConvert.SerializeObject(cache, Formatting.Indented));
    }

    private string GetCacheFilePath(string printsPath)
    {
        return Path.Combine(printsPath, "metadata-cache.json");
    }
}
