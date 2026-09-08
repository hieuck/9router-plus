using System.Net;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Infrastructure.Tests;

public sealed class RouterApiExceptionTests
{
    [Fact]
    public void Constructor_preserves_message_and_status_code()
    {
        var exception = new RouterApiException("synthetic failure", HttpStatusCode.BadGateway);

        Assert.Equal("synthetic failure", exception.Message);
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public void Exception_can_be_handled_as_an_exception_with_status_context()
    {
        Exception exception = new RouterApiException("synthetic unauthorized", HttpStatusCode.Unauthorized);

        var routerException = Assert.IsType<RouterApiException>(exception);
        Assert.Equal(HttpStatusCode.Unauthorized, routerException.StatusCode);
    }
}
