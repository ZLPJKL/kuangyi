using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KuangYi.Core.Offline;

public sealed class MarianModel : IDisposable
{
	private sealed class Beam
	{
		public List<long> Ids { get; }

		public double Score { get; }

		public Beam(List<long> ids, double score)
		{
			Ids = ids;
			Score = score;
		}
	}

	private readonly InferenceSession _encoder;

	private readonly InferenceSession _decoder;

	private readonly ITextTokenizer _sourceTokenizer;

	private readonly ITextTokenizer _targetTokenizer;

	private readonly MarianConfig _config;

	private readonly long _decoderStartTokenId;

	private readonly long _sourcePrefixTokenId;

	private readonly long _forcedFirstTokenId;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private bool _disposed;

	public string Direction { get; }

	public bool IsMultilingual => string.Equals(_config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase);

	public int LastStepCount { get; private set; }

	private MarianModel(string direction, InferenceSession encoder, InferenceSession decoder, ITextTokenizer sourceTokenizer, ITextTokenizer targetTokenizer, MarianConfig config, long decoderStartTokenId, long sourcePrefixTokenId, long forcedFirstTokenId)
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

	public static MarianModel Load(string directory, string direction)
	{
		string text = Path.GetFileName(directory.TrimEnd(new char[2]
		{
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar
		}));
		if (string.IsNullOrEmpty(text))
		{
			text = direction;
		}
		string text2 = Path.Combine(directory, "encoder_model_quantized.onnx");
		string text3 = Path.Combine(directory, "decoder_model_quantized.onnx");
		string text4 = Path.Combine(directory, "config.json");
		string text5 = Path.Combine(directory, "tokenizer.json");
		string path = Path.Combine(directory, "source.spm");
		string path2 = Path.Combine(directory, "target.spm");
		string[] array = new string[3] { text2, text3, text4 };
		foreach (string text6 in array)
		{
			if (!File.Exists(text6))
			{
				throw new FileNotFoundException("离线模型文件缺失", text6);
			}
		}
		MarianConfig marianConfig = MarianConfig.Load(text4);
		bool enableCpuMemArena = string.Equals(marianConfig.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase);
		SessionOptions options = new SessionOptions
		{
			GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
			IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 12),
			InterOpNumThreads = 1,
			EnableCpuMemArena = enableCpuMemArena,
			EnableMemoryPattern = true
		};
		InferenceSession encoder = new InferenceSession(text2, options);
		InferenceSession decoder = new InferenceSession(text3, options);
		ITextTokenizer textTokenizer;
		ITextTokenizer textTokenizer2;
		if (string.Equals(marianConfig.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase))
		{
			if (!File.Exists(text5))
			{
				throw new FileNotFoundException("M2M 模型缺少 tokenizer.json", text5);
			}
			List<(int, string)> extras = ReadAddedTokens(text5);
			textTokenizer = (textTokenizer2 = BpeTokenizer.Load(text5, extras));
		}
		else if (File.Exists(path) && File.Exists(path2))
		{
			textTokenizer2 = SentencePieceTokenizer.Load(path);
			textTokenizer = SentencePieceTokenizer.Load(path2);
		}
		else
		{
			if (!File.Exists(text5))
			{
				throw new FileNotFoundException("未找到 tokenizer.json 或 source.spm/target.spm", text5);
			}
			textTokenizer = (textTokenizer2 = UnigramTokenizer.Load(text5));
		}
		long decoderStartTokenId = marianConfig.DecoderStartTokenId;
		long sourcePrefixTokenId = -1L;
		long forcedFirstTokenId = -1L;
		if (string.Equals(marianConfig.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase))
		{
			string[] array2 = text.Split('-');
			if (array2.Length == 2)
			{
				sourcePrefixTokenId = textTokenizer2.IdOf("__" + array2[0] + "__");
				forcedFirstTokenId = textTokenizer.IdOf("__" + array2[1] + "__");
			}
		}
		return new MarianModel(text, encoder, decoder, textTokenizer2, textTokenizer, marianConfig, decoderStartTokenId, sourcePrefixTokenId, forcedFirstTokenId);
	}

	public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			long sourcePrefix = _sourcePrefixTokenId;
			long forcedFirst = _forcedFirstTokenId;
			if (string.Equals(_config.ModelType, "m2m_100", StringComparison.OrdinalIgnoreCase))
			{
				int num = _sourceTokenizer.IdOf("__" + sourceLanguage + "__");
				if (num >= 0)
				{
					sourcePrefix = num;
				}
				int num2 = _targetTokenizer.IdOf("__" + targetLanguage + "__");
				if (num2 >= 0)
				{
					forcedFirst = num2;
				}
			}
			string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
			if (normalized.Length == 0)
			{
				return string.Empty;
			}
			List<string> list = SplitParagraphs(normalized);
			if (list.Count == 0)
			{
				return string.Empty;
			}
			bool flag;
			switch (targetLanguage)
			{
			case "zh":
			case "ja":
			case "ko":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			string separator = (flag ? string.Empty : " ");
			List<string> translatedParagraphs = new List<string>();
			foreach (string item in list)
			{
				List<string> pieces = new List<string>();
				foreach (string item2 in Chunk(item))
				{
					ct.ThrowIfCancellationRequested();
					string current3 = item2;
					List<string> list2 = pieces;
					list2.Add(await Task.Run(() => TranslateChunk(current3, sourcePrefix, forcedFirst, ct), ct).ConfigureAwait(continueOnCapturedContext: false));
				}
				translatedParagraphs.Add(string.Join(separator, pieces.Where((string p) => p.Length > 0)));
			}
			return CollapseRepeatedOutput(string.Join("\n\n", translatedParagraphs.Where((string p) => p.Length > 0)), targetLanguage, normalized);
		}
		finally
		{
			_gate.Release();
		}
	}

	private string TranslateChunk(string text, long sourcePrefixToken, long forcedFirstToken, CancellationToken ct)
	{
		List<long> list = _sourceTokenizer.Encode(text, _config.EosTokenId);
		if (sourcePrefixToken >= 0)
		{
			list.Insert(0, sourcePrefixToken);
		}
		if (list.Count > 480)
		{
			list = list.Take(480).ToList();
			List<long> list2 = list;
			list2[list2.Count - 1] = _config.EosTokenId;
		}
		int count = list.Count;
		DenseTensor<long> denseTensor = new DenseTensor<long>(new int[2] { 1, count });
		DenseTensor<long> denseTensor2 = new DenseTensor<long>(new int[2] { 1, count });
		for (int i = 0; i < count; i++)
		{
			denseTensor[new int[2] { 0, i }] = list[i];
			denseTensor2[new int[2] { 0, i }] = 1L;
		}
		using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> source = _encoder.Run(new NamedOnnxValue[2]
		{
			NamedOnnxValue.CreateFromTensor("input_ids", denseTensor),
			NamedOnnxValue.CreateFromTensor("attention_mask", denseTensor2)
		});
		Tensor<float> tensor = source.First((DisposableNamedOnnxValue o) => o.Name == "last_hidden_state").AsTensor<float>();
		int num = tensor.Dimensions[2];
		int num2 = 1;
		List<long> list3 = new List<long> { _decoderStartTokenId };
		if (forcedFirstToken >= 0)
		{
			list3.Add(forcedFirstToken);
		}
		int num3 = Math.Clamp(count * 2 + 8, 24, 120);
		if (num2 == 1)
		{
			return DecodeGreedy(tensor, num, count, list3, num3, ct);
		}
		List<Beam> list4 = new List<Beam>
		{
			new Beam(list3, 0.0)
		};
		List<Beam> list5 = new List<Beam>();
		LastStepCount = 0;
		for (int num4 = 0; num4 < num3; num4++)
		{
			if (list4.Count <= 0)
			{
				break;
			}
			ct.ThrowIfCancellationRequested();
			int count2 = list4.Count;
			int count3 = list4[0].Ids.Count;
			DenseTensor<long> denseTensor3 = new DenseTensor<long>(new int[2] { count2, count3 });
			DenseTensor<long> denseTensor4 = new DenseTensor<long>(new int[2] { count2, count });
			DenseTensor<float> denseTensor5 = new DenseTensor<float>(new int[3] { count2, count, num });
			for (int num5 = 0; num5 < count2; num5++)
			{
				for (int num6 = 0; num6 < count3; num6++)
				{
					denseTensor3[new int[2] { num5, num6 }] = list4[num5].Ids[num6];
				}
				for (int num7 = 0; num7 < count; num7++)
				{
					denseTensor4[new int[2] { num5, num7 }] = 1L;
					for (int num8 = 0; num8 < num; num8++)
					{
						denseTensor5[new int[3] { num5, num7, num8 }] = tensor[new int[3] { 0, num7, num8 }];
					}
				}
			}
			using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> source2 = _decoder.Run(new NamedOnnxValue[3]
			{
				NamedOnnxValue.CreateFromTensor("input_ids", denseTensor3),
				NamedOnnxValue.CreateFromTensor("encoder_attention_mask", denseTensor4),
				NamedOnnxValue.CreateFromTensor("encoder_hidden_states", denseTensor5)
			});
			Tensor<float> tensor2 = source2.First((DisposableNamedOnnxValue o) => o.Name == "logits").AsTensor<float>();
			int num9 = tensor2.Dimensions[2];
			int num10 = tensor2.Dimensions[1];
			List<Beam> list6 = new List<Beam>(count2 * num2);
			for (int num11 = 0; num11 < count2; num11++)
			{
				int num12 = num10 - 1;
				float num13 = float.NegativeInfinity;
				for (int num14 = 0; num14 < num9; num14++)
				{
					if (num14 != _config.PadTokenId)
					{
						float num15 = tensor2[new int[3] { num11, num12, num14 }];
						if (num15 > num13)
						{
							num13 = num15;
						}
					}
				}
				double num16 = 0.0;
				for (int num17 = 0; num17 < num9; num17++)
				{
					if (num17 != _config.PadTokenId)
					{
						num16 += Math.Exp(tensor2[new int[3] { num11, num12, num17 }] - num13);
					}
				}
				double num18 = Math.Log(num16);
				List<(int, double)> list7 = new List<(int, double)>(num2 + 1);
				for (int num19 = 0; num19 < num9; num19++)
				{
					if (num19 == _config.PadTokenId || IntroducesRepeatedBigram(list4[num11].Ids, num19))
					{
						continue;
					}
					double num20 = (double)(tensor2[new int[3] { num11, num12, num19 }] - num13) - num18;
					if (list7.Count < num2)
					{
						list7.Add((num19, num20));
						continue;
					}
					int index = 0;
					for (int num21 = 1; num21 < list7.Count; num21++)
					{
						if (list7[num21].Item2 < list7[index].Item2)
						{
							index = num21;
						}
					}
					if (num20 > list7[index].Item2)
					{
						list7[index] = (num19, num20);
					}
				}
				foreach (var item3 in list7)
				{
					int item = item3.Item1;
					double item2 = item3.Item2;
					List<long> ids = new List<long>(list4[num11].Ids) { item };
					list6.Add(new Beam(ids, list4[num11].Score + item2));
				}
			}
			list6.Sort((Beam x, Beam y) => y.Score.CompareTo(x.Score));
			List<Beam> list8 = new List<Beam>();
			foreach (Beam item4 in list6)
			{
				List<long> ids2 = item4.Ids;
				if (ids2[ids2.Count - 1] == _config.EosTokenId)
				{
					list5.Add(item4);
				}
				else if (list8.Count < num2)
				{
					list8.Add(item4);
				}
				if (list5.Count >= num2 && list8.Count >= num2)
				{
					break;
				}
			}
			list4 = list8;
			LastStepCount++;
			if (list5.Count >= num2 && num4 >= 6)
			{
				break;
			}
		}
		Beam beam = list5.OrderByDescending((Beam b) => b.Score / (double)Math.Max(1, b.Ids.Count - 1)).FirstOrDefault() ?? list4.OrderByDescending((Beam b) => b.Score / (double)Math.Max(1, b.Ids.Count - 1)).FirstOrDefault();
		if (beam == null)
		{
			return string.Empty;
		}
		HashSet<int> skipIds = new HashSet<int> { _config.EosTokenId, _config.PadTokenId, _targetTokenizer.UnknownId };
		return _targetTokenizer.Decode(beam.Ids.Skip(1), skipIds);
	}

	private string DecodeGreedy(Tensor<float> hidden, int hiddenSize, int sourceLength, List<long> startIds, int maxNew, CancellationToken ct)
	{
		LastStepCount = 0;
		DenseTensor<long> denseTensor = new DenseTensor<long>(new int[2] { 1, sourceLength });
		DenseTensor<float> denseTensor2 = new DenseTensor<float>(new int[3] { 1, sourceLength, hiddenSize });
		for (int i = 0; i < sourceLength; i++)
		{
			denseTensor[new int[2] { 0, i }] = 1L;
			for (int j = 0; j < hiddenSize; j++)
			{
				denseTensor2[new int[3] { 0, i, j }] = hidden[new int[3] { 0, i, j }];
			}
		}
		for (int k = 0; k < maxNew; k++)
		{
			ct.ThrowIfCancellationRequested();
			int count = startIds.Count;
			DenseTensor<long> denseTensor3 = new DenseTensor<long>(new int[2] { 1, count });
			for (int l = 0; l < count; l++)
			{
				denseTensor3[new int[2] { 0, l }] = startIds[l];
			}
			using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> source = _decoder.Run(new NamedOnnxValue[3]
			{
				NamedOnnxValue.CreateFromTensor("input_ids", denseTensor3),
				NamedOnnxValue.CreateFromTensor("encoder_attention_mask", denseTensor),
				NamedOnnxValue.CreateFromTensor("encoder_hidden_states", denseTensor2)
			}))
			{
				Tensor<float> tensor = source.First((DisposableNamedOnnxValue o) => o.Name == "logits").AsTensor<float>();
				int num = tensor.Dimensions[2];
				int num2 = tensor.Dimensions[1] - 1;
				int num3 = -1;
				float num4 = float.NegativeInfinity;
				for (int num5 = 0; num5 < num; num5++)
				{
					if (num5 != _config.PadTokenId && !IntroducesRepeatedBigram(startIds, num5))
					{
						float num6 = tensor[new int[3] { 0, num2, num5 }];
						if (num6 > num4)
						{
							num4 = num6;
							num3 = num5;
						}
					}
				}
				if (num3 >= 0)
				{
					startIds.Add(num3);
					LastStepCount++;
					if (num3 != _config.EosTokenId)
					{
						continue;
					}
				}
			}
			break;
		}
		HashSet<int> skipIds = new HashSet<int> { _config.EosTokenId, _config.PadTokenId, _targetTokenizer.UnknownId };
		return _targetTokenizer.Decode(startIds.Skip(1), skipIds);
	}

	private static bool IntroducesRepeatedBigram(List<long> ids, int token)
	{
		int count = ids.Count;
		if (count == 0)
		{
			return false;
		}
		long num = ids[count - 1];
		if (num == token)
		{
			return true;
		}
		if (count < 2)
		{
			return false;
		}
		for (int i = 0; i + 1 < count; i++)
		{
			if (ids[i] == num && ids[i + 1] == token)
			{
				return true;
			}
		}
		return false;
	}

	private static string CollapseRepeatedOutput(string text, string targetLanguage, string sourceText)
	{
		string text2 = text.Trim();
		if (text2.Length == 0 || text2.Length > 80 || text2.Contains("\n\n"))
		{
			return text;
		}
		if ((targetLanguage == "zh" || targetLanguage == "ja") && IsSingleSourceWord(sourceText) && text2.Any(char.IsWhiteSpace))
		{
			string text3 = text2.Split(new char[4] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
			if (!string.IsNullOrEmpty(text3) && text3.Length < text2.Length)
			{
				return text3;
			}
		}
		bool flag;
		switch (targetLanguage)
		{
		case "zh":
		case "ja":
		case "ko":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			Match match = Regex.Match(text2, "^(.{1,16}?)(?:\\s*\\1)+$", RegexOptions.Singleline);
			if (match.Success)
			{
				return match.Groups[1].Value.Trim();
			}
		}
		else
		{
			string text4 = Regex.Replace(text2, "(?i)\\b([\\p{L}\\p{N}]+)(?:\\s+\\1\\b)+", "$1");
			if (!string.Equals(text4, text2, StringComparison.Ordinal))
			{
				return text4;
			}
		}
		return text;
	}

	private static bool IsSingleSourceWord(string text)
	{
		string text2 = text.Trim();
		if (text2.Length > 0 && text2.Length <= 24)
		{
			return !text2.Any(char.IsWhiteSpace);
		}
		return false;
	}

	private static List<string> SplitParagraphs(string text)
	{
		List<string> list = (from p in text.Split(new string[1] { "\n\n" }, StringSplitOptions.None)
			select p.Trim() into p
			where p.Length > 0
			select p).ToList();
		if (list.Count > 1)
		{
			return list;
		}
		List<string> list2 = (from s in SplitSentences(text)
			select s.Trim() into s
			where s.Length > 0
			select s).ToList();
		if (list2.Count < 3)
		{
			return list;
		}
		List<string> list3 = new List<string>();
		for (int num = 0; num < list2.Count; num += 2)
		{
			string text2 = list2[num];
			string text3 = text2;
			if (num + 1 < list2.Count)
			{
				string text4 = list2[num + 1];
				string text5 = ((IsCjkNoSpace(text2[text2.Length - 1]) || IsCjkNoSpace(text4[0])) ? string.Empty : " ");
				text3 = text3 + text5 + text4;
			}
			text3 = text3.Trim();
			if (text3.Length > 0)
			{
				list3.Add(text3);
			}
		}
		return list3;
	}

	private static bool IsCjkNoSpace(char c)
	{
		if ((c < '\u3040' || c > 'ヿ') && (c < '㐀' || c > '䶿') && (c < '一' || c > '鿿') && (c < '\u3000' || c > '〿'))
		{
			if (c >= '\uff00')
			{
				return c <= '\uffef';
			}
			return false;
		}
		return true;
	}

	private IEnumerable<string> Chunk(string text)
	{
		string text2 = string.Join(" ", text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (text2.Length == 0)
		{
			yield break;
		}
		StringBuilder buffer = new StringBuilder();
		foreach (string sentence in SplitSentences(text2))
		{
			if (sentence.Length > 180)
			{
				if (buffer.Length > 0)
				{
					yield return buffer.ToString();
					buffer.Clear();
				}
				for (int offset = 0; offset < sentence.Length; offset += 180)
				{
					yield return sentence.Substring(offset, Math.Min(180, sentence.Length - offset));
				}
			}
			else
			{
				if (buffer.Length + sentence.Length > 180 && buffer.Length > 0)
				{
					yield return buffer.ToString();
					buffer.Clear();
				}
				buffer.Append(sentence);
			}
		}
		if (buffer.Length > 0)
		{
			yield return buffer.ToString();
		}
	}

	private static IEnumerable<string> SplitSentences(string text)
	{
		int num = 0;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			bool flag;
			switch (c)
			{
			case '!':
			case '.':
			case ';':
			case '?':
			case '…':
			case '。':
			case '！':
			case '；':
			case '？':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag && (c != '.' || i + 1 >= text.Length || !char.IsDigit(text[i + 1])))
			{
				yield return text.Substring(num, i - num + 1);
				num = i + 1;
			}
		}
		if (num < text.Length)
		{
			yield return text.Substring(num);
		}
	}

	private static List<(int Id, string Piece)> ReadAddedTokens(string tokenizerPath)
	{
		List<(int, string)> list = new List<(int, string)>();
		if (!File.Exists(tokenizerPath))
		{
			return list;
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
		if (!jsonDocument.RootElement.TryGetProperty("added_tokens", out var value))
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			list.Add((item.GetProperty("id").GetInt32(), item.GetProperty("content").GetString() ?? string.Empty));
		}
		return list;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_encoder.Dispose();
			_decoder.Dispose();
			_gate.Dispose();
		}
	}
}
