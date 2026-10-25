using System.Globalization;
using System.Text;
using KuangYi.Core.Models;
using Microsoft.Data.Sqlite;

namespace KuangYi.Core.Services;

/// <summary>
/// Local-only history storage. Two independent pools:
/// recent entries (count + age limited) and favourites (count limited, never expire).
/// </summary>
public sealed class HistoryService
{
    private const string SelectColumns =
        "id, created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms";

    private readonly string _dbPath;
    private readonly string _thumbDir;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryService(string dbPath, string thumbDir)
    {
        _dbPath = dbPath;
        _thumbDir = thumbDir;

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Directory.CreateDirectory(thumbDir);
        Initialize();
    }

    public string DatabasePath => _dbPath;

    public async Task<long> AddAsync(HistoryEntry entry, HistoryOptions options, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    """
                    INSERT INTO entries
                        (created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms)
                    VALUES
                        ($created, $sl, $tl, $src, $dst, $provider, 0, $tags, $thumb, $ms);
                    """;
                cmd.Parameters.AddWithValue("$created", Normalize(entry.CreatedAt).ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$sl", entry.SourceLang);
                cmd.Parameters.AddWithValue("$tl", entry.TargetLang);
                cmd.Parameters.AddWithValue("$src", entry.SourceText);
                cmd.Parameters.AddWithValue("$dst", entry.TargetText);
                cmd.Parameters.AddWithValue("$provider", entry.Provider);
                cmd.Parameters.AddWithValue("$tags", entry.Tags ?? string.Empty);
                cmd.Parameters.AddWithValue("$thumb", (object?)entry.ThumbPath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ms", entry.ElapsedMs);
                cmd.ExecuteNonQuery();
            }

            using (var idCmd = conn.CreateCommand())
            {
                idCmd.CommandText = "SELECT last_insert_rowid();";
                entry.Id = (long)(idCmd.ExecuteScalar() ?? 0L);
            }

            var thumbs = Enforce(conn, options);
            DeleteThumbs(thumbs);
            return entry.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit = 500, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT {SelectColumns} FROM entries WHERE is_favorite = 0 ORDER BY created_utc DESC, id DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
            return Read(cmd);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetFavoritesAsync(int limit = 500, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT {SelectColumns} FROM entries WHERE is_favorite = 1 ORDER BY created_utc DESC, id DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
            return Read(cmd);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string? query,
        bool favoritesOnly,
        int limit = 500,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"""
                 SELECT {SelectColumns} FROM entries
                 WHERE ($favOnly = 0 OR is_favorite = 1)
                   AND ($q = '' OR instr(lower(source_text), lower($q)) > 0 OR instr(lower(target_text), lower($q)) > 0)
                 ORDER BY created_utc DESC, id DESC
                 LIMIT $limit;
                 """;
            cmd.Parameters.AddWithValue("$favOnly", favoritesOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$q", (query ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
            return Read(cmd);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FavoriteToggleResult> ToggleFavoriteAsync(long id, HistoryOptions options, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            int current;
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT is_favorite FROM entries WHERE id = $id;";
                read.Parameters.AddWithValue("$id", id);
                var value = read.ExecuteScalar();
                if (value is null)
                {
                    return FavoriteToggleResult.NotFound;
                }

                current = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            if (current == 0)
            {
                var favorites = Count(conn, "is_favorite = 1");
                if (favorites >= Math.Max(1, options.FavoriteLimit))
                {
                    return FavoriteToggleResult.LimitReached;
                }
            }

            using (var update = conn.CreateCommand())
            {
                update.CommandText = "UPDATE entries SET is_favorite = $fav WHERE id = $id;";
                update.Parameters.AddWithValue("$fav", current == 0 ? 1 : 0);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            if (current == 1)
            {
                var thumbs = Enforce(conn, options);
                DeleteThumbs(thumbs);
            }

            return FavoriteToggleResult.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            string? thumb = null;
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT thumb_path FROM entries WHERE id = $id;";
                read.Parameters.AddWithValue("$id", id);
                thumb = read.ExecuteScalar() as string;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM entries WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            var affected = cmd.ExecuteNonQuery();
            if (affected > 0 && !string.IsNullOrEmpty(thumb))
            {
                TryDeleteThumb(thumb);
            }

            return affected > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ClearAsync(bool includeFavorites, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            var thumbs = new List<string>();
            using (var read = conn.CreateCommand())
            {
                read.CommandText = includeFavorites
                    ? "SELECT thumb_path FROM entries WHERE thumb_path IS NOT NULL;"
                    : "SELECT thumb_path FROM entries WHERE is_favorite = 0 AND thumb_path IS NOT NULL;";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    thumbs.Add(reader.GetString(0));
                }
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = includeFavorites
                ? "DELETE FROM entries;"
                : "DELETE FROM entries WHERE is_favorite = 0;";
            var affected = cmd.ExecuteNonQuery();
            DeleteThumbs(thumbs);
            return affected;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryStats> GetStatsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            return new HistoryStats(Count(conn, "is_favorite = 0"), Count(conn, "is_favorite = 1"));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ExportCsvAsync(string path, bool favoritesOnly, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT {SelectColumns} FROM entries WHERE ($favOnly = 0 OR is_favorite = 1) ORDER BY created_utc DESC, id DESC;";
            cmd.Parameters.AddWithValue("$favOnly", favoritesOnly ? 1 : 0);

            var rows = Read(cmd);
            var sb = new StringBuilder();
            sb.AppendLine("时间,源语言,目标语言,原文,译文,来源,收藏,标签");
            foreach (var e in rows)
            {
                sb.Append(Csv(e.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                  .Append(Csv(e.SourceLang)).Append(',')
                  .Append(Csv(e.TargetLang)).Append(',')
                  .Append(Csv(e.SourceText)).Append(',')
                  .Append(Csv(e.TargetText)).Append(',')
                  .Append(Csv(e.Provider)).Append(',')
                  .Append(e.IsFavorite ? "是" : "否").Append(',')
                  .Append(Csv(e.Tags)).AppendLine();
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return rows.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS entries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                created_utc TEXT    NOT NULL,
                source_lang TEXT    NOT NULL,
                target_lang TEXT    NOT NULL,
                source_text TEXT    NOT NULL,
                target_text TEXT    NOT NULL,
                provider    TEXT    NOT NULL,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                tags        TEXT    NOT NULL DEFAULT '',
                thumb_path  TEXT    NULL,
                elapsed_ms  INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_entries_fav_created ON entries(is_favorite, created_utc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };

        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static List<string> Enforce(SqliteConnection conn, HistoryOptions options)
    {
        var limit = Math.Max(1, options.NonFavoriteLimit);
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, options.Days)).ToString("O", CultureInfo.InvariantCulture);
        var victims = new Dictionary<long, string?>();

        Collect(
            conn,
            victims,
            "SELECT id, thumb_path FROM entries WHERE is_favorite = 0 AND created_utc < $cutoff;",
            cmd => cmd.Parameters.AddWithValue("$cutoff", cutoff));

        Collect(
            conn,
            victims,
            """
            SELECT id, thumb_path FROM entries
            WHERE is_favorite = 0
              AND id NOT IN (
                  SELECT id FROM entries WHERE is_favorite = 0
                  ORDER BY created_utc DESC, id DESC LIMIT $limit)
            """,
            cmd => cmd.Parameters.AddWithValue("$limit", limit));

        if (victims.Count == 0)
        {
            return new List<string>();
        }

        using var tx = conn.BeginTransaction();
        foreach (var chunk in victims.Keys.Chunk(400))
        {
            var ids = string.Join(", ", chunk.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM entries WHERE id IN ({ids});";
            del.ExecuteNonQuery();
        }

        tx.Commit();
        return victims.Values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
    }

    private static void Collect(
        SqliteConnection conn,
        IDictionary<long, string?> victims,
        string sql,
        Action<SqliteCommand> bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var thumb = reader.IsDBNull(1) ? null : reader.GetString(1);
            victims[id] = thumb;
        }
    }

    private static int Count(SqliteConnection conn, string where)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM entries WHERE {where};";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static List<HistoryEntry> Read(SqliteCommand cmd)
    {
        var list = new List<HistoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new HistoryEntry
            {
                Id = reader.GetInt64(0),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SourceLang = reader.GetString(2),
                TargetLang = reader.GetString(3),
                SourceText = reader.GetString(4),
                TargetText = reader.GetString(5),
                Provider = reader.GetString(6),
                IsFavorite = reader.GetInt64(7) != 0,
                Tags = reader.GetString(8),
                ThumbPath = reader.IsDBNull(9) ? null : reader.GetString(9),
                ElapsedMs = reader.GetInt64(10),
            });
        }

        return list;
    }

    private void DeleteThumbs(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            TryDeleteThumb(path);
        }
    }

    private void TryDeleteThumb(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(_thumbDir);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch
        {
            // a dangling thumbnail must never break history bookkeeping
        }
    }

    private static DateTimeOffset Normalize(DateTimeOffset value)
        => value == default ? DateTimeOffset.UtcNow : value.ToUniversalTime();

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
