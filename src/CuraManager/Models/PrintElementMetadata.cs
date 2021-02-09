using System.Collections.Generic;

namespace CuraManager.Models
{
    public class PrintElementMetadata
    {
        public bool IsArchived { get; set; }
        public string Website { get; set; }
        public List<string> Tags { get; set; }

        public PrintElementMetadata()
        {
            Website = null;
            IsArchived = false;
            Tags = new List<string>();
        }
    }
}
