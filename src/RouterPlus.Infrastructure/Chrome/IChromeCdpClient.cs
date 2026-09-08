using System.Text.Json;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP call seam used by browser automation and OAuth page detectors.
/// </summary>
public interface IChromeCdpClient
{
    Task<JsonElement> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        string? sessionId = null);
}
