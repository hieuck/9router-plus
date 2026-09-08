using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class DirectLoginAutomationTests
{
    [Fact]
    public async Task RunAsync_CompletesAfterFillingCredentialsWithoutTotp()
    {
        // Arrange
        var client = new FakeCdpClient
        {
            Responses =
            [
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(false),
                Result(true)
            ]
        };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Login completed", result.Message);
        Assert.Equal(6, client.Calls.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2)], automation.Delays);
        Assert.Equal(["FillEmail", "FillPassword"], automation.FilledActions);
    }

    [Fact]
    public async Task RunAsync_ClicksLoginButtonBeforeWaitingForEmail()
    {
        // Arrange
        var client = new FakeCdpClient
        {
            Responses =
            [
                Result(false),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(false),
                Result(true)
            ]
        };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(7, client.Calls.Count);
        Assert.Equal("TryClickLoginButton", client.Calls[1].Operation);
    }

    [Fact]
    public async Task RunAsync_RetriesAfterLoginButtonAndEmailLookupFail()
    {
        // Arrange
        var client = new FakeCdpClient
        {
            Responses =
            [
                Result(false),
                Result(false),
                ResultString("https://example.test/login"),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(false)
            ]
        };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password")
        {
            SelectorResults = [false, true]
        };

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("GetCurrentUrl", client.Calls[2].Operation);
        Assert.Contains(TimeSpan.FromMilliseconds(500), automation.Delays);
    }

    [Fact]
    public async Task RunAsync_FillsAndSubmitsTotpOnce_WhenChallengeHasCode()
    {
        // Arrange
        var client = new FakeCdpClient
        {
            Responses =
            [
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true)
            ]
        };
        var totpCalls = 0;
        var automation = new ProbeDirectLoginAutomation(
            client,
            "session",
            "target",
            "user@example.test",
            "synthetic-password",
            () =>
            {
                totpCalls++;
                return Task.FromResult<string?>("123456");
            });

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, totpCalls);
        Assert.Equal(["FillEmail", "FillPassword", "FillTotp"], automation.FilledActions);
        Assert.Equal(8, client.Calls.Count);
    }

    [Fact]
    public async Task RunAsync_DoesNotFillTotp_WhenGeneratorReturnsBlankCode()
    {
        // Arrange
        var client = new FakeCdpClient
        {
            Responses =
            [
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true)
            ]
        };
        var automation = new ProbeDirectLoginAutomation(
            client,
            "session",
            "target",
            "user@example.test",
            "synthetic-password",
            () => Task.FromResult<string?>(" "));

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(["FillEmail", "FillPassword"], automation.FilledActions);
        Assert.Equal(6, client.Calls.Count);
    }

    [Fact]
    public async Task RunAsync_ReturnsTimeoutWithoutCallingCdp_WhenDeadlineAlreadyPassed()
    {
        // Arrange
        var client = new FakeCdpClient();
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.RunAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Timeout waiting for login completion", result.Message);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task IsElementVisibleAsync_ReturnsFalseForMissingValueAndCdpFailure()
    {
        // Arrange
        var noValueClient = new FakeCdpClient { Responses = [ResultWithoutValue()] };
        var failingClient = new FakeCdpClient { Exceptions = [new InvalidOperationException("synthetic CDP failure")] };
        var noValueAutomation = new ProbeDirectLoginAutomation(noValueClient, "session", "target", "user@example.test", "synthetic-password");
        var failingAutomation = new ProbeDirectLoginAutomation(failingClient, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var noValueResult = await noValueAutomation.IsVisibleAsync("input[name='email']", CancellationToken.None);
        var failureResult = await failingAutomation.IsVisibleAsync("input[name='email']", CancellationToken.None);

        // Assert
        Assert.False(noValueResult);
        Assert.False(failureResult);
    }

    [Fact]
    public async Task HelperMethods_ReturnFalseOrUnknownWhenCdpResultIsMissing()
    {
        // Arrange
        var visibleAutomation = new ProbeDirectLoginAutomation(new FakeCdpClient { Responses = [EmptyResult()] }, "session", "target", "user@example.test", "synthetic-password");
        var urlAutomation = new ProbeDirectLoginAutomation(new FakeCdpClient { Responses = [EmptyResult()] }, "session", "target", "user@example.test", "synthetic-password");
        var buttonAutomation = new ProbeDirectLoginAutomation(new FakeCdpClient { Responses = [EmptyResult()] }, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var visibleResult = await visibleAutomation.IsVisibleAsync("input[name='email']", CancellationToken.None);
        var urlResult = await urlAutomation.CurrentUrlAsync(CancellationToken.None);
        var buttonResult = await buttonAutomation.TryClickAsync(CancellationToken.None);

        // Assert
        Assert.False(visibleResult);
        Assert.Equal("unknown", urlResult);
        Assert.False(buttonResult);
    }

    [Fact]
    public async Task WaitForSelectorAsync_RetriesAfterInvisibleResultWithoutSleeping()
    {
        // Arrange
        var client = new FakeCdpClient { Responses = [Result(false), Result(true)] };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.WaitForSelectorPublicAsync("input[name='email']", CancellationToken.None, timeoutMs: 1000);

        // Assert
        Assert.True(result);
        Assert.Equal(2, client.Calls.Count);
        Assert.Contains(TimeSpan.FromMilliseconds(200), automation.Delays);
    }

    [Fact]
    public async Task FillInputAsync_ThrowsWhenCdpReturnsExceptionDetails()
    {
        // Arrange
        var client = new FakeCdpClient { Responses = [ResultWithExceptionDetails()] };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var action = () => automation.FillAsync("input[name='email']", "user@example.test", CancellationToken.None);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal("Failed to fill input with selector: input[name='email']", exception.Message);
    }

    [Fact]
    public async Task ClickAsync_ThrowsWhenCdpReturnsExceptionDetails()
    {
        // Arrange
        var client = new FakeCdpClient { Responses = [ResultWithExceptionDetails()] };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var action = () => automation.ClickAsync("button[type='submit']", CancellationToken.None);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal("Failed to click element with selector: button[type='submit']", exception.Message);
    }

    [Fact]
    public async Task GetCurrentUrlAsync_ReturnsUnknownForMissingValueAndErrorForCdpFailure()
    {
        // Arrange
        var noValueClient = new FakeCdpClient { Responses = [ResultWithoutValue()] };
        var failingClient = new FakeCdpClient { Exceptions = [new InvalidOperationException("synthetic CDP failure")] };
        var noValueAutomation = new ProbeDirectLoginAutomation(noValueClient, "session", "target", "user@example.test", "synthetic-password");
        var failingAutomation = new ProbeDirectLoginAutomation(failingClient, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var noValueResult = await noValueAutomation.CurrentUrlAsync(CancellationToken.None);
        var failureResult = await failingAutomation.CurrentUrlAsync(CancellationToken.None);

        // Assert
        Assert.Equal("unknown", noValueResult);
        Assert.Equal("error", failureResult);
    }

    [Fact]
    public async Task TryClickLoginButtonAsync_ReturnsFalseForMissingValueAndCdpFailure()
    {
        // Arrange
        var noValueClient = new FakeCdpClient { Responses = [ResultWithoutValue()] };
        var failingClient = new FakeCdpClient { Exceptions = [new InvalidOperationException("synthetic CDP failure")] };
        var noValueAutomation = new ProbeDirectLoginAutomation(noValueClient, "session", "target", "user@example.test", "synthetic-password");
        var failingAutomation = new ProbeDirectLoginAutomation(failingClient, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var noValueResult = await noValueAutomation.TryClickAsync(CancellationToken.None);
        var failureResult = await failingAutomation.TryClickAsync(CancellationToken.None);

        // Assert
        Assert.False(noValueResult);
        Assert.False(failureResult);
    }

    [Fact]
    public async Task FillTotpAsync_ThrowsWhenProviderHasNoTotpSelector()
    {
        // Arrange
        var automation = new NoTotpDirectLoginAutomation(
            new FakeCdpClient(),
            "session",
            "target",
            "user@example.test",
            "synthetic-password");

        // Act
        var action = () => automation.FillTotpPublicAsync("123456", CancellationToken.None);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal("TOTP selector not defined for this provider", exception.Message);
    }

    [Fact]
    public async Task IsTotpRequiredAsync_ReturnsFalseWhenProviderHasNoTotpSelector()
    {
        // Arrange
        var automation = new NoTotpDirectLoginAutomation(
            new FakeCdpClient(),
            "session",
            "target",
            "user@example.test",
            "synthetic-password");

        // Act
        var result = await automation.IsTotpRequiredPublicAsync(CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task WaitForSelectorAsync_ReturnsFalseWhenTimeoutHasElapsed()
    {
        // Arrange
        var client = new FakeCdpClient();
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.WaitForSelectorPublicAsync("input[name='email']", CancellationToken.None, timeoutMs: 0);

        // Assert
        Assert.False(result);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task WaitForSelectorAsync_ReturnsTrueWhenElementIsVisible()
    {
        // Arrange
        var client = new FakeCdpClient { Responses = [Result(true)] };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.WaitForSelectorPublicAsync("input[name='email']", CancellationToken.None, timeoutMs: 1000);

        // Assert
        Assert.True(result);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task GetCurrentUrlAsync_ReturnsUnknownWhenCdpValueIsNull()
    {
        // Arrange
        var client = new FakeCdpClient { Responses = [ResultWithNullValue()] };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.CurrentUrlAsync(CancellationToken.None);

        // Assert
        Assert.Equal("unknown", result);
    }

    [Fact]
    public async Task RunAsync_ReturnsCancellationWhenTokenIsAlreadyCanceled()
    {
        // Arrange
        var client = new FakeCdpClient();
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () => automation.RunAsync(TimeSpan.FromSeconds(1), cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task RunAsync_SkipsTotpWhenNoGeneratorIsConfigured()
    {
        // Arrange
        var client = new FakeCdpClient
        {
            Responses =
            [
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true),
                Result(true)
            ]
        };
        var automation = new ProbeDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password");

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(["FillEmail", "FillPassword"], automation.FilledActions);
        Assert.Equal(6, client.Calls.Count);
    }

    private static JsonElement Result(bool value) => JsonSerializer.SerializeToDocument(new { result = new { value } }).RootElement.Clone();

    private static JsonElement ResultString(string value) => JsonSerializer.SerializeToDocument(new { result = new { value } }).RootElement.Clone();

    private static JsonElement ResultWithoutValue() => JsonSerializer.SerializeToDocument(new { result = new { } }).RootElement.Clone();

    private static JsonElement EmptyResult() => JsonSerializer.SerializeToDocument(new { }).RootElement.Clone();

    private static JsonElement ResultWithNullValue() => JsonSerializer.SerializeToDocument(new { result = new { value = (string?)null } }).RootElement.Clone();

    private static JsonElement ResultWithExceptionDetails() => JsonSerializer.SerializeToDocument(new { exceptionDetails = new { text = "synthetic error" } }).RootElement.Clone();

    private sealed record CdpCall(string Operation, string? Selector, string? Value);

    private sealed class FakeCdpClient : IChromeCdpClient
    {
        public List<JsonElement> Responses { get; init; } = [];
        public List<Exception> Exceptions { get; init; } = [];
        public List<CdpCall> Calls { get; } = [];

        public Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken, string? sessionId = null)
        {
            var expression = parameters?.GetType().GetProperty("expression")?.GetValue(parameters)?.ToString();
            var selector = parameters?.GetType().GetProperty("selector")?.GetValue(parameters)?.ToString();
            var operation = expression switch
            {
                not null when expression.Contains("loginButton", StringComparison.Ordinal) => "TryClickLoginButton",
                not null when expression.Contains("window.location.href", StringComparison.Ordinal) => "GetCurrentUrl",
                not null when expression.Contains("element.value", StringComparison.Ordinal) => "FillInput",
                not null when expression.Contains("element.click", StringComparison.Ordinal) => "Click",
                not null when expression.Contains("querySelectorAll", StringComparison.Ordinal) => "IsElementVisible",
                _ => method
            };
            Calls.Add(new CdpCall(operation, selector, null));

            if (Exceptions.Count > 0)
                return Task.FromException<JsonElement>(Exceptions[0]);
            if (Responses.Count == 0)
                throw new InvalidOperationException("No fake CDP response configured.");

            var response = Responses[0];
            Responses.RemoveAt(0);
            return Task.FromResult(response);
        }
    }

    private class ProbeDirectLoginAutomation : DirectLoginAutomation
    {
        public ProbeDirectLoginAutomation(
            IChromeCdpClient client,
            string sessionId,
            string targetId,
            string email,
            string password,
            Func<Task<string?>>? totpGenerator = null)
            : base(client, sessionId, targetId, email, password, totpGenerator)
        {
        }

        public List<string> FilledActions { get; } = [];
        public List<TimeSpan> Delays { get; } = [];
        public List<bool> SelectorResults { get; init; } = [];

        public Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken) => IsElementVisibleAsync(selector, cancellationToken);
        public Task<bool> IsTotpRequiredPublicAsync(CancellationToken cancellationToken) => IsTotpRequiredAsync(cancellationToken);
        public Task<bool> WaitForSelectorPublicAsync(string selector, CancellationToken cancellationToken, int timeoutMs) => WaitForSelectorAsync(selector, cancellationToken, timeoutMs);
        public Task FillAsync(string selector, string value, CancellationToken cancellationToken) => FillInputAsync(selector, value, cancellationToken);
        public new Task ClickAsync(string selector, CancellationToken cancellationToken) => base.ClickAsync(selector, cancellationToken);
        public Task<string> CurrentUrlAsync(CancellationToken cancellationToken) => GetCurrentUrlAsync(cancellationToken);
        public Task<bool> TryClickAsync(CancellationToken cancellationToken) => TryClickLoginButtonAsync(cancellationToken);

        protected override string GetEmailSelector() => "input[name='email']";
        protected override string GetPasswordSelector() => "input[name='password']";
        protected override string? GetTotpSelector() => "input[name='otp']";
        protected override string GetSubmitSelector() => "button[type='submit']";

        protected override Task<bool> IsLoginCompleteAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        protected override async Task<bool> WaitForSelectorAsync(string selector, CancellationToken cancellationToken, int timeoutMs = 5000)
        {
            if (SelectorResults.Count == 0)
                return await base.WaitForSelectorAsync(selector, cancellationToken, timeoutMs);

            var result = SelectorResults[0];
            SelectorResults.RemoveAt(0);
            return result;
        }

        protected override async Task FillEmailAsync(CancellationToken cancellationToken)
        {
            FilledActions.Add("FillEmail");
            await base.FillEmailAsync(cancellationToken);
        }

        protected override async Task FillPasswordAsync(CancellationToken cancellationToken)
        {
            FilledActions.Add("FillPassword");
            await base.FillPasswordAsync(cancellationToken);
        }

        protected override async Task FillTotpAsync(string totpCode, CancellationToken cancellationToken)
        {
            FilledActions.Add("FillTotp");
            await base.FillTotpAsync(totpCode, cancellationToken);
        }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class NoTotpDirectLoginAutomation : ProbeDirectLoginAutomation
    {
        public NoTotpDirectLoginAutomation(IChromeCdpClient client, string sessionId, string targetId, string email, string password)
            : base(client, sessionId, targetId, email, password)
        {
        }

        public Task FillTotpPublicAsync(string code, CancellationToken cancellationToken) => FillTotpAsync(code, cancellationToken);

        protected override string? GetTotpSelector() => null;
    }
}
