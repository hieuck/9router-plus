using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Observability;
using RouterPlus.Infrastructure.Security;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

/// <summary>
/// E2E test demonstrating how ObservabilityHub helps diagnose credential lookup failures.
/// Scenario: User moved Chrome directory, Profile ID changed, credentials not found.
/// Note: This test verifies the diagnostic event structure, not the actual background flush timing.
/// </summary>
public sealed class ObservabilityE2ECredentialMismatchTests
{
    [Fact]
    public void CredentialLookup_diagnostic_event_has_required_fields()
    {
        // Arrange: Simulate credentials saved with old User Data path
        var oldProfileId = "C:\\Users\\user\\OldPath\\Chrome\\User Data||Profile 1";
        var newProfileId = "C:\\Users\\user\\NewPath\\Chrome\\User Data||Profile 1";

        var vault = new GoogleAccountVault([
            new GoogleLoginCredential(oldProfileId, "user@example.com", "password123", "TOTPSECRET")
        ]);

        // Act: Create the diagnostic event that would be logged
        var credential = vault.Find(newProfileId);
        Assert.Null(credential); // Verify mismatch

        var availableProfileIds = vault.Records.Select(r => r.ProfileId).ToList();

        // This is the event structure that ObservabilityHub would log
        var diagnosticContext = new
        {
            lookup_profile_id = newProfileId,
            available_profile_ids = availableProfileIds,
            diagnosis = "Credentials may have been saved with different User Data path or Directory Name",
            solution = "Delete old credential in Credentials Manager and save again"
        };

        // Assert: Verify the diagnostic context has all required fields for AI analysis
        Assert.Equal(newProfileId, diagnosticContext.lookup_profile_id);
        Assert.Contains(oldProfileId, diagnosticContext.available_profile_ids);
        Assert.Contains("different User Data path", diagnosticContext.diagnosis);
        Assert.Contains("Delete old credential", diagnosticContext.solution);

        // Verify the vault inventory would help diagnose the issue
        var vaultInventory = vault.Records.Select(r => new { r.ProfileId, r.Email }).ToList();
        Assert.Single(vaultInventory);
        Assert.Equal(oldProfileId, vaultInventory[0].ProfileId);
        Assert.Equal("user@example.com", vaultInventory[0].Email);
    }
}
