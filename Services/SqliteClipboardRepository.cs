using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClipManagerForWindows.Infrastructure;
using ClipManagerForWindows.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace ClipManagerForWindows.Services;

public sealed class SqliteClipboardRepository : IClipboardRepository
{
    private readonly string _dbPath;

    public SqliteClipboardRepository(IConfiguration cfg)
    {
        var configured = cfg.GetSection("App").GetValue<string>("DatabasePath");
        _dbPath = AppPaths.GetDatabasePath(configured);
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
        return conn;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        PRAGMA journal_mode=WAL;

        CREATE TABLE IF NOT EXISTS ClipboardEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CreatedUtc TEXT NOT NULL,
            TextContent TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_ClipboardEntries_CreatedUtc ON ClipboardEntries(CreatedUtc DESC);";

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> InsertAsync(ClipboardEntry entry, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        INSERT INTO ClipboardEntries (CreatedUtc, TextContent) VALUES ($created, $content);
        SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$created", entry.CreatedUtc.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$content", entry.TextContent);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (long)(result ?? 0L);
    }


    public async Task<IReadOnlyList<ClipboardEntry>> GetRecentAsync(int take, CancellationToken ct)
    {
        var list = new List<ClipboardEntry>();
        await using var conn = CreateConnection();

        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, CreatedUtc, TextContent
            FROM ClipboardEntries
            ORDER BY Id DESC
            LIMIT $take;
            ";
        cmd.Parameters.AddWithValue("$take", take);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadEntry(reader));
        }
        return list;
  }

    public async Task ClearAllAsync(CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM ClipboardEntries;";
        await cmd.ExecuteNonQueryAsync(ct);
        tx.Commit();
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ClipboardEntries WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PruneOldEntriesAsync(int maxEntries, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            DELETE FROM ClipboardEntries
            WHERE Id NOT IN (
                SELECT Id FROM ClipboardEntries
                ORDER BY Id DESC
                LIMIT $maxEntries
            );
        ";
        cmd.Parameters.AddWithValue("$maxEntries", maxEntries);
        var deletedRows = await cmd.ExecuteNonQueryAsync(ct);

        tx.Commit();

        if (deletedRows > 0)
        {
            // Log the pruning (assuming we have access to logger)
            System.Diagnostics.Debug.WriteLine($"Pruned {deletedRows} old clipboard entries to maintain max limit of {maxEntries}");
        }
    }

    private static ClipboardEntry ReadEntry(SqliteDataReader reader)
    {
        return new ClipboardEntry
        {
           Id = reader.GetInt64(0),
           CreatedUtc = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
           TextContent = reader.GetString(2)
        };
    }
}
