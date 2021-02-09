using System;
using System.Runtime.Serialization;

namespace CuraManager.Common
{
    public class WebProviderException : Exception
    {
        public WebProviderException()
        {
        }

        public WebProviderException(string message)
            : base(message)
        {
        }

        public WebProviderException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected WebProviderException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
