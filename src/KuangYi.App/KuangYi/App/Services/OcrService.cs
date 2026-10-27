using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.Core.Services;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace KuangYi.App.Services;

public sealed class OcrService
{
	private readonly record struct ScriptCounts(int Han, int Kana, int Hangul, int Cyrillic, int Latin, int Other)
	{
		public static ScriptCounts Analyze(string text)
		{
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			foreach (char c in text)
			{
				if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsDigit(c))
				{
					if (InRange(c, 19968, 40959) || InRange(c, 13312, 19903))
					{
						num++;
					}
					else if (InRange(c, 12352, 12543))
					{
						num2++;
					}
					else if (InRange(c, 44032, 55203) || InRange(c, 4352, 4607))
					{
						num3++;
					}
					else if (InRange(c, 1024, 1279))
					{
						num4++;
					}
					else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || InRange(c, 192, 591))
					{
						num5++;
					}
					else
					{
						num6++;
					}
				}
			}
			return new ScriptCounts(num, num2, num3, num4, num5, num6);
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

	public IReadOnlyList<string> AvailableLanguageTags => OcrEngine.AvailableRecognizerLanguages.Select((Language l) => l.LanguageTag).ToList();

	public bool HasAnyRecognizer => OcrEngine.AvailableRecognizerLanguages.Count > 0;

	public bool IsLanguageInstalled(string? code)
	{
		if (string.IsNullOrEmpty(code))
		{
			return false;
		}
		string tag = NormalizeTag(code);
		return OcrEngine.AvailableRecognizerLanguages.Any((Language l) => TagMatches(l.LanguageTag, tag));
	}

	public async Task<OcrOutput> RecognizeAsync(BitmapSource image, string? preferredLanguageCode, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		string preferred = (string.IsNullOrWhiteSpace(preferredLanguageCode) ? null : preferredLanguageCode);
		if (preferred != null && !IsLanguageInstalled(preferred))
		{
			throw new OcrLanguageUnavailableException(preferred);
		}
		BitmapSource bitmapSource = Prepare(image);
		List<BitmapSource> list = new List<BitmapSource> { bitmapSource };
		if (NeedsInvertedCopy(bitmapSource))
		{
			list.Add(Invert(bitmapSource));
		}
		List<(OcrEngine Engine, string Tag)> engines = CreateEngines(preferred);
		OcrOutput best = null;
		double bestScore = double.NegativeInfinity;
		foreach (BitmapSource item in list)
		{
			using SoftwareBitmap softwareBitmap = await DecodeSoftwareBitmapAsync(item);
			foreach (var item2 in engines)
			{
				var (ocrEngine, tag) = item2;
				ct.ThrowIfCancellationRequested();
				OcrResult ocrResult;
				try
				{
					ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
				}
				catch (Exception)
				{
					continue;
				}
				string code = CodeOf(tag);
				List<string> lines = (from l in ocrResult.Lines
					select CleanLine(l.Text.Trim(), code) into l
					where l.Length > 0
					select l).ToList();
				List<string> list2 = BuildParagraphs(ocrResult.Lines, code);
				string text = string.Join("\n\n", list2).Trim();
				if (text.Length != 0)
				{
					double num = Score(text, tag);
					OcrOutput ocrOutput = new OcrOutput(text, tag, lines, list2, ConfidenceFromScore(num));
					if (num > bestScore)
					{
						bestScore = num;
						best = ocrOutput;
					}
				}
			}
		}
		return best ?? new OcrOutput(string.Empty, preferred ?? string.Empty, Array.Empty<string>());
	}

