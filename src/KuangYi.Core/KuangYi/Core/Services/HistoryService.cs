using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;
using Microsoft.Data.Sqlite;

namespace KuangYi.Core.Services;

public sealed class HistoryService
{
	private const string SelectColumns = "id, created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms";

	private readonly string _dbPath;

	private readonly string _thumbDir;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public string DatabasePath => _dbPath;

	public HistoryService(string dbPath, string thumbDir)
	{
		_dbPath = dbPath;
		_thumbDir = thumbDir;
		string directoryName = Path.GetDirectoryName(dbPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		Directory.CreateDirectory(thumbDir);
		Initialize();
	}

	public async Task<long> AddAsync(HistoryEntry entry, HistoryOptions options, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				sqliteCommand.CommandText = "INSERT INTO entries\n    (created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms)\nVALUES\n    ($created, $sl, $tl, $src, $dst, $provider, 0, $tags, $thumb, $ms);";
				sqliteCommand.Parameters.AddWithValue("$created", Normalize(entry.CreatedAt).ToString("O", CultureInfo.InvariantCulture));
				sqliteCommand.Parameters.AddWithValue("$sl", entry.SourceLang);
				sqliteCommand.Parameters.AddWithValue("$tl", entry.TargetLang);
				sqliteCommand.Parameters.AddWithValue("$src", entry.SourceText);
				sqliteCommand.Parameters.AddWithValue("$dst", entry.TargetText);
				sqliteCommand.Parameters.AddWithValue("$provider", entry.Provider);
				sqliteCommand.Parameters.AddWithValue("$tags", entry.Tags ?? string.Empty);
				sqliteCommand.Parameters.AddWithValue("$thumb", ((object)entry.ThumbPath) ?? ((object)DBNull.Value));
				sqliteCommand.Parameters.AddWithValue("$ms", entry.ElapsedMs);
				sqliteCommand.ExecuteNonQuery();
			}
			using (SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand())
			{
				sqliteCommand2.CommandText = "SELECT last_insert_rowid();";
				entry.Id = (long)(sqliteCommand2.ExecuteScalar() ?? ((object)0L));
			}
			List<string> paths = Enforce(sqliteConnection, options);
			DeleteThumbs(paths);
			return entry.Id;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit = 500, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT id, created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms FROM entries WHERE is_favorite = 0 ORDER BY created_utc DESC, id DESC LIMIT $limit;";
			sqliteCommand.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
			return Read(sqliteCommand);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<IReadOnlyList<HistoryEntry>> GetFavoritesAsync(int limit = 500, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT id, created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms FROM entries WHERE is_favorite = 1 ORDER BY created_utc DESC, id DESC LIMIT $limit;";
			sqliteCommand.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
			return Read(sqliteCommand);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, bool favoritesOnly, int limit = 500, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT id, created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms FROM entries\nWHERE ($favOnly = 0 OR is_favorite = 1)\n  AND ($q = '' OR instr(lower(source_text), lower($q)) > 0 OR instr(lower(target_text), lower($q)) > 0)\nORDER BY created_utc DESC, id DESC\nLIMIT $limit;";
			sqliteCommand.Parameters.AddWithValue("$favOnly", favoritesOnly ? 1 : 0);
			sqliteCommand.Parameters.AddWithValue("$q", (query ?? string.Empty).Trim());
			sqliteCommand.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
			return Read(sqliteCommand);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<FavoriteToggleResult> ToggleFavoriteAsync(long id, HistoryOptions options, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			int num;
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				sqliteCommand.CommandText = "SELECT is_favorite FROM entries WHERE id = $id;";
				sqliteCommand.Parameters.AddWithValue("$id", id);
				object obj = sqliteCommand.ExecuteScalar();
				if (obj == null)
				{
					return FavoriteToggleResult.NotFound;
				}
				num = Convert.ToInt32(obj, CultureInfo.InvariantCulture);
			}
			if (num == 0 && Count(sqliteConnection, "is_favorite = 1") >= Math.Max(1, options.FavoriteLimit))
			{
				return FavoriteToggleResult.LimitReached;
			}
			using (SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand())
			{
				sqliteCommand2.CommandText = "UPDATE entries SET is_favorite = $fav WHERE id = $id;";
				sqliteCommand2.Parameters.AddWithValue("$fav", (num == 0) ? 1 : 0);
				sqliteCommand2.Parameters.AddWithValue("$id", id);
				sqliteCommand2.ExecuteNonQuery();
			}
			if (num == 1)
			{
				List<string> paths = Enforce(sqliteConnection, options);
				DeleteThumbs(paths);
			}
			return FavoriteToggleResult.Ok;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<bool> DeleteAsync(long id, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			string text = null;
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				sqliteCommand.CommandText = "SELECT thumb_path FROM entries WHERE id = $id;";
				sqliteCommand.Parameters.AddWithValue("$id", id);
				text = sqliteCommand.ExecuteScalar() as string;
			}
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = "DELETE FROM entries WHERE id = $id;";
			sqliteCommand2.Parameters.AddWithValue("$id", id);
			int num = sqliteCommand2.ExecuteNonQuery();
			if (num > 0 && !string.IsNullOrEmpty(text))
			{
				TryDeleteThumb(text);
			}
			return num > 0;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<int> ClearAsync(bool includeFavorites, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			List<string> list = new List<string>();
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				sqliteCommand.CommandText = (includeFavorites ? "SELECT thumb_path FROM entries WHERE thumb_path IS NOT NULL;" : "SELECT thumb_path FROM entries WHERE is_favorite = 0 AND thumb_path IS NOT NULL;");
				using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
				while (sqliteDataReader.Read())
				{
					list.Add(sqliteDataReader.GetString(0));
				}
			}
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = (includeFavorites ? "DELETE FROM entries;" : "DELETE FROM entries WHERE is_favorite = 0;");
			int result = sqliteCommand2.ExecuteNonQuery();
			DeleteThumbs(list);
			return result;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<HistoryStats> GetStatsAsync(CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection conn = Open();
			return new HistoryStats(Count(conn, "is_favorite = 0"), Count(conn, "is_favorite = 1"));
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<int> ExportCsvAsync(string path, bool favoritesOnly, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using SqliteConnection sqliteConnection = Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT id, created_utc, source_lang, target_lang, source_text, target_text, provider, is_favorite, tags, thumb_path, elapsed_ms FROM entries WHERE ($favOnly = 0 OR is_favorite = 1) ORDER BY created_utc DESC, id DESC;";
			sqliteCommand.Parameters.AddWithValue("$favOnly", favoritesOnly ? 1 : 0);
			List<HistoryEntry> list = Read(sqliteCommand);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("时间,源语言,目标语言,原文,译文,来源,收藏,标签");
			foreach (HistoryEntry item in list)
			{
				stringBuilder.Append(Csv(item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append(',').Append(Csv(item.SourceLang))
					.Append(',')
					.Append(Csv(item.TargetLang))
					.Append(',')
					.Append(Csv(item.SourceText))
					.Append(',')
					.Append(Csv(item.TargetText))
					.Append(',')
					.Append(Csv(item.Provider))
					.Append(',')
					.Append(item.IsFavorite ? "是" : "否")
					.Append(',')
					.Append(Csv(item.Tags))
					.AppendLine();
			}
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(path, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			return list.Count;
		}
		finally
		{
			_gate.Release();
		}
	}

	private void Initialize()
	{
		using SqliteConnection sqliteConnection = Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "CREATE TABLE IF NOT EXISTS entries (\n    id          INTEGER PRIMARY KEY AUTOINCREMENT,\n    created_utc TEXT    NOT NULL,\n    source_lang TEXT    NOT NULL,\n    target_lang TEXT    NOT NULL,\n    source_text TEXT    NOT NULL,\n    target_text TEXT    NOT NULL,\n    provider    TEXT    NOT NULL,\n    is_favorite INTEGER NOT NULL DEFAULT 0,\n    tags        TEXT    NOT NULL DEFAULT '',\n    thumb_path  TEXT    NULL,\n    elapsed_ms  INTEGER NOT NULL DEFAULT 0\n);\nCREATE INDEX IF NOT EXISTS idx_entries_fav_created ON entries(is_favorite, created_utc DESC);";
		sqliteCommand.ExecuteNonQuery();
	}

	private SqliteConnection Open()
	{
		SqliteConnection sqliteConnection = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = _dbPath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Shared
		}.ToString());
		sqliteConnection.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
		sqliteCommand.ExecuteNonQuery();
		return sqliteConnection;
	}

	private static List<string> Enforce(SqliteConnection conn, HistoryOptions options)
	{
		int limit = Math.Max(1, options.NonFavoriteLimit);
		string cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, options.Days)).ToString("O", CultureInfo.InvariantCulture);
		Dictionary<long, string> dictionary = new Dictionary<long, string>();
		Collect(conn, dictionary, "SELECT id, thumb_path FROM entries WHERE is_favorite = 0 AND created_utc < $cutoff;", (SqliteCommand cmd) =>
		{
			cmd.Parameters.AddWithValue("$cutoff", cutoff);
		});
		Collect(conn, dictionary, "SELECT id, thumb_path FROM entries\nWHERE is_favorite = 0\n  AND id NOT IN (\n      SELECT id FROM entries WHERE is_favorite = 0\n      ORDER BY created_utc DESC, id DESC LIMIT $limit)", (SqliteCommand cmd) =>
		{
			cmd.Parameters.AddWithValue("$limit", limit);
		});
		if (dictionary.Count == 0)
		{
			return new List<string>();
		}
		using SqliteTransaction sqliteTransaction = conn.BeginTransaction();
		foreach (long[] item in dictionary.Keys.Chunk(400))
		{
			string text = string.Join(", ", item.Select((long i) => i.ToString(CultureInfo.InvariantCulture)));
			using SqliteCommand sqliteCommand = conn.CreateCommand();
			sqliteCommand.Transaction = sqliteTransaction;
			sqliteCommand.CommandText = "DELETE FROM entries WHERE id IN (" + text + ");";
			sqliteCommand.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
		return (from v in dictionary.Values
			where !string.IsNullOrEmpty(v)
			select (v)).ToList();
	}

	private static void Collect(SqliteConnection conn, IDictionary<long, string?> victims, string sql, Action<SqliteCommand> bind)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		bind(sqliteCommand);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			long @int = sqliteDataReader.GetInt64(0);
			string value = (sqliteDataReader.IsDBNull(1) ? null : sqliteDataReader.GetString(1));
			victims[@int] = value;
		}
	}

