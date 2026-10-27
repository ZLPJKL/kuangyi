using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KuangYi.Core.Offline;

public sealed class SentencePieceTokenizer : ITextTokenizer
{
	private sealed class TrieNode
	{
		public Dictionary<char, TrieNode> Children { get; } = new Dictionary<char, TrieNode>();

		public int PieceId { get; set; } = -1;

		public double Score { get; set; }
	}

	private readonly List<string> _pieces = new List<string>();

	private readonly Dictionary<int, float> _scores = new Dictionary<int, float>();

	private readonly Dictionary<string, int> _pieceToId = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly TrieNode _root = new TrieNode();

	public int VocabularySize => _pieces.Count;

	public int UnknownId { get; private set; }

	public int EndOfSentenceId { get; private set; } = 2;

	public int PadId { get; private set; } = -1;

	public IReadOnlyList<string> Pieces => _pieces;

	public IReadOnlyDictionary<int, float> Scores => _scores;

	public static SentencePieceTokenizer Load(string path, IEnumerable<(int Id, string Piece)>? extras = null)
	{
		SentencePieceTokenizer sentencePieceTokenizer = new SentencePieceTokenizer();
		sentencePieceTokenizer.Parse(File.ReadAllBytes(path));
		if (extras != null)
		{
			foreach (var (id, piece) in extras)
			{
				sentencePieceTokenizer.AddToken(id, piece);
			}
		}
		sentencePieceTokenizer.BuildTrie();
		return sentencePieceTokenizer;
	}

	private void AddToken(int id, string piece)
	{
		while (_pieces.Count <= id)
		{
			_pieces.Add("<unused>");
			_scores[_pieces.Count - 1] = 0f;
		}
		_pieces[id] = piece;
		_pieceToId[piece] = id;
		_scores[id] = 0f;
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

	public int IdOf(string piece)
	{
		if (!_pieceToId.TryGetValue(piece, out var value))
		{
			return -1;
		}
		return value;
	}

	public string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (long id in ids)
		{
			if (id >= 0 && id < _pieces.Count && id != EndOfSentenceId && id != PadId && (skipIds == null || !skipIds.Contains((int)id)))
			{
				string text = _pieces[(int)id];
				if (!text.StartsWith("<0x", StringComparison.Ordinal) || !text.EndsWith('>'))
				{
					stringBuilder.Append(text);
				}
			}
		}
		return stringBuilder.ToString().Replace('▁', ' ').Trim();
	}

	private void Parse(byte[] data)
	{
		int position = 0;
		while (position < data.Length)
		{
			ulong num = ReadVarint(data, ref position);
			int num2 = (int)(num >> 3);
			int num3 = (int)(num & 7);
			if (num2 == 1 && num3 == 2)
			{
				int num4 = (int)ReadVarint(data, ref position);
				int num5 = position + num4;
				ParsePiece(data, position, num5);
				position = num5;
			}
			else
			{
				Skip(data, ref position, num3);
			}
		}
		for (int i = 0; i < _pieces.Count; i++)
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
		int position = start;
		string text = string.Empty;
		float value = 0f;
		int num = 1;
		while (position < end)
		{
			ulong num2 = ReadVarint(data, ref position);
			int num3 = (int)(num2 >> 3);
			int num4 = (int)(num2 & 7);
			switch (num3)
			{
			case 1:
				if (num4 == 2)
				{
					int num5 = (int)ReadVarint(data, ref position);
					text = Encoding.UTF8.GetString(data, position, num5);
					position += num5;
					continue;
				}
				break;
			case 2:
				if (num4 == 5)
				{
					value = BitConverter.ToSingle(data, position);
					position += 4;
					continue;
				}
				break;
			case 3:
				if (num4 == 0)
				{
					num = (int)ReadVarint(data, ref position);
					continue;
				}
				break;
			}
			Skip(data, ref position, num4);
		}
		int count = _pieces.Count;
		_pieces.Add(text);
		_scores[count] = value;
		_pieceToId[text] = count;
		if (num == 2)
		{
			UnknownId = count;
		}
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

	private static string Normalize(string text)
	{
		string text2 = text.Normalize(NormalizationForm.FormKC).Trim();
		StringBuilder stringBuilder = new StringBuilder(text2.Length + 1);
		stringBuilder.Append('▁');
		bool flag = true;
		string text3 = text2;
		foreach (char c in text3)
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

	private void BuildTrie()
	{
		foreach (KeyValuePair<string, int> item in _pieceToId)
		{
			item.Deconstruct(out var key, out var i);
			string text = key;
			int num = i;
			if (text.Length == 0)
			{
				continue;
			}
			switch (text)
			{
			case "<unk>":
			case "</s>":
			case "<s>":
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

	private static ulong ReadVarint(byte[] data, ref int position)
	{
		ulong num = 0uL;
		int num2 = 0;
		while (position < data.Length)
		{
			byte b = data[position++];
			num |= (ulong)((long)(b & 0x7F) << num2);
			if ((b & 0x80) == 0)
			{
				break;
			}
			num2 += 7;
		}
		return num;
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
		{
			int num = (int)ReadVarint(data, ref position);
			position += num;
			break;
		}
		case 5:
			position += 4;
			break;
		default:
			position = data.Length;
			break;
		}
	}
}
