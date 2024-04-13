using CuraManager.Models;

namespace CuraManager.Services;

public interface ICachingService
{
    MetadataCache LoadCache(string printsPath);
    void UpdateCache(MetadataCache cache);
}
