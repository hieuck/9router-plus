using System.Net;

namespace RouterPlus.Infrastructure.Router;

public sealed class RouterApiException : Exception
{
    public RouterApiException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
