using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace RouterPlus.Infrastructure.Router;

/// <summary>
/// Reads usage quota data from 9Router's local SQLite database.
/// </summary>
public sealed class UsageDatabaseReader
{
    private readonly string _databasePath;

    public UsageDatabaseReader(string? databasePath = null)
    {
        // Default: %APPDATA%\9router\db\data.sqlite
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "9router", "db", "data.sqlite");
    }

    /// <summary>
    /// Gets usage data for all connections from today's daily usage.
    /// Returns dictionary: connectionId -> (requests, promptTokens, completionTokens, cost)
    /// </summary>
    public Dictionary<string, UsageData> GetTodayUsageByConnection()
    {
        var result = new Dictionary<string, UsageData>();

        if (!File.Exists(_databasePath))
        {
            return result; // Database doesn't exist yet
        }

        try
        {
            using var conn = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM usageDaily WHERE dateKey = @today LIMIT 1";
            cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var jsonData = reader.GetString(0);
                var doc = JsonDocument.Parse(jsonData);

                // Parse byAccount section
                if (doc.RootElement.TryGetProperty("byAccount", out var byAccount))
                {
                    foreach (var account in byAccount.EnumerateObject())
                    {
                        var connectionId = account.Name;
                        var data = account.Value;

                        var usage = new UsageData(
                            Requests: data.TryGetProperty("requests", out var r) ? r.GetInt32() : 0,
                            PromptTokens: data.TryGetProperty("promptTokens", out var pt) ? pt.GetInt64() : 0,
                            CompletionTokens: data.TryGetProperty("completionTokens", out var ct) ? ct.GetInt64() : 0,
                            Cost: data.TryGetProperty("cost", out var c) ? c.GetDouble() : 0
                        );

                        result[connectionId] = usage;
                    }
                }
            }
        }
        catch (SqliteException)
        {
            // Database might be locked by 9Router, return empty result
            return result;
        }
        catch (Exception)
        {
            // Other errors (corrupted db, etc), return empty result
            return result;
        }

        return result;
    }
}

public record UsageData(
    int Requests,
    long PromptTokens,
    long CompletionTokens,
    double Cost);
