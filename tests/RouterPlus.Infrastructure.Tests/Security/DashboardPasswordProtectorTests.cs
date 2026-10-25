using System;
using System.Runtime.InteropServices;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

public sealed class DashboardPasswordProtectorTests
{
    [Fact]
    public void IsProtected_ReturnsFalse_For_Plaintext()
    {
        Assert.False(DashboardPasswordProtector.IsProtected("plain-password"));
        Assert.False(DashboardPasswordProtector.IsProtected(null));
        Assert.False(DashboardPasswordProtector.IsProtected(string.Empty));
    }

    [Fact]
    public void RoundTrip_ProtectsAndUnprotects()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        const string secret = "synthetic-password";
        var protectedValue = DashboardPasswordProtector.Protect(secret);

        Assert.True(DashboardPasswordProtector.IsProtected(protectedValue));
        Assert.StartsWith("dpapi:v1:", protectedValue, StringComparison.Ordinal);
        Assert.True(DashboardPasswordProtector.TryUnprotect(protectedValue, out var plaintext));
        Assert.Equal(secret, plaintext);
    }

    [Fact]
    public void TryUnprotect_ReturnsFalse_For_CorruptCiphertext()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        Assert.False(DashboardPasswordProtector.TryUnprotect("dpapi:v1:not-base64!!!", out _));
        Assert.False(DashboardPasswordProtector.TryUnprotect("dpapi:v1:" + Convert.ToBase64String(new byte[] { 1, 2, 3 }), out _));
    }
}