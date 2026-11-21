using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KuangYi.Core.Services;

/// <summary>
/// Reflows OCR/translation line breaks into readable paragraphs.
///
/// OCR engines split one sentence into several lines, and translation
/// providers often return each chunk as a separate paragraph. Keeping those
/// blank lines makes a long article look like a stack of fragments. This
/// formatter keeps title-like lines and bullet lists, then joins the body into
/// flowing paragraphs so a long text reads like a normal article.
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
        var blocks = normalized
            .Split("\n\n", StringSplitOptions.None)
            .Select(block => block.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList())
            .Where(lines => lines.Count > 0)
            .ToList();
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var output = new List<string>();
        var body = new StringBuilder();
        var bodyLanguage = languageCode;

        for (var index = 0; index < blocks.Count; index++)
        {
            var lines = blocks[index];
            if (lines.Count == 1 && IsHeading(lines[0], index, blocks, languageCode))
            {
                FlushBody(output, body);
                output.Add(lines[0]);
                continue;
            }

            foreach (var line in lines)
            {
                if (IsListLine(line))
                {
                    FlushBody(output, body);
                    output.Add(line);
                    continue;
                }

                if (body.Length > 0)
                {
                    body.Append(JoinSeparator(body.ToString(), line, bodyLanguage));
                }
                body.Append(line);
            }
        }

        FlushBody(output, body);
        return string.Join("\n\n", output);
    }

    private static void FlushBody(List<string> output, StringBuilder body)
    {
        if (body.Length == 0)
        {
            return;
        }

        output.Add(body.ToString().Trim());
        body.Clear();
    }

    private static bool IsHeading(string line, int index, IReadOnlyList<List<string>> blocks, string? languageCode)
    {
        if (line.Length == 0 || IsListLine(line))
        {
            return false;
        }

        var marker = line.TrimEnd();
        if (marker.EndsWith(':') || marker.EndsWith('：'))
        {
            return true;
        }

        if (ContainsHeadingMarker(line))
        {
            return true;
        }

        if (line.Length > HeadingLengthLimit(languageCode))
        {
            return false;
        }

        if (index == 0 && !EndsWithSentencePunctuation(line))
        {
            return true;
        }

        // A short line followed by a substantially longer block is a title,
        // not an ordinary one-sentence paragraph.
        if (index + 1 < blocks.Count)
        {
            var nextLength = blocks[index + 1].Sum(part => part.Length);
            return !EndsWithSentencePunctuation(line)
                   && nextLength > Math.Max(40, line.Length * 2);
        }

        return false;
    }

    private static bool ContainsHeadingMarker(string line)
    {
        string[] markers =
        {
            "题目", "标题", "標題", "題目", "主题", "主題",
            "Title", "Theme", "Тема", "제목", "題名", "目次",
        };
        return markers.Any(marker => line.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
                                     || line.Contains(marker + ":", StringComparison.OrdinalIgnoreCase)
                                     || line.Contains(marker + "：", StringComparison.OrdinalIgnoreCase));
    }

    private static int HeadingLengthLimit(string? languageCode)
    {
        var code = (languageCode ?? string.Empty).ToLowerInvariant();
        return code.StartsWith("zh") || code.StartsWith("ja") || code.StartsWith("ko") ? 32 : 72;
    }

    private static bool EndsWithSentencePunctuation(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        return line[^1] is '。' or '！' or '？' or '!' or '?' or '.' or '…' or '」' or '』';
    }

    private static string JoinSeparator(string previous, string next, string? languageCode)
    {
        var code = (languageCode ?? string.Empty).ToLowerInvariant();
        if (code.StartsWith("zh") || code.StartsWith("ja"))
        {
            return string.Empty;
        }

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
