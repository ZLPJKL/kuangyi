using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace KuangYi.Core.Offline;

public sealed class UnigramTokenizer : ITextTokenizer
{
	private sealed class TrieNode
	{
		public Dictionary<char, TrieNode> Children { get; } = new Dictionary<char, TrieNode>();

		public int PieceId { get; set; } = -1;

		public double Score { get; set; }
	}

	private readonly Dictionary<int, string> _idToPiece = new Dictionary<int, string>();

	private readonly Dictionary<string, int> _pieceToId = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly Dictionary<int, float> _scores = new Dictionary<int, float>();

	private readonly TrieNode _root = new TrieNode();

	public int VocabularySize => _idToPiece.Count;

	public int UnknownId { get; private set; }

	public int EndOfSentenceId { get; private set; }

	public int PadId { get; private set; }

	public static UnigramTokenizer Load(string path)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement property = rootElement.GetProperty("model");
		UnigramTokenizer unigramTokenizer = new UnigramTokenizer();
		JsonElement property2 = property.GetProperty("vocab");
		if (property2.ValueKind == JsonValueKind.Array)
		{
			int num = 0;
			foreach (JsonElement item in property2.EnumerateArray())
			{
				unigramTokenizer.Add(item[0].GetString() ?? string.Empty, item[1].GetSingle(), num);
				num++;
			}
		}
		else if (property2.ValueKind == JsonValueKind.Object)
		{
			int num2 = 0;
			foreach (JsonProperty item2 in property2.EnumerateObject())
			{
				unigramTokenizer.Add(item2.Name, item2.Value.GetSingle(), num2);
				num2++;
			}
		}
		if (rootElement.TryGetProperty("added_tokens", out var value))
		{
			foreach (JsonElement item3 in value.EnumerateArray())
			{
				int @int = item3.GetProperty("id").GetInt32();
				string text = item3.GetProperty("content").GetString() ?? string.Empty;
				unigramTokenizer._idToPiece[@int] = text;
				unigramTokenizer._pieceToId[text] = @int;
				unigramTokenizer._scores[@int] = 0f;
			}
		}
		unigramTokenizer.UnknownId = unigramTokenizer.ResolveSpecial("<unk>", 1);
		unigramTokenizer.EndOfSentenceId = unigramTokenizer.ResolveSpecial("</s>", 0);
		unigramTokenizer.PadId = unigramTokenizer.ResolveSpecial("<pad>", 65000);
		unigramTokenizer.BuildTrie();
		return unigramTokenizer;
	}

	public List<long> Encode(string text, int appendEos = -1)
	{
		List<long> list = Viterbi(Normalize(text));
		if (appendEos >= 0)
		{
			list.Add(appendEos);
		}
		return list;
	}

	public string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (long id in ids)
		{
			if (id != EndOfSentenceId && id != PadId && (skipIds == null || !skipIds.Contains((int)id)) && _idToPiece.TryGetValue((int)id, out string value) && (!value.StartsWith("__", StringComparison.Ordinal) || !value.EndsWith("__", StringComparison.Ordinal)))
			{
				stringBuilder.Append(value);
			}
		}
		return stringBuilder.ToString().Replace('▁', ' ').Trim();
	}

	public string PieceOf(long id)
	{
		if (!_idToPiece.TryGetValue((int)id, out string value))
		{
			return "?";
		}
		return value;
	}

	public int IdOf(string piece)
	{
		if (!_pieceToId.TryGetValue(piece, out var value))
		{
			return -1;
		}
		return value;
	}

	private void Add(string piece, float score, int id)
	{
		_idToPiece[id] = piece;
		_pieceToId[piece] = id;
		_scores[id] = score;
	}

	private int ResolveSpecial(string piece, int fallback)
	{
		if (!_pieceToId.TryGetValue(piece, out var value))
		{
			return fallback;
		}
		return value;
	}

	private static string Normalize(string text)
	{
		StringBuilder stringBuilder = new StringBuilder(text.Length + 1);
		stringBuilder.Append('▁');
		bool flag = true;
		string text2 = text.Trim();
		foreach (char c in text2)
		{
			if (char.IsWhiteSpace(c))
			{
				if (!flag)
				{
					stringBuilder.Append('▁');
					flag = true;
				}
			}
			else
			{
				stringBuilder.Append(c);
				flag = false;
			}
		}
		return stringBuilder.ToString();
	}

	private List<long> Viterbi(string text)
	{
		int length = text.Length;
		double[] array = new double[length + 1];
		int[] array2 = new int[length + 1];
		int[] array3 = new int[length + 1];
		for (int i = 0; i <= length; i++)
		{
			array[i] = double.NegativeInfinity;
			array2[i] = -1;
			array3[i] = -1;
		}
		array[length] = 0.0;
		double num = ((_scores.Count > 0) ? ((double)_scores.Values.Min()) : (-10.0)) - 10.0;
		for (int num2 = length - 1; num2 >= 0; num2--)
		{
			TrieNode trieNode = _root;
			TrieNode value;
			for (int j = num2; j < length && trieNode.Children.TryGetValue(text[j], out value); j++)
			{
				trieNode = value;
				if (trieNode.PieceId >= 0)
				{
					double num3 = trieNode.Score + array[j + 1];
					if (num3 > array[num2])
					{
						array[num2] = num3;
						array2[num2] = j + 1;
						array3[num2] = trieNode.PieceId;
					}
				}
			}
			if (double.IsNegativeInfinity(array[num2]))
			{
				array[num2] = num + array[num2 + 1];
				array2[num2] = num2 + 1;
				array3[num2] = UnknownId;
			}
		}
		List<long> list = new List<long>();
		int num4 = 0;
		while (num4 < length && array2[num4] > num4)
		{
			list.Add(array3[num4]);
			num4 = array2[num4];
		}
		return list;
	}

	private void BuildTrie()
	{
		foreach (KeyValuePair<string, int> item in _pieceToId)
		{
			item.Deconstruct(out var key, out var i);
			string text = key;
			int num = i;
			bool flag = text.Length == 0;
			if (!flag)
			{
				bool flag2;
				switch (text)
				{
				case "<unk>":
				case "</s>":
				case "<pad>":
				case "<s>":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				continue;
			}
			TrieNode trieNode = _root;
			key = text;
			for (i = 0; i < key.Length; i++)
			{
				char key2 = key[i];
				if (!trieNode.Children.TryGetValue(key2, out TrieNode value))
				{
					value = new TrieNode();
					trieNode.Children[key2] = value;
				}
				trieNode = value;
			}
			trieNode.PieceId = num;
			trieNode.Score = (_scores.TryGetValue(num, out var value2) ? value2 : 0f);
		}
	}
}
