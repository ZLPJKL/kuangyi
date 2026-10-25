using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KuangYi.Core.Offline;

public sealed class MarianConfig
{
    public int DecoderStartTokenId { get; init; }

    public int EosTokenId { get; init; }

    public int PadTokenId { get; init; }

    public int MaxLength { get; init; } = 512;

    public int BeamSize { get; init; } = 4;

    public string ModelType { get; init; } = "marian";

    public static MarianConfig Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        return new MarianConfig
        {
            DecoderStartTokenId = root.GetProperty("decoder_start_token_id").GetInt32(),
            EosTokenId = root.GetProperty("eos_token_id").GetInt32(),
            PadTokenId = root.GetProperty("pad_token_id").GetInt32(),
            ModelType = root.TryGetProperty("model_type", out var type) ? type.GetString() ?? "marian" : "marian",
            MaxLength = root.TryGetProperty("max_length", out var max) && max.ValueKind == JsonValueKind.Number
                ? max.GetInt32()
                : 512,
            BeamSize = root.TryGetProperty("num_beams", out var beams) && beams.ValueKind == JsonValueKind.Number
                ? beams.GetInt32()
                : 4,
        };
    }
}

/// <summary>
/// SentencePiece Unigram tokenizer reconstructed from tokenizer.json.
/// Supports the Metaspace pre-tokenization used by Marian/OPUS-MT exports.
/// </summary>
public sealed class UnigramTokenizer : ITextTokenizer
{
    private readonly Dictionary<int, string> _idToPiece = new();
    private readonly Dictionary<string, int> _pieceToId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, float> _scores = new();
    private readonly TrieNode _root = new();

    public int VocabularySize => _idToPiece.Count;

    public int UnknownId { get; private set; }

    public int EndOfSentenceId { get; private set; }

    public int PadId { get; private set; }

    public static UnigramTokenizer Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var model = root.GetProperty("model");
        var tokenizer = new UnigramTokenizer();

        var vocab = model.GetProperty("vocab");
        if (vocab.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var entry in vocab.EnumerateArray())
            {
                tokenizer.Add(entry[0].GetString() ?? string.Empty, entry[1].GetSingle(), index);
                index++;
            }
        }
        else if (vocab.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var property in vocab.EnumerateObject())
            {
                tokenizer.Add(property.Name, property.Value.GetSingle(), index);
                index++;
            }
        }

        if (root.TryGetProperty("added_tokens", out var added))
        {
            foreach (var token in added.EnumerateArray())
            {
                var id = token.GetProperty("id").GetInt32();
                var content = token.GetProperty("content").GetString() ?? string.Empty;
                tokenizer._idToPiece[id] = content;
                tokenizer._pieceToId[content] = id;
                tokenizer._scores[id] = 0f;
            }
        }

        tokenizer.UnknownId = tokenizer.ResolveSpecial("<unk>", 1);
        tokenizer.EndOfSentenceId = tokenizer.ResolveSpecial("</s>", 0);
        tokenizer.PadId = tokenizer.ResolveSpecial("<pad>", 65000);
        tokenizer.BuildTrie();
        return tokenizer;
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

    public string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null)
    {
        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == EndOfSentenceId || id == PadId || (skipIds is not null && skipIds.Contains((int)id)))
            {
                continue;
            }

            if (_idToPiece.TryGetValue((int)id, out var piece))
            {
                if (piece.StartsWith("__", StringComparison.Ordinal) && piece.EndsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(piece);
            }
        }

        return builder.ToString().Replace('\u2581', ' ').Trim();
    }

    public string PieceOf(long id) => _idToPiece.TryGetValue((int)id, out var piece) ? piece : "?";

    public int IdOf(string piece) => _pieceToId.TryGetValue(piece, out var id) ? id : -1;

    private void Add(string piece, float score, int id)
    {
        _idToPiece[id] = piece;
        _pieceToId[piece] = id;
        _scores[id] = score;
    }

    private int ResolveSpecial(string piece, int fallback)
        => _pieceToId.TryGetValue(piece, out var id) ? id : fallback;

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length + 1);
        builder.Append('\u2581');
        var lastWasSpace = true;

        foreach (var ch in text.Trim())
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
        var unkScore = (_scores.Count > 0 ? _scores.Values.Min() : -10.0) - 10.0;

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
                best[i] = unkScore + best[i + 1];
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

    private void BuildTrie()
    {
        foreach (var (piece, id) in _pieceToId)
        {
            if (piece.Length == 0 || piece is "<unk>" or "</s>" or "<pad>" or "<s>")
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

    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children { get; } = new();

        public int PieceId { get; set; } = -1;

        public double Score { get; set; }
    }
}

