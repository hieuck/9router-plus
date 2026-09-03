using System.Text.RegularExpressions;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// Smart suggestion logic for Google account selection in Codex/Provider tabs.
/// Suggests the matching Google account when Chrome profile name is an exact email match.
/// </summary>
public static class SmartGoogleAccountSuggestion
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Gets suggested Google accounts for a profile, with matching account at top if applicable.
    /// </summary>
    /// <param name="profileName">Chrome profile name (may be an email)</param>
    /// <param name="configuredAccounts">Available Google accounts from vault</param>
    /// <returns>Ordered list with suggestion at top (if match found), separator, then other accounts</returns>
    public static IEnumerable<GoogleAccountItem> GetSuggestedAccounts(
        string profileName,
        IEnumerable<GoogleAccountRowViewModel> configuredAccounts)
    {
        ArgumentNullException.ThrowIfNull(configuredAccounts);

        var accountsList = configuredAccounts.ToList();

        // Check if profile name is valid email format
        if (string.IsNullOrWhiteSpace(profileName) || !IsValidEmailFormat(profileName))
        {
            // No suggestion - return flat list of configured accounts
            return accountsList.Select(account => GoogleAccountItem.FromAccount(account, isSuggested: false));
        }

        // Profile name is valid email - always suggest it at top
        var matchedAccount = accountsList.FirstOrDefault(account =>
            string.Equals(account.Email, profileName, StringComparison.OrdinalIgnoreCase));

        var result = new List<GoogleAccountItem>();

        if (matchedAccount != null)
        {
            // Email exists in vault - use configured account
            result.Add(GoogleAccountItem.FromAccount(matchedAccount, isSuggested: true));
        }
        else
        {
            // Email not in vault - create suggestion placeholder
            result.Add(GoogleAccountItem.CreateSuggestion(profileName));
        }

        // Add separator and other accounts only if there are configured accounts
        if (accountsList.Count > 0)
        {
            // Only add separator if we have accounts to separate from
            if (matchedAccount == null || accountsList.Count > 1)
            {
                result.Add(GoogleAccountItem.CreateSeparator());
            }

            // Add all configured accounts except the matched one
            var otherAccounts = accountsList
                .Where(account => account != matchedAccount)
                .Select(account => GoogleAccountItem.FromAccount(account, isSuggested: false));

            result.AddRange(otherAccounts);
        }

        return result;
    }

    private static bool IsValidEmailFormat(string email)
    {
        try
        {
            return EmailRegex.IsMatch(email);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

/// <summary>
/// Represents a Google account item in the suggestion ComboBox.
/// Can be a regular account, a suggested account, or a separator.
/// </summary>
public sealed class GoogleAccountItem
{
    public string Email { get; }
    public bool IsSuggested { get; }
    public bool IsSeparator { get; }
    public GoogleAccountRowViewModel? SourceAccount { get; }

    private GoogleAccountItem(
        string email,
        bool isSuggested,
        bool isSeparator,
        GoogleAccountRowViewModel? sourceAccount)
    {
        Email = email;
        IsSuggested = isSuggested;
        IsSeparator = isSeparator;
        SourceAccount = sourceAccount;
    }

    public static GoogleAccountItem FromAccount(GoogleAccountRowViewModel account, bool isSuggested)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new GoogleAccountItem(account.Email, isSuggested, false, account);
    }

    public static GoogleAccountItem CreateSeparator()
    {
        return new GoogleAccountItem("---", false, true, null);
    }

    public static GoogleAccountItem CreateSuggestion(string email)
    {
        return new GoogleAccountItem(email, true, false, null);
    }

    /// <summary>
    /// Display text for ComboBox. Shows [Suggested] label for suggested items.
    /// </summary>
    public string DisplayText => IsSeparator
        ? "─────────────────────"
        : IsSuggested
            ? $"{Email}  [Suggested]"
            : Email;
}
