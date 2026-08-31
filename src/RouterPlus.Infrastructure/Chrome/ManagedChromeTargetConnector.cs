using System.Text.Json;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Connects to a managed Chrome page target and wraps its CDP client/session ownership.
/// Target policy remains with the caller through the selector callback.
/// </summary>
internal sealed class ManagedChromeTargetConnector
{
    private static readonly TimeSpan TargetWaitTimeout = TimeSpan.FromSeconds(30);
    private readonly Uri _devToolsBaseUri;

    internal ManagedChromeTargetConnector(Uri devToolsBaseUri)
    {
        ArgumentNullException.ThrowIfNull(devToolsBaseUri);
        _devToolsBaseUri = devToolsBaseUri;
    }

    internal async Task<CdpSession> ConnectAsync(
        Func<IReadOnlyList<ManagedChromeTarget>, string?> selectTarget,
        string noTargetMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(noTargetMessage);

        var client = new ChromeCdpClient(_devToolsBaseUri);
        await client.ConnectAsync(cancellationToken);

        try
        {
            var deadline = DateTimeOffset.UtcNow + TargetWaitTimeout;
            string? targetId = null;

            while (DateTimeOffset.UtcNow < deadline && targetId is null)
            {
                var response = await client.CallAsync("Target.getTargets", null, cancellationToken);
                targetId = selectTarget(ReadTargets(response));

                if (targetId is null)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            if (targetId is null)
            {
                throw new InvalidOperationException(noTargetMessage);
            }

            var attachResponse = await client.CallAsync(
                "Target.attachToTarget",
                new { targetId, flatten = true },
                cancellationToken);
            var sessionId = attachResponse.GetProperty("sessionId").GetString();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("CDP attach response did not contain a session identifier.");
            }

            await client.CallAsync("Page.bringToFront", null, cancellationToken, sessionId);
            return new CdpSession(client, sessionId, targetId);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    internal static string? SelectFirstPage(IReadOnlyList<ManagedChromeTarget> targets)
    {
        return targets.FirstOrDefault(target => target.Type == "page")?.TargetId;
    }

    internal static string? SelectMarkedGooglePage(
        IReadOnlyList<ManagedChromeTarget> targets,
        string sessionMarker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionMarker);

        var googleTargets = targets
            .Where(target => target.Type == "page" && IsAllowedGoogleHost(target.Url))
            .ToList();
        var markedTargets = googleTargets
            .Where(target => target.Url.Contains(sessionMarker, StringComparison.Ordinal))
            .ToList();

        if (markedTargets.Count > 1)
        {
            throw new InvalidOperationException(
                "Multiple Google targets with session marker found; exactly one is required.");
        }

        return markedTargets.SingleOrDefault()?.TargetId
            ?? (googleTargets.Count == 1 ? googleTargets[0].TargetId : null);
    }

    private static IReadOnlyList<ManagedChromeTarget> ReadTargets(JsonElement response)
    {
        var targetInfos = response.GetProperty("targetInfos");
        var targets = new List<ManagedChromeTarget>();
        foreach (var target in targetInfos.EnumerateArray())
        {
            var targetId = target.GetProperty("targetId").GetString();
            if (string.IsNullOrWhiteSpace(targetId))
            {
                continue;
            }

            targets.Add(new ManagedChromeTarget(
                targetId,
                target.GetProperty("type").GetString() ?? string.Empty,
                target.GetProperty("url").GetString() ?? string.Empty));
        }

        return targets;
    }

    private static bool IsAllowedGoogleHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Host == "accounts.google.com"
                || uri.Host == "myaccount.google.com"
                || uri.Host == "www.google.com");
    }
}

internal sealed record ManagedChromeTarget(string TargetId, string Type, string Url);
