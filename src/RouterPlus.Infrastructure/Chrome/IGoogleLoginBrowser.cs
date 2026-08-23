using RouterPlus.Core.Security;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Testable browser interface for Google login automation.
/// </summary>
public interface IGoogleLoginBrowser : IAsyncDisposable
{
    /// <summary>
    /// Reads the current state of the Google login page.
    /// </summary>
    Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fills the specified field with a value.
    /// </summary>
    Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Submits the form containing the specified field.
    /// </summary>
    Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken);
}
