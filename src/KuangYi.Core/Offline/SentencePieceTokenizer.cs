using System.Text;

namespace KuangYi.Core.Offline;

/// <summary>
/// Minimal SentencePiece (unigram) reader for OPUS-MT models that ship source.spm / target.spm
/// instead of a HuggingFace tokenizer.json. Parses the ModelProto protobuf directly.
/// </summary>
public sealed class SentencePieceTokenizer : ITextTokenizer
{
    private readonly List<string> _pieces = new();
    private readonly Dictionary<int, float> _scores = new();
    private readonly Dictionary<string, int> _pieceToId = new(StringComparer.Ordinal);
    private readonly TrieNode _root = new();

    public int VocabularySize => _pieces.Count;

    public int UnknownId { get; private set; } = 0;

    public int EndOfSentenceId { get; private set; } = 2;

    public int PadId { get; private set; } = -1;

    public IReadOnlyList<string> Pieces => _pieces;

    public IReadOnlyDictionary<int, float> Scores => _scores;

    public static SentencePieceTokenizer Load(string path, IEnumerable<(int Id, string Piece)>? extras = null)
    {
        var tokenizer = new SentencePieceTokenizer();
        tokenizer.Parse(File.ReadAllBytes(path));

        if (extras is not null)
        {
            foreach (var (id, piece) in extras)
            {
                tokenizer.AddToken(id, piece);
            }
        }

        tokenizer.BuildTrie();
        return tokenizer;
    }

    private void AddToken(int id, string piece)
    {
        while (_pieces.Count <= id)
        {
            _pieces.Add("<unused>");
            _scores[_pieces.Count - 1] = 0;
        }

        _pieces[id] = piece;
        _pieceToId[piece] = id;
        _scores[id] = 0;
    }

    public List<long> Encode(string text, int appendEos = -1)
    {
        var ids = Viterbi(Normalize(text));
        if (appendEos >= 0)
        {
            ids.Add(appendEos);
        }

        return ids;
    }

    public int IdOf(string piece) => _pieceToId.TryGetValue(piece, out var id) ? id : -1;

    public string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null)    {
        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            if (id < 0 || id >= _pieces.Count)
            {
                continue;
            }

            if (id == EndOfSentenceId || id == PadId || (skipIds is not null && skipIds.Contains((int)id)))
            {
                continue;
            }

            var piece = _pieces[(int)id];
            if (piece.StartsWith("<0x", StringComparison.Ordinal) && piece.EndsWith('>'))
            {
                continue;
            }

            builder.Append(piece);
        }

        return builder.ToString().Replace('\u2581', ' ').Trim();
    }

    private void Parse(byte[] data)
    {
        var position = 0;
        while (position < data.Length)
        {
            var tag = ReadVarint(data, ref position);
            var field = (int)(tag >> 3);
            var wire = (int)(tag & 7);

            if (field == 1 && wire == 2)
            {
                var length = (int)ReadVarint(data, ref position);
                var end = position + length;
                ParsePiece(data, position, end);
                position = end;
            }
            else
            {
                Skip(data, ref position, wire);
            }
        }

        for (var i = 0; i < _pieces.Count; i++)
        {
            if (_pieces[i] == "<unk>")
            {
                UnknownId = i;
            }
            else if (_pieces[i] == "</s>")
            {
                EndOfSentenceId = i;
            }
            else if (_pieces[i] == "<pad>")
            {
                PadId = i;
            }
        }
    }

    private void ParsePiece(byte[] data, int start, int end)
    {
        var position = start;
        string piece = string.Empty;
        var score = 0f;
        var type = 1;

        while (position < end)
        {
            var tag = ReadVarint(data, ref position);
            var field = (int)(tag >> 3);
            var wire = (int)(tag & 7);

            switch (field)
            {
                case 1 when wire == 2:
                {
                    var length = (int)ReadVarint(data, ref position);
                    piece = Encoding.UTF8.GetString(data, position, length);
                    position += length;
                    break;
                }

                case 2 when wire == 5:
                    score = BitConverter.ToSingle(data, position);
                    position += 4;
                    break;

                case 3 when wire == 0:
                    type = (int)ReadVarint(data, ref position);
                    break;

                default:
                    Skip(data, ref position, wire);
                    break;
            }
        }

        var id = _pieces.Count;
        _pieces.Add(piece);
        _scores[id] = score;
        _pieceToId[piece] = id;

        if (type == 2)
        {
            UnknownId = id;
        }
    }

    private List<long> Viterbi(string text)
    {
        var n = text.Length;
        var best = new double[n + 1];
        var next = new int[n + 1];
        var bestId = new int[n + 1];

        for (var i = 0; i <= n; i++)
        {
            best[i] = double.NegativeInfinity;
            next[i] = -1;
            bestId[i] = -1;
        }

        best[n] = 0;
        var unkPenalty = (_scores.Count > 0 ? _scores.Values.Min() : -10.0) - 10.0;

        for (var i = n - 1; i >= 0; i--)
        {
            var node = _root;
            for (var j = i; j < n; j++)
            {
                if (!node.Children.TryGetValue(text[j], out var child))
                {
                    break;
                }

                node = child;
                if (node.PieceId >= 0)
                {
                    var candidate = node.Score + best[j + 1];
                    if (candidate > best[i])
                    {
                        best[i] = candidate;
                        next[i] = j + 1;
                        bestId[i] = node.PieceId;
                    }
                }
            }

            if (double.IsNegativeInfinity(best[i]))
            {
                best[i] = unkPenalty + best[i + 1];
                next[i] = i + 1;
                bestId[i] = UnknownId;
            }
        }

        var ids = new List<long>();
        var position = 0;
        while (position < n && next[position] > position)
        {
            ids.Add(bestId[position]);
            position = next[position];
        }

        return ids;
    }

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

    private void BuildTrie()
    {
        foreach (var (piece, id) in _pieceToId)
        {
            if (piece.Length == 0 || piece == "<unk>" || piece == "</s>" || piece == "<s>")
            {
                continue;
            }

            var node = _root;
            foreach (var ch in piece)
            {
                if (!node.Children.TryGetValue(ch, out var child))
                {
                    child = new TrieNode();
                    node.Children[ch] = child;
                }

                node = child;
            }

            node.PieceId = id;
            node.Score = _scores.TryGetValue(id, out var score) ? score : 0;
        }
    }

    private static ulong ReadVarint(byte[] data, ref int position)
    {
        ulong result = 0;
        var shift = 0;
        while (position < data.Length)
        {
            var b = data[position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return result;
    }

    private static void Skip(byte[] data, ref int position, int wire)
    {
        switch (wire)
        {
            case 0:
                ReadVarint(data, ref position);
                break;
            case 1:
                position += 8;
                break;
            case 2:
                var length = (int)ReadVarint(data, ref position);
                position += length;
                break;
            case 5:
                position += 4;
                break;
            default:
                position = data.Length;
                break;
        }
    }

    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children { get; } = new();

        public int PieceId { get; set; } = -1;

        public double Score { get; set; }
    }
}
