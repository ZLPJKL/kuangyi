using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;
using Microsoft.Data.Sqlite;

namespace KuangYi.Core.Services.Translation;

public sealed class DictionaryProvider : ITranslationProvider
{
	private readonly string _dbPath;

	public string Id => "dictionary";

	public string Name => "内置词典";

	public bool IsAvailable => File.Exists(_dbPath);

	public DictionaryProvider(string dbPath)
	{
		_dbPath = dbPath;
	}

	public Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
	{
		if (!IsAvailable)
		{
			return Task.FromResult<TranslationResult>(null);
		}
		string text = request.Text.Trim();
		if (text.Length == 0 || text.Length > 64 || text.Contains('\n'))
		{
			return Task.FromResult<TranslationResult>(null);
		}
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnection(new SqliteConnectionStringBuilder
			{
				DataSource = _dbPath,
				Mode = SqliteOpenMode.ReadOnly
			}.ToString());
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT translation FROM dict\nWHERE source_lang = $s AND target_lang = $t AND term = $term COLLATE NOCASE\nLIMIT 1;";
			sqliteCommand.Parameters.AddWithValue("$s", request.SourceCode);
			sqliteCommand.Parameters.AddWithValue("$t", request.TargetCode);
			sqliteCommand.Parameters.AddWithValue("$term", text);
			string text2 = sqliteCommand.ExecuteScalar() as string;
			if (string.IsNullOrWhiteSpace(text2))
			{
				return Task.FromResult<TranslationResult>(null);
			}
			return Task.FromResult(new TranslationResult(text2, Id, Name, IsOffline: true));
		}
		catch
		{
			return Task.FromResult<TranslationResult>(null);
		}
	}
}
