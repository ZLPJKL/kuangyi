using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KuangYi.Core.Services;

public sealed record GlossaryApplication(string Text, IReadOnlyDictionary<int, string> Replacements);

public sealed record GlossaryEntry(string? SourceLanguage, string? TargetLanguage, string Source, string Target);

/// <summary>
/// Simple user-editable glossary. Format:
///   en>zh Account=账户
///   ja>zh 読書=阅读
/// Lines starting with # are ignored; a missing direction applies to every pair.
/// </summary>
public sealed class GlossaryService
{
    private static readonly Regex PlaceholderRegex = new(
        @"\[*\s*G(\d+)\s*\]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly string _path;
    private List<Entry> _cachedEntries = new();
    private DateTime _cachedWriteTime;
    private long _cachedLength = -1;

    public GlossaryService(string path)
    {
        _path = path;
        EnsureDefaultFile();
    }

    public string FilePath => _path;

    public string Version
    {
        get
        {
            try
            {
                var file = new FileInfo(_path);
                return file.Exists
                    ? $"{file.LastWriteTimeUtc.Ticks}:{file.Length}"
                    : "0";
            }
            catch
            {
                return "0";
            }
        }
    }

    public int Count => LoadEntries().Count;

    public IReadOnlyList<GlossaryEntry> Entries => LoadEntries()
        .Select(entry => new GlossaryEntry(
            entry.Direction is null ? null : entry.Direction.Split('>')[0],
            entry.Direction is null ? null : entry.Direction.Split('>')[1],
            entry.Source,
            entry.Target))
        .ToList();

    public string ReadRaw()
    {
        EnsureDefaultFile();
        try
        {
            return File.ReadAllText(_path, Encoding.UTF8);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void WriteRaw(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, text ?? string.Empty, new UTF8Encoding(false));
    }

    public void ImportCsv(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        var output = new List<string>
        {
            "# 从 CSV 导入",
            "# 每行：源语言>目标语言 原文=译文",
        };

        foreach (var raw in lines)
        {
            var fields = ParseCsvLine(raw);
            if (fields.Count < 4)
            {
                continue;
            }

            if (string.Equals(fields[0].Trim(), "source_lang", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceLanguage = fields[0].Trim();
            var targetLanguage = fields[1].Trim();
            var source = fields[2].Trim();
            var target = fields[3].Trim();
            if (source.Length == 0 || target.Length == 0)
            {
                continue;
            }

            var direction = sourceLanguage.Length > 0 && targetLanguage.Length > 0
                ? $"{sourceLanguage}>{targetLanguage} "
                : string.Empty;
            output.Add($"{direction}{source}={target}");
        }

        WriteRaw(string.Join(Environment.NewLine, output));
    }

    public void ExportCsv(string csvPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("source_lang,target_lang,source,target");
        foreach (var entry in Entries)
        {
            builder.Append(Csv(entry.SourceLanguage ?? string.Empty)).Append(',')
                .Append(Csv(entry.TargetLanguage ?? string.Empty)).Append(',')
                .Append(Csv(entry.Source)).Append(',')
                .Append(Csv(entry.Target)).AppendLine();
        }

        File.WriteAllText(csvPath, builder.ToString(), new UTF8Encoding(false));
    }

    public GlossaryApplication Prepare(string text, string sourceCode, string targetCode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new GlossaryApplication(text, new Dictionary<int, string>());
        }

        var entries = LoadEntries()
            .Where(e => e.Matches(sourceCode, targetCode))
            .OrderByDescending(e => e.Source.Length)
            .ToList();
        if (entries.Count == 0)
        {
            return new GlossaryApplication(text, new Dictionary<int, string>());
        }

        var prepared = text;
        var replacements = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            var placeholder = $"[[G{replacements.Count + 1}]]";
            var replaced = ReplaceTerm(prepared, entry.Source, placeholder);
            if (string.Equals(replaced, prepared, StringComparison.Ordinal))
            {
                continue;
            }

            replacements[replacements.Count + 1] = entry.Target;
            prepared = replaced;
        }

        return new GlossaryApplication(prepared, replacements);
    }

    private static string ReplaceTerm(string text, string term, string replacement)
    {
        if (!NeedsWordBoundary(term))
        {
            return text.Replace(term, replacement, StringComparison.OrdinalIgnoreCase);
        }

        var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])";
        return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
    }

