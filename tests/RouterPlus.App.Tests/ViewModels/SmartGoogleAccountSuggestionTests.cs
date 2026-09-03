using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// Tests for smart Google account suggestion in Codex/Provider tabs.
/// Feature: When editing Codex/Provider connection with Google OAuth method,
/// suggest the matching Google account if profile name is an exact email match.
/// </summary>
public sealed class SmartGoogleAccountSuggestionTests
{
    [Fact]
    public void GetSuggestedGoogleAccounts_ProfileNameIsExactEmailMatch_ReturnsSuggestionFirst()
    {
        // Arrange
        var profileName = "work@company.com";
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("personal@gmail.com"),
            CreateGoogleAccount("work@company.com"),
            CreateGoogleAccount("dev@company.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        Assert.Collection(result,
            first =>
            {
                Assert.Equal("work@company.com", first.Email);
                Assert.True(first.IsSuggested);
            },
            separator =>
            {
                Assert.True(separator.IsSeparator);
            },
            second =>
            {
                Assert.Equal("personal@gmail.com", second.Email);
                Assert.False(second.IsSuggested);
            },
            third =>
            {
                Assert.Equal("dev@company.com", third.Email);
                Assert.False(third.IsSuggested);
            });
    }

    [Fact]
    public void GetSuggestedGoogleAccounts_ProfileNameNotEmailFormat_ReturnsAllAccountsWithoutSuggestion()
    {
        // Arrange
        var profileName = "Work Profile";
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("work@company.com"),
            CreateGoogleAccount("personal@gmail.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        Assert.Collection(result,
            first =>
            {
                Assert.Equal("work@company.com", first.Email);
                Assert.False(first.IsSuggested);
            },
            second =>
            {
                Assert.Equal("personal@gmail.com", second.Email);
                Assert.False(second.IsSuggested);
            });

        // No separator should exist
        Assert.DoesNotContain(result, item => item.IsSeparator);
    }

    [Fact]
    public void GetSuggestedGoogleAccounts_ProfileEmailNotInVault_ReturnsProfileEmailAsSuggestionBeforeAvailableAccounts()
    {
        // Arrange
        var profileName = "notfound@nowhere.com";
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("work@company.com"),
            CreateGoogleAccount("personal@gmail.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        Assert.Collection(result,
            suggested =>
            {
                Assert.Equal("notfound@nowhere.com", suggested.Email);
                Assert.True(suggested.IsSuggested);
                Assert.Null(suggested.SourceAccount);
            },
            separator => Assert.True(separator.IsSeparator),
            firstAvailable => Assert.Equal("work@company.com", firstAvailable.Email),
            secondAvailable => Assert.Equal("personal@gmail.com", secondAvailable.Email));
    }

