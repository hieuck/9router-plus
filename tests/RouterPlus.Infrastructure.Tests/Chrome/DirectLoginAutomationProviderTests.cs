using System.Reflection;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class DirectLoginAutomationProviderTests
{
    public static TheoryData<Type> ProviderTypes => new()
    {
        typeof(CodexDirectLoginAutomation),
        typeof(GitHubDirectLoginAutomation),
        typeof(KiroDirectLoginAutomation),
        typeof(OpenRouterDirectLoginAutomation)
    };

    [Theory]
    [MemberData(nameof(ProviderTypes))]
    public async Task IsLoginCompleteAsync_ReturnsCdpBoolean(Type providerType)
    {
        // Arrange
        var client = new FakeCdpClient { Response = Result(true) };
        var automation = CreateAutomation(providerType, client);

        // Act
        var result = await InvokeCompletionAsync(automation);

        // Assert
        Assert.True(result);
        Assert.Single(client.Calls);
        Assert.Equal("Runtime.evaluate", client.Calls[0]);
    }

    [Theory]
    [MemberData(nameof(ProviderTypes))]
    public async Task IsLoginCompleteAsync_ReturnsFalseWhenCdpHasNoValue(Type providerType)
    {
        // Arrange
        var client = new FakeCdpClient { Response = ResultWithoutValue() };
        var automation = CreateAutomation(providerType, client);

        // Act
        var result = await InvokeCompletionAsync(automation);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [MemberData(nameof(ProviderTypes))]
    public async Task IsLoginCompleteAsync_ReturnsFalseWhenCdpFails(Type providerType)
    {
        // Arrange
        var client = new FakeCdpClient { Exception = new InvalidOperationException("synthetic CDP failure") };
        var automation = CreateAutomation(providerType, client);

        // Act
        var result = await InvokeCompletionAsync(automation);

        // Assert
        Assert.False(result);
    }

    private static DirectLoginAutomation CreateAutomation(Type providerType, IChromeCdpClient client) =>
        (DirectLoginAutomation)Activator.CreateInstance(
            providerType,
            client,
            "session",
            "target",
            "user@example.test",
            "synthetic-password",
            null)!;

    private static async Task<bool> InvokeCompletionAsync(DirectLoginAutomation automation)
    {
        var method = automation.GetType().GetMethod(
            "IsLoginCompleteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<bool>)method.Invoke(automation, [CancellationToken.None])!;
        return await task;
    }

    private static JsonElement Result(bool value) => JsonSerializer.SerializeToDocument(new { result = new { value } }).RootElement.Clone();

    private static JsonElement ResultWithoutValue() => JsonSerializer.SerializeToDocument(new { result = new { } }).RootElement.Clone();

    private sealed class FakeCdpClient : IChromeCdpClient
    {
        public JsonElement Response { get; init; }
        public Exception? Exception { get; init; }
        public List<string> Calls { get; } = [];

        public Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken, string? sessionId = null)
        {
            Calls.Add(method);
            if (Exception is not null)
                return Task.FromException<JsonElement>(Exception);
            return Task.FromResult(Response);
        }
    }
}