/// <summary>
/// One OPUS-MT / Marian translation direction running on ONNX Runtime (CPU).
/// Greedy decoding without KV cache: simple, portable, fast enough for screenshot-sized text.
/// </summary>
public sealed class MarianModel : IDisposable
{
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly ITextTokenizer _sourceTokenizer;
    private readonly ITextTokenizer _targetTokenizer;
    private readonly MarianConfig _config;
    private readonly long _decoderStartTokenId;
    private readonly long _sourcePrefixTokenId;
    private readonly long _forcedFirstTokenId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private MarianModel(
        string direction,
        InferenceSession encoder,
        InferenceSession decoder,
        ITextTokenizer sourceTokenizer,
        ITextTokenizer targetTokenizer,
        MarianConfig config,
        long decoderStartTokenId,
        long sourcePrefixTokenId,
        long forcedFirstTokenId)
    {
        Direction = direction;
        _encoder = encoder;
        _decoder = decoder;
        _sourceTokenizer = sourceTokenizer;
        _targetTokenizer = targetTokenizer;
        _config = config;
        _decoderStartTokenId = decoderStartTokenId;
        _sourcePrefixTokenId = sourcePrefixTokenId;
        _forcedFirstTokenId = forcedFirstTokenId;
    }

    public string Direction { get; }

    public bool IsMultilingual => string.Equals(_config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase);

    public int LastStepCount { get; private set; }

    public static MarianModel Load(string directory, string direction)
    {
        var directionName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(directionName))
        {
            directionName = direction;
        }

        var encoderPath = Path.Combine(directory, "encoder_model_quantized.onnx");
        var decoderPath = Path.Combine(directory, "decoder_model_quantized.onnx");
        var configPath = Path.Combine(directory, "config.json");
        var tokenizerPath = Path.Combine(directory, "tokenizer.json");
        var sourceSpmPath = Path.Combine(directory, "source.spm");
        var targetSpmPath = Path.Combine(directory, "target.spm");

