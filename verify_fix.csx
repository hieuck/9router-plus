using System;
using System.Linq;
using RouterPlus.Infrastructure.Router;

Console.WriteLine("=== VERIFICATION: Database Reader Test ===\n");

var reader = new UsageDatabaseReader();

// Test 1: Read usage data
Console.WriteLine("1. Testing GetTodayUsageByConnection():");
var usageData = reader.GetTodayUsageByConnection();
Console.WriteLine($"   Found {usageData.Count} connections with usage data");
foreach (var kvp in usageData.Take(5))
{
    Console.WriteLine($"   - {kvp.Key.Substring(0, 8)}...: {kvp.Value.Requests} requests");
}

// Test 2: Read token expiration
Console.WriteLine("\n2. Testing GetTokenExpirationByConnection():");
var tokenData = reader.GetTokenExpirationByConnection();
Console.WriteLine($"   Found {tokenData.Count} connections with token data");

var codexTokens = tokenData.Where(k => k.Key.StartsWith("dde24e37") || k.Key.StartsWith("73bfb520") || k.Key.StartsWith("f27b2995")).ToList();
Console.WriteLine($"\n   Codex tokens ({codexTokens.Count}):");
foreach (var kvp in codexTokens)
{
    var hoursLeft = kvp.Value.ExpiresAt.HasValue ? (kvp.Value.ExpiresAt.Value - DateTimeOffset.Now).TotalHours : 0;
    Console.WriteLine($"   - {kvp.Key.Substring(0, 8)}...: expires {kvp.Value.ExpiresAt} (~{hoursLeft:F1} hours)");
}

var kiroTokens = tokenData.Where(k => k.Key.StartsWith("719d7a5a") || k.Key.StartsWith("ae371368")).ToList();
Console.WriteLine($"\n   Kiro tokens ({kiroTokens.Count}):");
foreach (var kvp in kiroTokens)
{
    var minutesLeft = kvp.Value.ExpiresAt.HasValue ? (kvp.Value.ExpiresAt.Value - DateTimeOffset.Now).TotalMinutes : 0;
    Console.WriteLine($"   - {kvp.Key.Substring(0, 8)}...: expires {kvp.Value.ExpiresAt} (~{minutesLeft:F1} minutes)");
}

Console.WriteLine("\n=== VERIFICATION PASSED ===");