	private static async Task<SoftwareBitmap> DecodeSoftwareBitmapAsync(BitmapSource image)
	{
		byte[] value = EncodePng(image);
		using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(stream))
		{
			writer.WriteBytes(value);
			await writer.StoreAsync();
			await writer.FlushAsync();
			writer.DetachStream();
		}
		stream.Seek(0uL);
		return await (await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream)).GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
	}

	private static bool NeedsInvertedCopy(BitmapSource image)
	{
		try
		{
			FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0.0);
			int num = formatConvertedBitmap.PixelWidth * 4;
			byte[] array = new byte[num * formatConvertedBitmap.PixelHeight];
			formatConvertedBitmap.CopyPixels(array, num, 0);
			long num2 = 0L;
			long num3 = 0L;
			for (int i = 0; i + 3 < array.Length; i += 16)
			{
				byte b = array[i];
				byte b2 = array[i + 1];
				int num4 = (array[i + 2] * 299 + b2 * 587 + b * 114) / 1000;
				num3++;
				if (num4 < 110)
				{
					num2++;
				}
			}
			return num3 > 0 && num2 * 100 / num3 > 32;
		}
		catch
		{
			return false;
		}
	}

	private static BitmapSource Invert(BitmapSource image)
	{
		FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0.0);
		int num = formatConvertedBitmap.PixelWidth * 4;
		byte[] array = new byte[num * formatConvertedBitmap.PixelHeight];
		formatConvertedBitmap.CopyPixels(array, num, 0);
		for (int i = 0; i + 3 < array.Length; i += 4)
		{
			array[i] = (byte)(255 - array[i]);
			array[i + 1] = (byte)(255 - array[i + 1]);
			array[i + 2] = (byte)(255 - array[i + 2]);
		}
		BitmapSource bitmapSource = BitmapSource.Create(formatConvertedBitmap.PixelWidth, formatConvertedBitmap.PixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null, array, num);
		bitmapSource.Freeze();
		return bitmapSource;
	}

	private static List<(OcrEngine Engine, string Tag)> CreateEngines(string? preferredLanguageCode)
	{
		List<(OcrEngine, string)> list = new List<(OcrEngine, string)>();
		IReadOnlyList<Language> availableRecognizerLanguages = OcrEngine.AvailableRecognizerLanguages;
		if (!string.IsNullOrEmpty(preferredLanguageCode))
		{
			Language language = availableRecognizerLanguages.FirstOrDefault((Language l) => IsPreferred(l.LanguageTag, preferredLanguageCode));
			if ((object)language != null)
			{
				OcrEngine ocrEngine = OcrEngine.TryCreateFromLanguage(language);
				if ((object)ocrEngine != null)
				{
					list.Add((ocrEngine, language.LanguageTag));
				}
			}
			return list;
		}
		OcrEngine ocrEngine2 = OcrEngine.TryCreateFromUserProfileLanguages();
		if ((object)ocrEngine2 != null)
		{
			string item = ocrEngine2.RecognizerLanguage?.LanguageTag ?? string.Empty;
			list.Add((ocrEngine2, item));
		}
		foreach (Language language2 in availableRecognizerLanguages)
		{
			if (!list.Any(((OcrEngine, string) e) => TagMatches(e.Item2, language2.LanguageTag)))
			{
				OcrEngine ocrEngine3 = OcrEngine.TryCreateFromLanguage(language2);
				if ((object)ocrEngine3 != null)
				{
					list.Add((ocrEngine3, language2.LanguageTag));
				}
			}
		}
		return list;
	}

	private static List<string> BuildParagraphs(IReadOnlyList<OcrLine> lines, string code)
	{
		List<(string, double, double)> list = new List<(string, double, double)>();
		foreach (OcrLine line in lines)
		{
			string text = CleanLine(line.Text.Trim(), code);
			if (text.Length == 0)
			{
				continue;
			}
			double num = double.MaxValue;
			double num2 = double.MinValue;
			foreach (OcrWord word in line.Words)
			{
				Rect boundingRect = word.BoundingRect;
				if (!(boundingRect.Height <= 0.0))
				{
					num = Math.Min(num, boundingRect.Top);
					num2 = Math.Max(num2, boundingRect.Bottom);
				}
			}
			if (num == double.MaxValue)
			{
				num = (double)list.Count * 20.0;
				num2 = num + 14.0;
			}
			list.Add((text, num, num2));
		}
		if (list.Count == 0)
		{
			return new List<string>();
		}
		if (list.Count == 1)
		{
			return new List<string> { list[0].Item1 };
		}
		list.Sort(((string Text, double Top, double Bottom) a, (string Text, double Top, double Bottom) b) => a.Top.CompareTo(b.Top));
		List<double> list2 = (from h in list.Where(((string Text, double Top, double Bottom) e) => e.Bottom > e.Top).Select(((string Text, double Top, double Bottom) e) => e.Bottom - e.Top)
			orderby h
			select h).ToList();
		double num3 = ((list2.Count > 0) ? list2[list2.Count / 2] : 16.0);
		double num4 = Math.Max(5.0, num3 * 0.55);
		List<string> list3 = new List<string>();
		List<string> list4 = new List<string>();
		double num5 = list[0].Item2;
		foreach (var item in list)
		{
			if (list4.Count > 0 && item.Item2 - num5 > num4)
			{
				list3.Add(JoinLines(list4));
				list4.Clear();
			}
			list4.Add(item.Item1);
			num5 = Math.Max(num5, item.Item3);
		}
		if (list4.Count > 0)
		{
			list3.Add(JoinLines(list4));
		}
		return list3.Where((string p) => p.Length > 0).ToList();
	}

	private static string CleanLine(string text, string code)
	{
		if (!(code == "ja") && !(code == "zh"))
		{
			return text;
		}
		return RemoveCjkSpaces(text);
	}

	private static string RemoveCjkSpaces(string text)
	{
		if (text.IndexOf(' ') < 0)
		{
			return text;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == ' ' && i > 0 && i + 1 < text.Length)
			{
				char c = text[i - 1];
				char c2 = text[i + 1];
				if (IsCjk(c) && IsCjk(c2))
				{
					continue;
				}
			}
			stringBuilder.Append(text[i]);
		}
		return stringBuilder.ToString();
	}

	private static bool IsCjk(char c)
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

	private static string JoinLines(IReadOnlyList<string> lines)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string line in lines)
		{
			if (stringBuilder.Length > 0 && NeedsSpace(stringBuilder[stringBuilder.Length - 1], line[0]))
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(line);
		}
		return stringBuilder.ToString();
	}

	private static bool NeedsSpace(char left, char right)
	{
		if (!IsAsciiWordChar(left))
		{
			return IsAsciiWordChar(right);
		}
		return true;
	}

	private static bool IsAsciiWordChar(char c)
	{
		switch (c)
		{
		case '0':
		case '1':
		case '2':
		case '3':
		case '4':
		case '5':
		case '6':
		case '7':
		case '8':
		case '9':
		case 'A':
		case 'B':
		case 'C':
		case 'D':
		case 'E':
		case 'F':
		case 'G':
		case 'H':
		case 'I':
		case 'J':
		case 'K':
		case 'L':
		case 'M':
		case 'N':
		case 'O':
		case 'P':
		case 'Q':
		case 'R':
		case 'S':
		case 'T':
		case 'U':
		case 'V':
		case 'W':
		case 'X':
		case 'Y':
		case 'Z':
		case 'a':
		case 'b':
		case 'c':
		case 'd':
		case 'e':
		case 'f':
		case 'g':
		case 'h':
		case 'i':
		case 'j':
		case 'k':
		case 'l':
		case 'm':
		case 'n':
		case 'o':
		case 'p':
		case 'q':
		case 'r':
		case 's':
		case 't':
		case 'u':
		case 'v':
		case 'w':
		case 'x':
		case 'y':
		case 'z':
			return true;
		default:
			return false;
		}
	}

	private static double Score(string text, string availableTag)
	{
		ScriptCounts scriptCounts = ScriptCounts.Analyze(text);
		int num = scriptCounts.Han + scriptCounts.Kana + scriptCounts.Hangul + scriptCounts.Cyrillic + scriptCounts.Latin;
		if (num == 0)
		{
			return -1.0;
		}
		string text2 = CodeOf(availableTag);
		double num2 = text2 switch
		{
			"zh" => scriptCounts.Han, 
			"ja" => (double)scriptCounts.Kana + (double)scriptCounts.Han * 0.3, 
			"ko" => scriptCounts.Hangul, 
			"ru" => scriptCounts.Cyrillic, 
			_ => scriptCounts.Latin, 
		} / (double)num;
		double num3 = (double)scriptCounts.Other / ((double)(num + scriptCounts.Other) + 1.0);
		double num4 = Math.Min(1.0, (double)num / 80.0) * 0.35;
		double num5 = ((num < 12) ? 0.35 : 0.0);
		DetectionResult detectionResult = LanguageDetector.Detect(text);
		double num6;
		if (detectionResult.IsConfident)
		{
			num6 = (string.Equals(detectionResult.Language.Code, text2, StringComparison.OrdinalIgnoreCase) ? 0.35 : (-0.3));
		}
		else
		{
			num6 = 0.0;
		}
		double num7 = ((!string.Equals(text2, "en", StringComparison.OrdinalIgnoreCase) && num2 >= 0.55) ? 0.3 : 0.0);
		bool flag = scriptCounts.Kana >= 1 && scriptCounts.Latin <= 20 && scriptCounts.Kana + scriptCounts.Han >= 4 && text.Length <= 1200;
		if (string.Equals(text2, "ja", StringComparison.OrdinalIgnoreCase) & flag)
		{
			num7 += 0.9;
		}
		else if (string.Equals(text2, "zh", StringComparison.OrdinalIgnoreCase) && scriptCounts.Kana >= 1 && text.Length <= 1200)
		{
			num7 -= 0.6;
		}
		if (string.Equals(text2, "ko", StringComparison.OrdinalIgnoreCase) && scriptCounts.Hangul >= 5 && scriptCounts.Latin <= 40 && text.Length <= 1200)
		{
			num7 += 0.8;
		}
		if (string.Equals(text2, "ru", StringComparison.OrdinalIgnoreCase) && scriptCounts.Cyrillic >= 2)
		{
			num7 += 0.5;
		}
		return num2 + num4 + num6 + num7 - num3 - num5;
	}

	private static double ConfidenceFromScore(double score)
	{
		return Math.Round(Math.Clamp((score + 0.25) / 2.35, 0.35, 0.99), 2);
	}

	private static string CodeOf(string tag)
	{
		if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
		{
			return "zh";
		}
		if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
		{
			return "ja";
		}
		if (tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
		{
			return "ko";
		}
		if (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
		{
			return "ru";
		}
		return "en";
	}

	private static BitmapSource Prepare(BitmapSource image)
	{
		uint maxImageDimension = OcrEngine.MaxImageDimension;
		double num = 1.0;
		if (image.PixelWidth < 350 && image.PixelHeight < 350)
		{
			num = 2.0;
		}
		else if (image.PixelWidth < 600)
		{
			num = 1.25;
		}
		if ((double)image.PixelWidth * num > (double)maxImageDimension || (double)image.PixelHeight * num > (double)maxImageDimension)
		{
			num = Math.Min((double)maxImageDimension / (double)image.PixelWidth, (double)maxImageDimension / (double)image.PixelHeight);
		}
		if (Math.Abs(num - 1.0) < 0.01)
		{
			return image;
		}
		TransformedBitmap transformedBitmap = new TransformedBitmap(image, new ScaleTransform(num, num));
		transformedBitmap.Freeze();
		return transformedBitmap;
	}

	private static byte[] EncodePng(BitmapSource image)
	{
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
		using MemoryStream memoryStream = new MemoryStream();
		pngBitmapEncoder.Save(memoryStream);
		return memoryStream.ToArray();
	}

	private static string NormalizeTag(string code)
	{
		return code switch
		{
			"zh" => "zh-Hans", 
			"en" => "en", 
			"ja" => "ja", 
			"ko" => "ko", 
			"ru" => "ru", 
			_ => code, 
		};
	}

	private static bool IsPreferred(string availableTag, string requested)
	{
		return TagMatches(availableTag, NormalizeTag(requested));
	}

	private static bool TagMatches(string a, string b)
	{
		if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b))
		{
			if (!a.Equals(b, StringComparison.OrdinalIgnoreCase) && !a.StartsWith(b + "-", StringComparison.OrdinalIgnoreCase))
			{
				return b.StartsWith(a + "-", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}
}