        foreach (var path in new[] { encoderPath, decoderPath, configPath })
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("离线模型文件缺失", path);
            }
        }

        var config = MarianConfig.Load(configPath);
        var isMultilingual = string.Equals(config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase);

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 12),
            InterOpNumThreads = 1,
            // The 418M multilingual model is the only one that benefits enough from
            // the arena to justify its memory; keep small Marian sessions lean.
            EnableCpuMemArena = isMultilingual,
            EnableMemoryPattern = true,
        };

        var encoder = new InferenceSession(encoderPath, options);
        var decoder = new InferenceSession(decoderPath, options);

        ITextTokenizer sourceTokenizer;
        ITextTokenizer targetTokenizer;

        if (string.Equals(config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(tokenizerPath))
            {
                throw new FileNotFoundException("M2M 模型缺少 tokenizer.json", tokenizerPath);
            }

            var extras = ReadAddedTokens(tokenizerPath);
            var shared = BpeTokenizer.Load(tokenizerPath, extras);
            sourceTokenizer = shared;
            targetTokenizer = shared;
        }
        else if (File.Exists(sourceSpmPath) && File.Exists(targetSpmPath))
        {
            sourceTokenizer = SentencePieceTokenizer.Load(sourceSpmPath);
            targetTokenizer = SentencePieceTokenizer.Load(targetSpmPath);
        }
        else if (File.Exists(tokenizerPath))
        {
            var shared = UnigramTokenizer.Load(tokenizerPath);
            sourceTokenizer = shared;
            targetTokenizer = shared;
        }
        else
        {
            throw new FileNotFoundException("未找到 tokenizer.json 或 source.spm/target.spm", tokenizerPath);
        }

        var decoderStart = (long)config.DecoderStartTokenId;
        long sourcePrefix = -1;
        long forcedFirst = -1;

        if (string.Equals(config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase))
        {
            var segments = directionName.Split('-');
            if (segments.Length == 2)
            {
                sourcePrefix = sourceTokenizer.IdOf($"__{segments[0]}__");
                forcedFirst = targetTokenizer.IdOf($"__{segments[1]}__");
            }
        }

        return new MarianModel(
            directionName,
            encoder,
            decoder,
            sourceTokenizer,
            targetTokenizer,
            config,
            decoderStart,
            sourcePrefix,
            forcedFirst);
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var sourcePrefix = _sourcePrefixTokenId;
            var forcedFirst = _forcedFirstTokenId;

            if (string.Equals(_config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase))
            {
                var dynamicPrefix = _sourceTokenizer.IdOf($"__{sourceLanguage}__");
                if (dynamicPrefix >= 0)
                {
                    sourcePrefix = dynamicPrefix;
                }

                var dynamicFirst = _targetTokenizer.IdOf($"__{targetLanguage}__");
                if (dynamicFirst >= 0)
                {
                    forcedFirst = dynamicFirst;
                }
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            var paragraphs = SplitParagraphs(normalized);

            if (paragraphs.Count == 0)
            {
                return string.Empty;
            }

            var separator = targetLanguage is "zh" or "ja" or "ko" ? string.Empty : " ";
            var translatedParagraphs = new List<string>();

            foreach (var paragraph in paragraphs)
            {
                var pieces = new List<string>();
                foreach (var chunk in Chunk(paragraph))
                {
                    ct.ThrowIfCancellationRequested();
                    var current = chunk;
                    pieces.Add(await Task.Run(
                        () => TranslateChunk(current, sourcePrefix, forcedFirst, ct),
                        ct).ConfigureAwait(false));
                }

                translatedParagraphs.Add(string.Join(separator, pieces.Where(p => p.Length > 0)));
            }

            return CollapseRepeatedOutput(
                string.Join("\n\n", translatedParagraphs.Where(p => p.Length > 0)),
                targetLanguage,
                normalized);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string TranslateChunk(string text, long sourcePrefixToken, long forcedFirstToken, CancellationToken ct)
    {
        var sourceIds = _sourceTokenizer.Encode(text, _config.EosTokenId);
        if (sourcePrefixToken >= 0)
        {
            sourceIds.Insert(0, sourcePrefixToken);
        }

        if (sourceIds.Count > 480)
        {
            sourceIds = sourceIds.Take(480).ToList();
            sourceIds[^1] = _config.EosTokenId;
        }

        var sourceLength = sourceIds.Count;
        var encoderIds = new DenseTensor<long>(new[] { 1, sourceLength });
        var mask = new DenseTensor<long>(new[] { 1, sourceLength });
        for (var i = 0; i < sourceLength; i++)
        {
            encoderIds[0, i] = sourceIds[i];
            mask[0, i] = 1;
        }

        using var encoderOutputs = _encoder.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", encoderIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        });

        var hidden = encoderOutputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();
        var hiddenSize = (int)hidden.Dimensions[2];

        var beamSize = 1;
        var startIds = new List<long> { _decoderStartTokenId };
        if (forcedFirstToken >= 0)
        {
            startIds.Add(forcedFirstToken);
        }

        var maxNew = Math.Clamp(sourceLength * 2 + 8, 24, 120);

        if (beamSize == 1)
        {
            return DecodeGreedy(hidden, hiddenSize, sourceLength, startIds, maxNew, ct);
        }

        var active = new List<Beam> { new(startIds, 0) };
        var finished = new List<Beam>();
        LastStepCount = 0;

        for (var step = 0; step < maxNew && active.Count > 0; step++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = active.Count;
            var length = active[0].Ids.Count;
            var decoderIds = new DenseTensor<long>(new[] { batch, length });
            var maskBatch = new DenseTensor<long>(new[] { batch, sourceLength });
            var hiddenBatch = new DenseTensor<float>(new[] { batch, sourceLength, hiddenSize });

            for (var b = 0; b < batch; b++)
            {
                for (var i = 0; i < length; i++)
                {
                    decoderIds[b, i] = active[b].Ids[i];
                }

                for (var i = 0; i < sourceLength; i++)
                {
                    maskBatch[b, i] = 1;
                    for (var d = 0; d < hiddenSize; d++)
                    {
                        hiddenBatch[b, i, d] = hidden[0, i, d];
                    }
                }
            }

            using var outputs = _decoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", decoderIds),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskBatch),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenBatch),
            });

            var logits = outputs.First(o => o.Name == "logits").AsTensor<float>();
            var vocab = (int)logits.Dimensions[2];
            var sequence = (int)logits.Dimensions[1];
            var candidates = new List<Beam>(batch * beamSize);

            for (var b = 0; b < batch; b++)
            {
                var row = sequence - 1;
                var maxLogit = float.NegativeInfinity;
                for (var token = 0; token < vocab; token++)
                {
                    if (token == _config.PadTokenId)
                    {
                        continue;
                    }

                    var value = logits[b, row, token];
                    if (value > maxLogit)
                    {
                        maxLogit = value;
                    }
                }

                var sum = 0.0;
                for (var token = 0; token < vocab; token++)
                {
                    if (token == _config.PadTokenId)
                    {
                        continue;
                    }

                    sum += Math.Exp(logits[b, row, token] - maxLogit);
                }

                var logSum = Math.Log(sum);
                var top = new List<(int Token, double LogProb)>(beamSize + 1);

                for (var token = 0; token < vocab; token++)
                {
                    if (token == _config.PadTokenId || IntroducesRepeatedBigram(active[b].Ids, token))
                    {
                        continue;
                    }

                    var logProb = logits[b, row, token] - maxLogit - logSum;
                    if (top.Count < beamSize)
                    {
                        top.Add((token, logProb));
                        continue;
                    }

                    var worst = 0;
                    for (var i = 1; i < top.Count; i++)
                    {
                        if (top[i].LogProb < top[worst].LogProb)
                        {
                            worst = i;
                        }
                    }

                    if (logProb > top[worst].LogProb)
                    {
                        top[worst] = (token, logProb);
                    }
                }

                foreach (var (token, logProb) in top)
                {
                    var ids = new List<long>(active[b].Ids) { token };
                    candidates.Add(new Beam(ids, active[b].Score + logProb));
                }
            }

            candidates.Sort((x, y) => y.Score.CompareTo(x.Score));

            var next = new List<Beam>();
            foreach (var candidate in candidates)
            {
                if (candidate.Ids[^1] == _config.EosTokenId)
                {
                    finished.Add(candidate);
                }
                else if (next.Count < beamSize)
                {
                    next.Add(candidate);
                }

                if (finished.Count >= beamSize && next.Count >= beamSize)
                {
                    break;
                }
            }

            active = next;
            LastStepCount++;

            if (finished.Count >= beamSize && step >= 6)
            {
                break;
            }
        }

        var best = finished
                       .OrderByDescending(b => b.Score / Math.Max(1, b.Ids.Count - 1))
                       .FirstOrDefault()
                   ?? active.OrderByDescending(b => b.Score / Math.Max(1, b.Ids.Count - 1)).FirstOrDefault();

        if (best is null)
        {
            return string.Empty;
        }

        var skip = new HashSet<int> { _config.EosTokenId, _config.PadTokenId, _targetTokenizer.UnknownId };
        return _targetTokenizer.Decode(best.Ids.Skip(1), skip);
    }

    private string DecodeGreedy(
        Tensor<float> hidden,
        int hiddenSize,
        int sourceLength,
        List<long> startIds,
        int maxNew,
        CancellationToken ct)
    {
        LastStepCount = 0;
        var ids = startIds;
        var maskBatch = new DenseTensor<long>(new[] { 1, sourceLength });
        var hiddenBatch = new DenseTensor<float>(new[] { 1, sourceLength, hiddenSize });
        for (var i = 0; i < sourceLength; i++)
        {
            maskBatch[0, i] = 1;
            for (var d = 0; d < hiddenSize; d++)
            {
                hiddenBatch[0, i, d] = hidden[0, i, d];
            }
        }

        for (var step = 0; step < maxNew; step++)
        {
            ct.ThrowIfCancellationRequested();

            var length = ids.Count;
            var decoderIds = new DenseTensor<long>(new[] { 1, length });
            for (var i = 0; i < length; i++)
            {
                decoderIds[0, i] = ids[i];
            }

            using var outputs = _decoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", decoderIds),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskBatch),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenBatch),
            });

            var logits = outputs.First(o => o.Name == "logits").AsTensor<float>();
            var vocab = (int)logits.Dimensions[2];
            var row = (int)logits.Dimensions[1] - 1;

            var bestToken = -1;
            var bestLogit = float.NegativeInfinity;
            for (var token = 0; token < vocab; token++)
            {
                if (token == _config.PadTokenId || IntroducesRepeatedBigram(ids, token))
                {
                    continue;
                }

                var value = logits[0, row, token];
                if (value > bestLogit)
                {
                    bestLogit = value;
                    bestToken = token;
                }
            }

            if (bestToken < 0)
            {
                break;
            }

            ids.Add(bestToken);
            LastStepCount++;

            if (bestToken == _config.EosTokenId)
            {
                break;
            }
        }

        var skip = new HashSet<int> { _config.EosTokenId, _config.PadTokenId, _targetTokenizer.UnknownId };
        return _targetTokenizer.Decode(ids.Skip(1), skip);
    }

    private sealed class Beam
    {
        public Beam(List<long> ids, double score)
        {
            Ids = ids;
            Score = score;
        }

        public List<long> Ids { get; }

        public double Score { get; }
    }

    private static bool IntroducesRepeatedBigram(List<long> ids, int token)
    {
        var count = ids.Count;
        if (count == 0)
        {
            return false;
        }

        var last = ids[count - 1];
        if (last == token)
        {
            return true;
        }

        if (count < 2)
        {
            return false;
        }

        for (var i = 0; i + 1 < count; i++)
        {
            if (ids[i] == last && ids[i + 1] == token)
            {
                return true;
            }
        }

        return false;
    }

    private static string CollapseRepeatedOutput(string text, string targetLanguage, string sourceText)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 80 || trimmed.Contains("\n\n"))
        {
            return text;
        }

        if (targetLanguage is "zh" or "ja"
            && IsSingleSourceWord(sourceText)
            && trimmed.Any(char.IsWhiteSpace))
        {
            var first = trimmed
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(first) && first.Length < trimmed.Length)
            {
                return first;
            }
        }

        if (targetLanguage is "zh" or "ja" or "ko")
        {
            var repeated = Regex.Match(
                trimmed,
                @"^(.{1,16}?)(?:\s*\1)+$",
                RegexOptions.Singleline);
            if (repeated.Success)
            {
                return repeated.Groups[1].Value.Trim();
            }
        }
        else
        {
            var collapsed = Regex.Replace(
                trimmed,
                @"(?i)\b([\p{L}\p{N}]+)(?:\s+\1\b)+",
                "$1");
            if (!string.Equals(collapsed, trimmed, StringComparison.Ordinal))
            {
                return collapsed;
            }
        }

        return text;
    }

    private static bool IsSingleSourceWord(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 0
               && trimmed.Length <= 24
               && !trimmed.Any(char.IsWhiteSpace);
    }

    private static List<string> SplitParagraphs(string text)
    {
        var explicitParagraphs = text
            .Split(new[] { "\n\n" }, StringSplitOptions.None)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (explicitParagraphs.Count > 1)
        {
            return explicitParagraphs;
        }

        var sentences = SplitSentences(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (sentences.Count < 3)
        {
            return explicitParagraphs;
        }

        // Screenshot OCR often returns one long line without blank lines. Group every
        // two sentences so the result still reads as paragraphs instead of one block.
        var paragraphs = new List<string>();
        for (var i = 0; i < sentences.Count; i += 2)
        {
            var first = sentences[i];
            var block = first;
            if (i + 1 < sentences.Count)
            {
                var second = sentences[i + 1];
                var separator = IsCjkNoSpace(first[^1]) || IsCjkNoSpace(second[0]) ? string.Empty : " ";
                block += separator + second;
            }

            block = block.Trim();
            if (block.Length > 0)
            {
                paragraphs.Add(block);
            }
        }

        return paragraphs;
    }

    private static bool IsCjkNoSpace(char c)
        => (c >= 0x3040 && c <= 0x30FF)
           || (c >= 0x3400 && c <= 0x4DBF)
           || (c >= 0x4E00 && c <= 0x9FFF)
           || (c >= 0x3000 && c <= 0x303F)
           || (c >= 0xFF00 && c <= 0xFFEF);

    private IEnumerable<string> Chunk(string text)
    {
        var normalized = string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (normalized.Length == 0)
        {
            yield break;
        }

        const int maxChars = 180;
        var buffer = new StringBuilder();

        foreach (var sentence in SplitSentences(normalized))
        {
            if (sentence.Length > maxChars)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                for (var offset = 0; offset < sentence.Length; offset += maxChars)
                {
                    yield return sentence.Substring(offset, Math.Min(maxChars, sentence.Length - offset));
                }

                continue;
            }

            if (buffer.Length + sentence.Length > maxChars && buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }

            buffer.Append(sentence);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is '。' or '！' or '？' or '!' or '?' or ';' or '；' or '…' or '.')
            {
                if (ch == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))
                {
                    continue;
                }

                yield return text.Substring(start, i - start + 1);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text.Substring(start);
        }
    }

    private static List<(int Id, string Piece)> ReadAddedTokens(string tokenizerPath)
    {
        var result = new List<(int Id, string Piece)>();
        if (!File.Exists(tokenizerPath))
        {
            return result;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
        if (!doc.RootElement.TryGetProperty("added_tokens", out var added))
        {
            return result;
        }

        foreach (var token in added.EnumerateArray())
        {
            result.Add((
                token.GetProperty("id").GetInt32(),
                token.GetProperty("content").GetString() ?? string.Empty));
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _encoder.Dispose();
        _decoder.Dispose();
        _gate.Dispose();
    }
}