    private static bool NeedsWordBoundary(string term)
    {
        foreach (var c in term)
        {
            // CJK terms are commonly written without spaces; direct replacement is
            // the useful behavior there. Latin/Cyrillic words need boundaries so
            // that "API" does not match inside "CAPITAL".
            var code = (int)c;
            if ((code >= 0x3040 && code <= 0x30FF)
                || (code >= 0x3400 && code <= 0x9FFF)
                || (code >= 0xAC00 && code <= 0xD7AF))
            {
                return false;
            }

            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    public string Restore(string translated, IReadOnlyDictionary<int, string> replacements)
    {
        if (string.IsNullOrEmpty(translated) || replacements.Count == 0)
        {
            return translated;
        }

        return PlaceholderRegex.Replace(translated, match =>
        {
            return int.TryParse(match.Groups[1].Value, out var index)
                   && replacements.TryGetValue(index, out var target)
                ? target
                : match.Value;
        });
    }

    public void OpenInEditor()
    {
        EnsureDefaultFile();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _path,
            UseShellExecute = true
        });
    }

    private List<Entry> LoadEntries()
    {
        lock (_gate)
        {
            try
            {
                var file = new FileInfo(_path);
                if (!file.Exists)
                {
                    EnsureDefaultFile();
                    file.Refresh();
                }

                if (_cachedLength == file.Length && _cachedWriteTime == file.LastWriteTimeUtc)
                {
                    return _cachedEntries;
                }

                var entries = new List<Entry>();
                foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }

                    var equals = line.IndexOf('=');
                    if (equals <= 0 || equals >= line.Length - 1)
                    {
                        continue;
                    }

                    var left = line[..equals].Trim();
                    var target = line[(equals + 1)..].Trim();
                    string source;
                    string? direction = null;
                    var arrow = left.IndexOf('>');
                    if (arrow > 0)
                    {
                        var separator = left.IndexOf(' ', arrow);
                        if (separator > arrow)
                        {
                            direction = left[..separator].Trim().ToLowerInvariant();
                            source = left[(separator + 1)..].Trim();
                        }
                        else
                        {
                            // No term after the direction is not a valid entry.
                            continue;
                        }
                    }
                    else
                    {
                        source = left;
                    }

                    if (source.Length == 0 || target.Length == 0)
                    {
                        continue;
                    }

                    entries.Add(new Entry(direction, source, target));
                }

                _cachedEntries = entries;
                _cachedWriteTime = file.LastWriteTimeUtc;
                _cachedLength = file.Length;
                return entries;
            }
            catch
            {
                return new List<Entry>();
            }
        }
    }

    private void EnsureDefaultFile()
    {
        try
        {
            if (File.Exists(_path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, """
                # 框译术语表
                # 每行：源语言>目标语言 原文=译文
                # 不写方向则对所有语言方向生效。
                #
                # 示例：
                # en>zh Account=账户
                # en>zh prompt=提示词
                # ja>zh 読書=阅读
                # API=API
                """, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static string Csv(string value)
        => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private sealed record Entry(string? Direction, string Source, string Target)
    {
        public bool Matches(string sourceCode, string targetCode)
        {
            if (string.IsNullOrEmpty(Direction))
            {
                return true;
            }

            var pair = $"{sourceCode}>{targetCode}";
            return string.Equals(Direction, pair, StringComparison.OrdinalIgnoreCase);
        }
    }
}
