using System.Collections.Generic;
using CuraManager.Models;

namespace CuraManager.Services;

public interface IPrintsService
{
    IEnumerable<PrintElement> GetPrintElements(MetadataCache cache);
    IEnumerable<PrintElement> GetNewPrintElements(MetadataCache cache, ICollection<PrintElement> elements);
}
