using System.Collections.Generic;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

/// <summary>
/// Integration tests using real error messages from 9Router backend.
/// These verify that UsageInferenceService correctly parses actual production errors.
/// </summary>
public sealed class UsageInferenceIntegrationTests
{
    [Fact]
    public void ParseConnection_infers_usage_from_codex_429_error()
    {
        var json = """
{
    "id": "13f7aa39-7dd2-4fc9-8f12-15bbae774103",
    "provider": "codex",
    "name": "demo.user1@example.com",
    "priority": 36,
    "isActive": false,
    "testStatus": "unavailable",
    "errorCode": 429,
    "lastError": "[429]: The usage limit has been reached",
    "lastErrorAt": "2026-08-22T06:32:40.306Z"
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("codex", connection.Provider.ToString().ToLowerInvariant());
        
        // Should have inferred usage data
        Assert.True(connection.HasUsageData);
        Assert.Equal(100, connection.UsageCount);
        Assert.Equal(100, connection.LimitCount);
        Assert.True(connection.IsOverLimit);
        Assert.NotNull(connection.UsageResetAt);
    }

    [Fact]
    public void ParseConnection_infers_usage_from_kiro_monthly_limit()
    {
        var json = """
{
    "id": "ae371368-9419-4c4c-8982-abd8af77bcac",
    "provider": "kiro",
    "name": "demo.user2@example.com",
    "priority": 1,
    "isActive": true,
    "testStatus": "unavailable",
    "errorCode": 402,
    "lastError": "[402]: {\"message\":\"You have reached the limit.\",\"reason\":\"MONTHLY_REQUEST_COUNT\"}",
    "lastErrorAt": "2026-08-22T08:00:00.000Z"
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("kiro", connection.Provider.ToString().ToLowerInvariant());
        Assert.True(connection.HasUsageData);
        Assert.Equal(100, connection.UsageCount);
        Assert.Equal(100, connection.LimitCount);
        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void ParseConnection_infers_usage_from_ollama_weekly_limit()
    {
        var json = """
{
    "id": "ee3eb532-033e-4838-a850-43af0156223f",
    "provider": "ollama",
    "name": "demo.user1@example.com",
    "priority": 35,
    "isActive": true,
    "testStatus": "unavailable",
    "errorCode": 429,
    "lastError": "[429]: {\"error\":\"you (synthetic-user-123) have reached your session usage limit, upgrade for higher limit\"}",
    "lastErrorAt": "2026-08-22T09:10:37.944Z"
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("ollama", connection.Provider.ToString().ToLowerInvariant());
        Assert.True(connection.HasUsageData);
        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void ParseConnection_infers_usage_from_openrouter_402_error()
    {
        var json = """
{
    "id": "c417d363-4a08-4e09-8182-2d7fd0b6ffa6",
    "provider": "openrouter",
    "name": "beekelly7996@yahoo.com.vn",
    "priority": 1,
    "isActive": true,
    "testStatus": "unavailable",
    "errorCode": 402,
    "lastError": "[402]: {\"error\":{\"message\":\"This request requires more credits, or fewer max_tokens.\"}}",
    "lastErrorAt": "2026-08-22T08:00:00.000Z"
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("openrouter", connection.Provider.ToString().ToLowerInvariant());
        Assert.True(connection.HasUsageData);
        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void ParseConnection_infers_usage_from_kimchi_credits_exhausted()
    {
        var json = """
{
    "id": "984fceab-2e3c-41c3-a907-e927af01cf6d",
    "provider": "kimchi",
    "name": "demo.user2@example.com",
    "priority": 1,
    "isActive": true,
    "testStatus": "unavailable",
    "errorCode": 402,
    "lastError": "[402]: {\"error\": \"the provider for model kimi-k2.7 has exhausted its credits and cannot process requ\"}",
    "lastErrorAt": "2026-08-22T08:00:00.000Z"
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("kimchi", connection.Provider.ToString().ToLowerInvariant());
        Assert.True(connection.HasUsageData);
        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void ParseConnection_does_not_infer_usage_for_active_connection()
    {
        var json = """
{
    "id": "a2ae2a28-a5a0-4660-aab7-0faa68a48724",
    "provider": "openrouter",
    "name": "demo.user1@example.com",
    "priority": 35,
    "isActive": true,
    "testStatus": "active",
    "lastError": null,
    "errorCode": null
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("openrouter", connection.Provider.ToString().ToLowerInvariant());
        
        // Should NOT have usage data (no error)
        Assert.False(connection.HasUsageData);
    }

    [Fact]
    public void ParseConnection_does_not_infer_usage_for_non_limit_errors()
    {
        var json = """
{
    "id": "179e503f-5a9f-48c3-8afd-904c11b9818a",
    "provider": "kiro",
    "name": "demo.user3@example.com",
    "priority": 1,
    "isActive": true,
    "testStatus": "unavailable",
    "errorCode": 400,
    "lastError": "[400]: {\"message\":\"Input is too long.\",\"reason\":\"CONTENT_LENGTH_EXCEEDS_THRESHOLD\"}",
    "lastErrorAt": "2026-08-22T08:00:00.000Z"
}
""";

        var element = JsonDocument.Parse(json).RootElement;
        var connection = ParseConnectionViaReflection(element);

        Assert.NotNull(connection);
        Assert.Equal("kiro", connection.Provider.ToString().ToLowerInvariant());
        
        // Should NOT have usage data (error is not about limits)
        Assert.False(connection.HasUsageData);
    }

    private static ProviderConnection? ParseConnectionViaReflection(JsonElement element)
    {
        // Use reflection to call private ParseConnection method
        var method = typeof(RouterApiClient).GetMethod(
            "ParseConnection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        return method.Invoke(null, new object[] { element, new Dictionary<string, UsageData>(), new Dictionary<string, TokenExpirationData>() }) as ProviderConnection;
    }
}
