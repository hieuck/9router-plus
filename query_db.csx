using System;
using System.Data;
using Microsoft.Data.Sqlite;

var dbPath = @"C:\Users\hieut\AppData\Roaming\9router\db\data.sqlite";
using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

Console.WriteLine("=== Tables ===");
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) Console.WriteLine(reader.GetString(0));
}

Console.WriteLine("\n=== usageDaily Schema ===");
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "PRAGMA table_info(usageDaily)";
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) {
        Console.WriteLine($"{reader.GetString(1)} {reader.GetString(2)}");
    }
}

Console.WriteLine("\n=== usageDaily Data (last 5) ===");
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT dateKey, substr(data, 1, 200) FROM usageDaily ORDER BY dateKey DESC LIMIT 5";
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) {
        Console.WriteLine($"{reader.GetString(0)}: {reader.GetString(1)}");
    }
}
