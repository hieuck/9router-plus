using System.Text.Json;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP call seam used by browser automation.
/// </summary>
public interface IChromeCdpClient
{
    Task<JsonElement> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        string? sessionId = null);
}
