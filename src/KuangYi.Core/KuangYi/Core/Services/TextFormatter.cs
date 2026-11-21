using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KuangYi.Core.Services;

/// <summary>
/// Reflows OCR/translation line breaks into readable paragraphs.
/// OCR engines often split a sentence across lines; translation providers
/// sometimes return each chunk on its own line. This keeps blank-line
/// paragraph boundaries and rejoins wrapped lines.
/// </summary>
public static class TextFormatter
{
    public static string Reflow(string? text, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var paragraphs = new List<string>();
        foreach (var block in normalized.Split("\n\n", StringSplitOptions.None))
        {
            var lines = block.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var builder = new StringBuilder();
            var previous = string.Empty;
            foreach (var line in lines)
            {
                if (builder.Length > 0)
                {
                    if (IsListLine(line) || IsListLine(previous))
                    {
                        builder.Append('\n');
                    }
                    else
                    {
                        builder.Append(JoinSeparator(previous, line, languageCode));
                    }
                }

                builder.Append(line);
                previous = line;
            }

            paragraphs.Add(builder.ToString());
        }

        return string.Join("\n\n", paragraphs);
    }

    private static string JoinSeparator(string previous, string next, string? languageCode)
    {
        var code = (languageCode ?? string.Empty).ToLowerInvariant();
        if (code.StartsWith("zh") || code.StartsWith("ja"))
        {
            return string.Empty;
        }

        // Korean and Latin/Cyrillic scripts need a space between joined lines.
        if (previous.EndsWith('-') && previous.Length > 1)
        {
            return string.Empty;
        }

        return " ";
    }

    private static bool IsListLine(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        var first = line[0];
        if (first is '•' or '·' or '-' or '*' or '–' or '—')
        {
            return true;
        }

        return line.Length > 2
               && char.IsDigit(line[0])
               && (line[1] == '.' || line[1] == '、' || line[1] == ')');
    }
}
