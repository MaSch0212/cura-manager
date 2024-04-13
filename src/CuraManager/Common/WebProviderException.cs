namespace CuraManager.Common;

public class WebProviderException : Exception
{
    public WebProviderException() { }

    public WebProviderException(string message)
        : base(message) { }

    public WebProviderException(string message, Exception innerException)
        : base(message, innerException) { }
}
