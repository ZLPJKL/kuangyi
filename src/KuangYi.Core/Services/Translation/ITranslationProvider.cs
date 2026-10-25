using System.Text;
using System.Text.RegularExpressions;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public interface ITranslationProvider
{
    string Id { get; }

    string Name { get; }

    /// <summary>Returns null when the provider is not applicable (no data pack, not configured).</summary>
    Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct);
}

public static class TextChunker
{
    private static readonly Regex SentenceBoundary = new("(?<=[。！？!?；;\\n])", RegexOptions.Compiled);

    /// <summary>Splits text into UTF-8 byte bounded chunks, preferring sentence boundaries.</summary>
    public static IReadOnlyList<string> SplitByUtf8Bytes(string text, int maxBytes = 450)
    {
        if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return new[] { text };
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        var currentBytes = 0;

        foreach (var segment in SentenceBoundary.Split(text))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            var segmentBytes = Encoding.UTF8.GetByteCount(segment);
            if (segmentBytes > maxBytes)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                    currentBytes = 0;
                }

                foreach (var part in HardSplit(segment, maxBytes))
                {
                    chunks.Add(part);
                }

                continue;
            }

            if (currentBytes + segmentBytes > maxBytes && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
                currentBytes = 0;
            }

            current.Append(segment);
            currentBytes += segmentBytes;
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return chunks;
    }

    private static IEnumerable<string> HardSplit(string text, int maxBytes)
    {
        var current = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var s = rune.ToString();
            var size = Encoding.UTF8.GetByteCount(s);
            if (bytes + size > maxBytes && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
                bytes = 0;
            }

            current.Append(s);
            bytes += size;
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