	private static int Count(SqliteConnection conn, string where)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "SELECT COUNT(*) FROM entries WHERE " + where + ";";
		return Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0), CultureInfo.InvariantCulture);
	}

	private static List<HistoryEntry> Read(SqliteCommand cmd)
	{
		List<HistoryEntry> list = new List<HistoryEntry>();
		using SqliteDataReader sqliteDataReader = cmd.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(new HistoryEntry
			{
				Id = sqliteDataReader.GetInt64(0),
				CreatedAt = DateTimeOffset.Parse(sqliteDataReader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
				SourceLang = sqliteDataReader.GetString(2),
				TargetLang = sqliteDataReader.GetString(3),
				SourceText = sqliteDataReader.GetString(4),
				TargetText = sqliteDataReader.GetString(5),
				Provider = sqliteDataReader.GetString(6),
				IsFavorite = (sqliteDataReader.GetInt64(7) != 0),
				Tags = sqliteDataReader.GetString(8),
				ThumbPath = (sqliteDataReader.IsDBNull(9) ? null : sqliteDataReader.GetString(9)),
				ElapsedMs = sqliteDataReader.GetInt64(10)
			});
		}
		return list;
	}

	private void DeleteThumbs(IEnumerable<string> paths)
	{
		foreach (string path in paths)
		{
			TryDeleteThumb(path);
		}
	}

	private void TryDeleteThumb(string path)
	{
		try
		{
			string fullPath = Path.GetFullPath(path);
			string fullPath2 = Path.GetFullPath(_thumbDir);
			if (fullPath.StartsWith(fullPath2, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
			{
				File.Delete(fullPath);
			}
		}
		catch
		{
		}
	}

	private static DateTimeOffset Normalize(DateTimeOffset value)
	{
		if (!(value == default(DateTimeOffset)))
		{
			return value.ToUniversalTime();
		}
		return DateTimeOffset.UtcNow;
	}

	private static string Csv(string? value)
	{
		string text = value ?? string.Empty;
		if (text.IndexOfAny(new char[4] { ',', '"', '\n', '\r' }) < 0)
		{
			return text;
		}
		return "\"" + text.Replace("\"", "\"\"") + "\"";
	}
}
