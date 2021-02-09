using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CuraManager.Models;

namespace CuraManager.Services
{
    public class PrintsService : IPrintsService
    {
        public PrintsService()
        {
        }

        public IEnumerable<PrintElement> GetPrintElements(MetadataCache cache)
        {
            return GetNewPrintElements(cache, new List<PrintElement>());
        }

        public IEnumerable<PrintElement> GetNewPrintElements(MetadataCache cache, ICollection<PrintElement> elements)
        {
            var printsPath = cache.PrintsPath;
            var directories = Directory.EnumerateDirectories(printsPath, "*", SearchOption.TopDirectoryOnly);

            foreach (var dir in directories)
            {
                if (!elements.Any(y => string.Equals(y.DirectoryLocation, dir, StringComparison.OrdinalIgnoreCase)))
                {
                    var element = new PrintElement(dir);
                    if (cache.PrintElements.TryGetValue(element.Name, out var elementCache))
                    {
                        element.Metadata = new PrintElementMetadata
                        {
                            IsArchived = elementCache.IsArchived,
                            Tags = elementCache.Tags?.ToList() ?? new List<string>(),
                        };
                    }
                    else
                    {
                        element.InitializeMetadata();
                    }

                    yield return element;
                }
            }
        }
    }
}