    [Fact]
    public void GetSuggestedGoogleAccounts_EmailMatchingIsCaseInsensitive()
    {
        // Arrange
        var profileName = "WORK@COMPANY.COM";
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("work@company.com"),
            CreateGoogleAccount("personal@gmail.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        var suggested = result.First();
        Assert.Equal("work@company.com", suggested.Email);
        Assert.True(suggested.IsSuggested);
    }

    [Fact]
    public void GetSuggestedGoogleAccounts_EmptyProfileName_ReturnsAllAccountsWithoutSuggestion()
    {
        // Arrange
        var profileName = "";
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("work@company.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        Assert.Single(result);
        Assert.False(result.First().IsSuggested);
    }

    [Fact]
    public void GetSuggestedGoogleAccounts_NoConfiguredAccounts_ReturnsProfileEmailSuggestionWithoutSeparator()
    {
        // Arrange
        var profileName = "work@company.com";
        var configuredAccounts = Array.Empty<GoogleAccountRowViewModel>();

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        var suggestion = Assert.Single(result);
        Assert.Equal(profileName, suggestion.Email);
        Assert.True(suggestion.IsSuggested);
        Assert.Null(suggestion.SourceAccount);
        Assert.False(suggestion.IsSeparator);
    }

    [Fact]
    public void GetSuggestedGoogleAccounts_OnlyOneSuggestedAccount_NoSeparatorOrOtherAccounts()
    {
        // Arrange
        var profileName = "work@company.com";
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("work@company.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        Assert.Single(result);
        Assert.True(result.First().IsSuggested);
        Assert.DoesNotContain(result, item => item.IsSeparator);
    }

    [Theory]
    [InlineData("user@domain")]        // Missing TLD
    [InlineData("@domain.com")]        // Missing username
    [InlineData("user@.com")]          // Missing domain
    [InlineData("user.domain.com")]    // Missing @
    [InlineData("user @domain.com")]   // Space in username
    public void GetSuggestedGoogleAccounts_InvalidEmailFormats_NoSuggestion(string invalidEmail)
    {
        // Arrange
        var profileName = invalidEmail;
        var configuredAccounts = new[]
        {
            CreateGoogleAccount("valid@email.com")
        };

        // Act
        var result = SmartGoogleAccountSuggestion.GetSuggestedAccounts(
            profileName,
            configuredAccounts);

        // Assert
        Assert.DoesNotContain(result, item => item.IsSuggested);
    }

    [Fact]
    public void CodexConnectionRow_SuggestedGoogleAccounts_UsesExactProfileEmailBeforeAvailableAccounts()
    {
        // Arrange
        var row = new CodexConnectionRowViewModel
        {
            ProfileName = "work@company.com"
        };
        var availableAccount = CreateGoogleAccount("personal@gmail.com");

        // Act
        row.SetConfiguredGoogleAccounts(new[] { availableAccount });
        var result = row.SuggestedGoogleAccounts.ToList();

        // Assert
        Assert.Collection(result,
            suggested =>
            {
                Assert.Equal("work@company.com", suggested.Email);
                Assert.True(suggested.IsSuggested);
                Assert.Null(suggested.SourceAccount);
            },
            separator => Assert.True(separator.IsSeparator),
            available =>
            {
                Assert.Equal("personal@gmail.com", available.Email);
                Assert.False(available.IsSuggested);
                Assert.Same(availableAccount, available.SourceAccount);
            });
    }

    [Fact]
    public void CodexConnectionRow_SuggestedGoogleAccounts_RefreshesWhenProfileNameChanges()
    {
        // Arrange
        var row = new CodexConnectionRowViewModel
        {
            ProfileName = "work@company.com"
        };
        row.SetConfiguredGoogleAccounts(new[] { CreateGoogleAccount("personal@gmail.com") });

        // Act
        row.ProfileName = "Work Profile";
        var result = row.SuggestedGoogleAccounts.ToList();

        // Assert
        Assert.Equal(new[] { "personal@gmail.com" }, result.Select(item => item.Email));
        Assert.DoesNotContain(result, item => item.IsSuggested || item.IsSeparator);
    }

    [Fact]
    public void GoogleAccountItem_IsSeparator_HasCorrectProperties()
    {
        // Arrange & Act
        var separator = GoogleAccountItem.CreateSeparator();

        // Assert
        Assert.True(separator.IsSeparator);
        Assert.False(separator.IsSuggested);
        Assert.Equal("---", separator.Email);
        Assert.Null(separator.SourceAccount);
    }

    [Fact]
    public void GoogleAccountItem_FromAccount_HasCorrectProperties()
    {
        // Arrange
        var account = CreateGoogleAccount("test@example.com");

        // Act
        var item = GoogleAccountItem.FromAccount(account, isSuggested: true);

        // Assert
        Assert.False(item.IsSeparator);
        Assert.True(item.IsSuggested);
        Assert.Equal("test@example.com", item.Email);
        Assert.Same(account, item.SourceAccount);
    }

    // Helper method
    private static GoogleAccountRowViewModel CreateGoogleAccount(string email)
    {
        return new GoogleAccountRowViewModel
        {
            ProfileName = "Test Profile",
            Email = email,
            Password = "test-password",
            HasCredentials = true
        };
    }
}
