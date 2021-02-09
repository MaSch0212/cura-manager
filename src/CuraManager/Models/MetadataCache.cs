using Newtonsoft.Json;
using System.Collections.Generic;

namespace CuraManager.Models
{
    public class MetadataCache
    {
        [JsonIgnore]
        public string PrintsPath { get; set; }

        public IDictionary<string, PrintElementMetadataCache> PrintElements { get; set; }

        public MetadataCache()
        {
            PrintElements = new Dictionary<string, PrintElementMetadataCache>();
        }
    }
}
