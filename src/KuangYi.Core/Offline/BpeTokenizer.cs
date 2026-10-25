using System.Text;
using System.Text.Json;

namespace KuangYi.Core.Offline;

/// <summary>
/// SentencePiece-BPE tokenizer reconstructed from a HuggingFace tokenizer.json
/// (vocab ids plus merged-pair ranks). Used by M2M100 exports.
/// </summary>
public sealed class BpeTokenizer : ITextTokenizer
{
    private readonly Dictionary<string, int> _vocab = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idToPiece = new();
    private readonly Dictionary<string, int> _ranks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _specialPieces = new(StringComparer.Ordinal);

    public int UnknownId { get; private set; } = 3;

    public int EndOfSentenceId { get; private set; } = 2;

    public int PadId { get; private set; } = 1;

    public static BpeTokenizer Load(string tokenizerPath, IEnumerable<(int Id, string Piece)> extras)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
        var root = doc.RootElement;
        var model = root.GetProperty("model");
        var tokenizer = new BpeTokenizer();

        foreach (var property in model.GetProperty("vocab").EnumerateObject())
        {
            var id = property.Value.GetInt32();
            tokenizer._vocab[property.Name] = id;
            tokenizer._idToPiece[id] = property.Name;
        }

        var rank = 0;
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            string pair;
            if (merge.ValueKind == JsonValueKind.Array)
            {
                pair = merge[0].GetString() + " " + merge[1].GetString();
            }
            else
            {
                pair = merge.GetString() ?? string.Empty;
            }

            if (pair.Length > 0 && !tokenizer._ranks.ContainsKey(pair))
            {
                tokenizer._ranks[pair] = rank;
            }

            rank++;
        }

        foreach (var (id, piece) in extras)
        {
            tokenizer._idToPiece[id] = piece;
            tokenizer._vocab[piece] = id;
            tokenizer._specialPieces.Add(piece);
        }

        tokenizer.UnknownId = tokenizer._vocab.TryGetValue("<unk>", out var unk) ? unk : 3;
        tokenizer.EndOfSentenceId = tokenizer._vocab.TryGetValue("</s>", out var eos) ? eos : 2;
        tokenizer.PadId = tokenizer._vocab.TryGetValue("<pad>", out var pad) ? pad : 1;
        tokenizer._specialPieces.Add("<unk>");
        tokenizer._specialPieces.Add("</s>");
        tokenizer._specialPieces.Add("<pad>");
        tokenizer._specialPieces.Add("<s>");
        return tokenizer;
    }

    public List<long> Encode(string text, int appendEos = -1)
    {
        var symbols = Normalize(text).Select(c => c.ToString()).ToList();

        while (symbols.Count > 1)
        {
            var bestRank = int.MaxValue;
            var bestPair = string.Empty;

            for (var i = 0; i + 1 < symbols.Count; i++)
            {
                var key = symbols[i] + " " + symbols[i + 1];
                if (_ranks.TryGetValue(key, out var rankValue) && rankValue < bestRank)
                {
                    bestRank = rankValue;
                    bestPair = key;
                }
            }

            if (bestRank == int.MaxValue)
            {
                break;
            }

            var parts = bestPair.Split(' ');
            var merged = parts[0] + parts[1];
            var next = new List<string>(symbols.Count);
            for (var i = 0; i < symbols.Count; i++)
            {
                if (i + 1 < symbols.Count
                    && symbols[i] == parts[0]
                    && symbols[i + 1] == parts[1])
                {
                    next.Add(merged);
                    i++;
                }
                else
                {
                    next.Add(symbols[i]);
                }
            }

            symbols = next;
        }

        var ids = new List<long>(symbols.Count + 1);
        foreach (var symbol in symbols)
        {
            ids.Add(_vocab.TryGetValue(symbol, out var id) ? id : UnknownId);
        }

        if (appendEos >= 0)
        {
            ids.Add(appendEos);
        }

        return ids;
    }

    public string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null)
    {
        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == EndOfSentenceId || id == PadId || id == UnknownId
                || (skipIds is not null && skipIds.Contains((int)id)))
            {
                continue;
            }

            if (!_idToPiece.TryGetValue((int)id, out var piece))
            {
                continue;
            }

            if (piece.StartsWith("__", StringComparison.Ordinal) && piece.EndsWith("__", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(piece);
        }

        return builder.ToString().Replace('\u2581', ' ').Trim();
    }

    public int IdOf(string piece) => _vocab.TryGetValue(piece, out var id) ? id : -1;

    private static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).Trim();
        var builder = new StringBuilder(normalized.Length + 1);
        builder.Append('\u2581');
        var lastWasSpace = true;

        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append('\u2581');
                    lastWasSpace = true;
                }
            }
            else
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
        }

        return builder.ToString();
    }
}
