using System.Linq;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services;

public static class LanguageDetector
{
	public static DetectionResult Detect(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return DetectionResult.Unknown;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsDigit(c))
			{
				if (InRange(c, 19968, 40959) || InRange(c, 13312, 19903))
				{
					num++;
				}
				else if (InRange(c, 12352, 12447))
				{
					num2++;
				}
				else if (InRange(c, 12448, 12543))
				{
					num3++;
				}
				else if (InRange(c, 44032, 55203) || InRange(c, 4352, 4607))
				{
					num4++;
				}
				else if (InRange(c, 1024, 1279))
				{
					num5++;
				}
				else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || InRange(c, 192, 591))
				{
					num6++;
				}
				else
				{
					num7++;
				}
			}
		}
		int num8 = num2 + num3;
		if (num + num8 + num4 + num5 + num6 == 0)
		{
			return DetectionResult.Unknown;
		}
		if (num8 >= 2 || (num8 > 0 && num > 0 && num2 > 0))
		{
			return new DetectionResult(Languages.Japanese, 0.95, IsConfident: true);
		}
		if (num4 >= 2)
		{
			return new DetectionResult(Languages.Korean, 0.95, IsConfident: true);
		}
		if (num >= 1 && num8 == 0 && num4 == 0)
		{
			double confidence = ((num >= 4) ? 0.85 : 0.6);
			return new DetectionResult(Languages.Chinese, confidence, num >= 4);
		}
		if (num5 > 0 && num5 >= num6)
		{
			return new DetectionResult(Languages.Russian, 0.9, num5 >= 3);
		}
		if (num6 > 0 && num == 0 && num8 == 0 && num4 == 0 && num5 == 0)
		{
			return new DetectionResult(Languages.English, 0.9, num6 >= 3);
		}
		return new DetectionResult(new (LanguageInfo, int)[5]
		{
			(Languages.Chinese, num),
			(Languages.Japanese, num8),
			(Languages.Korean, num4),
			(Languages.Russian, num5),
			(Languages.English, num6)
		}.OrderByDescending(((LanguageInfo Language, int Count) x) => x.Count).First().Item1, 0.5, IsConfident: false);
	}

	private static bool InRange(char c, int from, int to)
	{
		if (c >= from)
		{
			return c <= to;
		}
		return false;
	}
}
