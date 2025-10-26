using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClipManagerForWindows.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace ClipManagerForWindows.Services;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly string _dbPath;

    public SqliteSettingsStore(IConfiguration cfg)
    {
        var configured = cfg.GetSection("App").GetValue<string>("DatabasePath");
        _dbPath = AppPaths.GetDatabasePath(configured);
    }

    private SqliteConnection CreateConnection() => new($"Data Source={_dbPath};Cache=Shared");

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);
 SELECT Value FROM Settings WHERE Key=$k;";
        cmd.Parameters.AddWithValue("$k", key);
        string? last = null;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            last = reader.IsDBNull(0) ? null : reader.GetString(0);
        }
        return last;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);
 INSERT INTO Settings(Key,Value) VALUES($k,$v)
 ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
