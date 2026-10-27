using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace KuangYi.Core.Offline;

public sealed class BpeTokenizer : ITextTokenizer
{
	private readonly Dictionary<string, int> _vocab = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly Dictionary<int, string> _idToPiece = new Dictionary<int, string>();

	private readonly Dictionary<string, int> _ranks = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly HashSet<string> _specialPieces = new HashSet<string>(StringComparer.Ordinal);

	public int UnknownId { get; private set; } = 3;

	public int EndOfSentenceId { get; private set; } = 2;

	public int PadId { get; private set; } = 1;

	public static BpeTokenizer Load(string tokenizerPath, IEnumerable<(int Id, string Piece)> extras)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
		JsonElement property = jsonDocument.RootElement.GetProperty("model");
		BpeTokenizer bpeTokenizer = new BpeTokenizer();
		foreach (JsonProperty item in property.GetProperty("vocab").EnumerateObject())
		{
			int @int = item.Value.GetInt32();
			bpeTokenizer._vocab[item.Name] = @int;
			bpeTokenizer._idToPiece[@int] = item.Name;
		}
		int num = 0;
		foreach (JsonElement item2 in property.GetProperty("merges").EnumerateArray())
		{
			string text = ((item2.ValueKind != JsonValueKind.Array) ? (item2.GetString() ?? string.Empty) : (item2[0].GetString() + " " + item2[1].GetString()));
			if (text.Length > 0 && !bpeTokenizer._ranks.ContainsKey(text))
			{
				bpeTokenizer._ranks[text] = num;
			}
			num++;
		}
		foreach (var (num2, text2) in extras)
		{
			bpeTokenizer._idToPiece[num2] = text2;
			bpeTokenizer._vocab[text2] = num2;
			bpeTokenizer._specialPieces.Add(text2);
		}
		bpeTokenizer.UnknownId = (bpeTokenizer._vocab.TryGetValue("<unk>", out var value) ? value : 3);
		bpeTokenizer.EndOfSentenceId = (bpeTokenizer._vocab.TryGetValue("</s>", out var value2) ? value2 : 2);
		bpeTokenizer.PadId = ((!bpeTokenizer._vocab.TryGetValue("<pad>", out var value3)) ? 1 : value3);
		bpeTokenizer._specialPieces.Add("<unk>");
		bpeTokenizer._specialPieces.Add("</s>");
		bpeTokenizer._specialPieces.Add("<pad>");
		bpeTokenizer._specialPieces.Add("<s>");
		return bpeTokenizer;
	}

	public List<long> Encode(string text, int appendEos = -1)
	{
		List<string> list = Normalize(text).Select((char c) =>
		{
			char c2 = c;
			return c2.ToString();
		}).ToList();
		while (list.Count > 1)
		{
			int num = int.MaxValue;
			string text2 = string.Empty;
			for (int num2 = 0; num2 + 1 < list.Count; num2++)
			{
				string text3 = list[num2] + " " + list[num2 + 1];
				if (_ranks.TryGetValue(text3, out var value) && value < num)
				{
					num = value;
					text2 = text3;
				}
			}
			if (num == int.MaxValue)
			{
				break;
			}
			string[] array = text2.Split(' ');
			string item = array[0] + array[1];
			List<string> list2 = new List<string>(list.Count);
			for (int num3 = 0; num3 < list.Count; num3++)
			{
				if (num3 + 1 < list.Count && list[num3] == array[0] && list[num3 + 1] == array[1])
				{
					list2.Add(item);
					num3++;
				}
				else
				{
					list2.Add(list[num3]);
				}
			}
			list = list2;
		}
		List<long> list3 = new List<long>(list.Count + 1);
		foreach (string item2 in list)
		{
			list3.Add(_vocab.TryGetValue(item2, out var value2) ? value2 : UnknownId);
		}
		if (appendEos >= 0)
		{
			list3.Add(appendEos);
		}
		return list3;
	}

	public string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (long id in ids)
		{
			if (id != EndOfSentenceId && id != PadId && id != UnknownId && (skipIds == null || !skipIds.Contains((int)id)) && _idToPiece.TryGetValue((int)id, out string value) && (!value.StartsWith("__", StringComparison.Ordinal) || !value.EndsWith("__", StringComparison.Ordinal)))
			{
				stringBuilder.Append(value);
			}
		}
		return stringBuilder.ToString().Replace('▁', ' ').Trim();
	}

	public int IdOf(string piece)
	{
		if (!_vocab.TryGetValue(piece, out var value))
		{
			return -1;
		}
		return value;
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
}
