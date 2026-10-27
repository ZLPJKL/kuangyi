using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace KuangYi.Core.Services.Translation;

public static class TextChunker
{
	private static readonly Regex SentenceBoundary = new Regex("(?<=[。！？!?；;\\n])", RegexOptions.Compiled);

	public static IReadOnlyList<string> SplitByUtf8Bytes(string text, int maxBytes = 450)
	{
		if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes)
		{
			return new string[1] { text };
		}
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		string[] array = SentenceBoundary.Split(text);
		foreach (string text2 in array)
		{
			if (text2.Length == 0)
			{
				continue;
			}
			int byteCount = Encoding.UTF8.GetByteCount(text2);
			if (byteCount > maxBytes)
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					num = 0;
				}
				foreach (string item in HardSplit(text2, maxBytes))
				{
					list.Add(item);
				}
			}
			else
			{
				if (num + byteCount > maxBytes && stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					num = 0;
				}
				stringBuilder.Append(text2);
				num += byteCount;
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	private static IEnumerable<string> HardSplit(string text, int maxBytes)
	{
		StringBuilder current = new StringBuilder();
		int num = 0;
		foreach (Rune item in text.EnumerateRunes())
		{
			string s = item.ToString();
			int size = Encoding.UTF8.GetByteCount(s);
			if (num + size > maxBytes && current.Length > 0)
			{
				yield return current.ToString();
				current.Clear();
				num = 0;
			}
			current.Append(s);
			num += size;
		}
		if (current.Length > 0)
		{
			yield return current.ToString();
		}
	}
}
