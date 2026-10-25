using KuangYi.Core.Models;
using Microsoft.Data.Sqlite;

namespace KuangYi.Core.Services.Translation;

/// <summary>
/// Offline dictionary provider. A dictionary pack is a SQLite database with:
///   CREATE TABLE dict(source_lang TEXT, target_lang TEXT, term TEXT, translation TEXT, pos TEXT, phonetic TEXT);
///   CREATE INDEX dict_lookup ON dict(source_lang, target_lang, term);
/// The pack is optional and user supplied; the provider is simply skipped when absent.
/// </summary>
public sealed class DictionaryProvider : ITranslationProvider
{
    private readonly string _dbPath;

    public DictionaryProvider(string dbPath)
    {
        _dbPath = dbPath;
    }

    public string Id => "dictionary";

    public string Name => "内置词典";

    public bool IsAvailable => File.Exists(_dbPath);

    public Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return Task.FromResult<TranslationResult?>(null);
        }

        var term = request.Text.Trim();
        if (term.Length == 0 || term.Length > 64 || term.Contains('\n'))
        {
            return Task.FromResult<TranslationResult?>(null);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            };

            using var conn = new SqliteConnection(builder.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT translation FROM dict
                WHERE source_lang = $s AND target_lang = $t AND term = $term COLLATE NOCASE
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$s", request.SourceCode);
            cmd.Parameters.AddWithValue("$t", request.TargetCode);
            cmd.Parameters.AddWithValue("$term", term);

            var value = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return Task.FromResult<TranslationResult?>(null);
            }

            return Task.FromResult<TranslationResult?>(
                new TranslationResult(value, Id, Name, IsOffline: true));
        }
        catch
        {
            // A corrupt or incompatible pack must not block the rest of the chain.
            return Task.FromResult<TranslationResult?>(null);
        }
    }
}
