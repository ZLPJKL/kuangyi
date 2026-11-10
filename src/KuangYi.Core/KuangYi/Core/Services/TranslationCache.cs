using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services;

/// <summary>
/// Small persistent cache for repeated OCR/translation results.
/// The cache is deliberately bounded so it cannot grow without limit.
/// </summary>
public sealed class TranslationCache
{
    private const int DefaultMaxEntries = 1000;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _maxEntries = DefaultMaxEntries;

    public int MaxEntries
    {
        get => _maxEntries;
        set => _maxEntries = Math.Clamp(value, 1, 5000);
    }

    public TranslationCache(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS translation_cache (
                    cache_key TEXT PRIMARY KEY,
                    source_text TEXT NOT NULL,
                    result_text TEXT NOT NULL,
                    provider_id TEXT NOT NULL,
                    provider_name TEXT NOT NULL,
                    is_offline INTEGER NOT NULL,
                    last_used INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
        catch
        {
            // A cache must never prevent translation.
        }
    }

    public static string CreateKey(string text, string source, string target, string mode, string glossaryVersion, string fingerprint = "")
    {
        var input = string.Join("\u001f", source, target, mode, glossaryVersion, fingerprint, text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    public static string ComputeFingerprint(string appVersion, string modelRoot)
    {
        var builder = new StringBuilder();
        builder.Append(appVersion).Append('|');
        try
        {
            if (Directory.Exists(modelRoot))
            {
                foreach (var file in Directory.EnumerateFiles(modelRoot, "*", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(file);
                    builder.Append(Path.GetRelativePath(modelRoot, file))
                        .Append(':')
                        .Append(info.Length)
                        .Append(':')
                        .Append(info.LastWriteTimeUtc.Ticks)
                        .Append(';');
                }
            }
        }
        catch
        {
            // A missing or unreadable model directory still yields a stable app-version fingerprint.
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    public async Task<TranslationResult?> TryGetAsync(string key, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT result_text, provider_id, provider_name, is_offline
                    FROM translation_cache
                    WHERE cache_key = $key
                    LIMIT 1;
                    """;
                command.Parameters.AddWithValue("$key", key);
                using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                var result = new TranslationResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3) != 0);
                await reader.DisposeAsync().ConfigureAwait(false);

                using var touch = connection.CreateCommand();
                touch.CommandText = "UPDATE translation_cache SET last_used = $now WHERE cache_key = $key;";
                touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                touch.Parameters.AddWithValue("$key", key);
                await touch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            return null;
        }
    }

    public async Task StoreAsync(string key, string sourceText, TranslationResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return;
        }

        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT OR REPLACE INTO translation_cache
                        (cache_key, source_text, result_text, provider_id, provider_name, is_offline, last_used)
                    VALUES ($key, $source, $result, $providerId, $providerName, $offline, $now);
                    """;
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$source", sourceText);
                command.Parameters.AddWithValue("$result", result.Text);
                command.Parameters.AddWithValue("$providerId", result.ProviderId);
                command.Parameters.AddWithValue("$providerName", result.ProviderName);
                command.Parameters.AddWithValue("$offline", result.IsOffline ? 1 : 0);
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                using var prune = connection.CreateCommand();
                prune.CommandText = """
                    DELETE FROM translation_cache
                    WHERE cache_key IN (
                        SELECT cache_key FROM translation_cache
                        ORDER BY last_used ASC
                        LIMIT MAX(0, (SELECT COUNT(*) FROM translation_cache) - $max)
                    );
                    """;
                prune.Parameters.AddWithValue("$max", MaxEntries);
                await prune.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            // Cache write failure is non-fatal.
        }
    }

    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM translation_cache;";
                return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            return 0;
        }
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM translation_cache;";
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            return 0;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        connection.Open();
        return connection;
    }
}
