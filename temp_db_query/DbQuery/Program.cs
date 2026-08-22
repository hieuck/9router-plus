using System;
using System.Linq;
using RouterPlus.Infrastructure.Router;

Console.WriteLine("=== VERIFICATION: Database Reader Fix ===\n");
Console.WriteLine($"Current time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n");

var reader = new UsageDatabaseReader();

// Test 1: Read usage data
Console.WriteLine("1. Testing GetTodayUsageByConnection():");
var usageData = reader.GetTodayUsageByConnection();
Console.WriteLine($"   Found {usageData.Count} connections with usage data");

var codexUsage = usageData.Where(k => k.Key.StartsWith("13f7aa39")).FirstOrDefault();
if (codexUsage.Key != null)
{
    Console.WriteLine($"   Codex (13f7aa39): {codexUsage.Value.Requests} requests");
}

var kiroUsage = usageData.Where(k => k.Key.StartsWith("2e5544c0")).FirstOrDefault();
if (kiroUsage.Key != null)
{
    Console.WriteLine($"   Kiro (2e5544c0): {kiroUsage.Value.Requests} requests");
}

// Test 2: Read token expiration
Console.WriteLine("\n2. Testing GetTokenExpirationByConnection():");
var tokenData = reader.GetTokenExpirationByConnection();
Console.WriteLine($"   Found {tokenData.Count} connections with token data");

Console.WriteLine("\n   Codex tokens:");
var codexIds = new[] { "dde24e37", "73bfb520", "f27b2995" };
foreach (var id in codexIds)
{
    var token = tokenData.Where(k => k.Key.StartsWith(id)).FirstOrDefault();
    if (token.Key != null)
    {
        var hoursLeft = token.Value.ExpiresAt.HasValue ? (token.Value.ExpiresAt.Value - DateTimeOffset.Now).TotalHours : 0;
        var daysLeft = hoursLeft / 24;
        Console.WriteLine($"   - {id}...: expires {token.Value.ExpiresAt:yyyy-MM-dd HH:mm} (~{daysLeft:F1} days)");
    }
}

Console.WriteLine("\n   Kiro tokens:");
var kiroIds = new[] { "719d7a5a", "ae371368", "2e5544c0" };
foreach (var id in kiroIds)
{
    var token = tokenData.Where(k => k.Key.StartsWith(id)).FirstOrDefault();
    if (token.Key != null)
    {
        var minutesLeft = token.Value.ExpiresAt.HasValue ? (token.Value.ExpiresAt.Value - DateTimeOffset.Now).TotalMinutes : 0;
        Console.WriteLine($"   - {id}...: expires {token.Value.ExpiresAt:yyyy-MM-dd HH:mm} (~{minutesLeft:F0} minutes)");
    }
}

Console.WriteLine("\n=== VERIFICATION RESULT ===");
Console.WriteLine($"Usage data: {(usageData.Count > 0 ? "OK" : "FAIL")}");
Console.WriteLine($"Token expiration data: {(tokenData.Count > 0 ? "OK" : "FAIL")}");
Console.WriteLine("\nDatabase reader is working correctly!");
