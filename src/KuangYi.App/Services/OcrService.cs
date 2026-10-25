using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.Core.Services;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using SoftwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap;

namespace KuangYi.App.Services;

public sealed record OcrOutput(
    string Text,
    string LanguageTag,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string>? Paragraphs = null);

public sealed class OcrLanguageUnavailableException : Exception
{
    public OcrLanguageUnavailableException(string languageCode)
        : base($"未安装 {languageCode} 识别语言包")
    {
        LanguageCode = languageCode;
    }

    public string LanguageCode { get; }
}

public sealed class OcrService
{
    public IReadOnlyList<string> AvailableLanguageTags =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public bool HasAnyRecognizer => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public bool IsLanguageInstalled(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        var tag = NormalizeTag(code);
        return OcrEngine.AvailableRecognizerLanguages.Any(l => TagMatches(l.LanguageTag, tag));
    }

    public async Task<OcrOutput> RecognizeAsync(BitmapSource image, string? preferredLanguageCode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var preferred = string.IsNullOrWhiteSpace(preferredLanguageCode) ? null : preferredLanguageCode;
        if (preferred is not null && !IsLanguageInstalled(preferred))
        {
            throw new OcrLanguageUnavailableException(preferred);
        }

        var normalized = Prepare(image);
        var variants = new List<BitmapSource> { normalized };
        if (NeedsInvertedCopy(normalized))
        {
            variants.Add(Invert(normalized));
        }

        var engines = CreateEngines(preferred);
        OcrOutput? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var variant in variants)
        {
            using var softwareBitmap = await DecodeSoftwareBitmapAsync(variant);

            foreach (var (engine, tag) in engines)
            {
                ct.ThrowIfCancellationRequested();

                OcrResult result;
                try
                {
                    result = await engine.RecognizeAsync(softwareBitmap);
                }
                catch (Exception)
                {
                    continue;
                }

                var code = CodeOf(tag);
                var lines = result.Lines
                    .Select(l => CleanLine(l.Text.Trim(), code))
                    .Where(l => l.Length > 0)
                    .ToList();

                var paragraphs = BuildParagraphs(result.Lines, code);
                var text = string.Join("\n\n", paragraphs).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var output = new OcrOutput(text, tag, lines, paragraphs);
                var score = Score(text, tag);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = output;
                }
            }
        }

        return best ?? new OcrOutput(string.Empty, preferred ?? string.Empty, Array.Empty<string>());
    }

    private static async Task<SoftwareBitmap> DecodeSoftwareBitmapAsync(BitmapSource image)
    {
        var png = EncodePng(image);

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(
            global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
    }

    private static bool NeedsInvertedCopy(BitmapSource image)
    {
        try
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            long dark = 0;
            long sampled = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 16)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                var luma = (r * 299 + g * 587 + b * 114) / 1000;
                sampled++;
                if (luma < 110)
                {
                    dark++;
                }
            }

            return sampled > 0 && dark * 100 / sampled > 32;
        }
        catch
        {
            return false;
        }
    }

    private static BitmapSource Invert(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(255 - pixels[i]);
            pixels[i + 1] = (byte)(255 - pixels[i + 1]);
            pixels[i + 2] = (byte)(255 - pixels[i + 2]);
        }

        var inverted = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        inverted.Freeze();
        return inverted;
    }

    private static List<(OcrEngine Engine, string Tag)> CreateEngines(string? preferredLanguageCode)
    {
        var engines = new List<(OcrEngine, string)>();
        var available = OcrEngine.AvailableRecognizerLanguages;

        if (!string.IsNullOrEmpty(preferredLanguageCode))
        {
            var preferred = available.FirstOrDefault(l => IsPreferred(l.LanguageTag, preferredLanguageCode));
            if (preferred is not null && OcrEngine.TryCreateFromLanguage(preferred) is { } engine)
            {
                engines.Add((engine, preferred.LanguageTag));
            }

            return engines;
        }

        if (OcrEngine.TryCreateFromUserProfileLanguages() is { } profile)
        {
            var tag = profile.RecognizerLanguage?.LanguageTag ?? string.Empty;
            engines.Add((profile, tag));
        }

        // Auto mode: run every installed recognizer and score the output by script.
        // Windows OCR has no auto-detect, so this is what keeps mixed-language
        // screenshots from being recognized by the wrong engine.
        foreach (var language in available)
        {
            if (engines.Any(e => TagMatches(e.Item2, language.LanguageTag)))
            {
                continue;
            }

            if (OcrEngine.TryCreateFromLanguage(language) is { } engine)
            {
                engines.Add((engine, language.LanguageTag));
            }
        }

        return engines;
    }

    private static List<string> BuildParagraphs(IReadOnlyList<OcrLine> lines, string code)
    {
        var entries = new List<(string Text, double Top, double Bottom)>();
        foreach (var line in lines)
        {
            var text = CleanLine(line.Text.Trim(), code);
            if (text.Length == 0)
            {
                continue;
            }

            var top = double.MaxValue;
            var bottom = double.MinValue;
            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;
                if (rect.Height <= 0)
                {
                    continue;
                }

                top = Math.Min(top, rect.Top);
                bottom = Math.Max(bottom, rect.Bottom);
            }

            if (top == double.MaxValue)
            {
                top = entries.Count * 20.0;
                bottom = top + 14.0;
            }

            entries.Add((text, top, bottom));
        }

        if (entries.Count == 0)
        {
            return new List<string>();
        }

        if (entries.Count == 1)
        {
            return new List<string> { entries[0].Text };
        }

        entries.Sort((a, b) => a.Top.CompareTo(b.Top));
        var heights = entries
            .Where(e => e.Bottom > e.Top)
            .Select(e => e.Bottom - e.Top)
            .OrderBy(h => h)
            .ToList();
        var median = heights.Count > 0 ? heights[heights.Count / 2] : 16.0;
        var paragraphGap = Math.Max(5.0, median * 0.55);

        var paragraphs = new List<string>();
        var current = new List<string>();
        var previousBottom = entries[0].Top;

        foreach (var entry in entries)
        {
            if (current.Count > 0 && entry.Top - previousBottom > paragraphGap)
            {
                paragraphs.Add(JoinLines(current));
                current.Clear();
            }

            current.Add(entry.Text);
            previousBottom = Math.Max(previousBottom, entry.Bottom);
        }

        if (current.Count > 0)
        {
            paragraphs.Add(JoinLines(current));
        }

        return paragraphs.Where(p => p.Length > 0).ToList();
    }

    private static string CleanLine(string text, string code)
    {
        if (code is not ("ja" or "zh"))
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

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && i > 0 && i + 1 < text.Length)
            {
                var previous = text[i - 1];
                var next = text[i + 1];
                if (IsCjk(previous) && IsCjk(next))
                {
                    continue;
                }
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private static bool IsCjk(char c)
        => (c >= 0x3040 && c <= 0x30FF)
           || (c >= 0x3400 && c <= 0x4DBF)
           || (c >= 0x4E00 && c <= 0x9FFF)
           || (c >= 0x3000 && c <= 0x303F)
           || (c >= 0xFF00 && c <= 0xFFEF);

    private static string JoinLines(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length > 0 && NeedsSpace(builder[^1], line[0]))
            {
                builder.Append(' ');
            }

            builder.Append(line);
        }

        return builder.ToString();
    }

    private static bool NeedsSpace(char left, char right)
        => IsAsciiWordChar(left) || IsAsciiWordChar(right);

    private static bool IsAsciiWordChar(char c)
        => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static double Score(string text, string availableTag)    {
        var counts = ScriptCounts.Analyze(text);
        var meaningful = counts.Han + counts.Kana + counts.Hangul + counts.Cyrillic + counts.Latin;
        if (meaningful == 0)
        {
            return -1;
        }

        var code = CodeOf(availableTag);
        var expected = code switch
        {
            "zh" => counts.Han,
            "ja" => counts.Kana + counts.Han * 0.3,
            "ko" => counts.Hangul,
            "ru" => counts.Cyrillic,
            _ => counts.Latin,
        };

        var scriptRatio = expected / meaningful;
        var otherPenalty = counts.Other / (meaningful + counts.Other + 1.0);
        var lengthBonus = Math.Min(1.0, meaningful / 80.0) * 0.35;
        var shortPenalty = meaningful < 12 ? 0.35 : 0;

        var detected = LanguageDetector.Detect(text);
        var agreement = detected.IsConfident
            ? (string.Equals(detected.Language.Code, code, StringComparison.OrdinalIgnoreCase) ? 0.35 : -0.3)
            : 0;

        // Windows' Latin engine happily returns digit/symbol soup for CJK text and
        // can outscore the correct CJK engine. When a non-Latin engine really sees
        // its own script, prefer it.
        var scriptBonus = !string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)
                          && scriptRatio >= 0.55
            ? 0.3
            : 0;

        var japaneseLike = counts.Kana >= 1
                           && counts.Latin <= 20
                           && counts.Kana + counts.Han >= 4
                           && text.Length <= 1200;

        if (string.Equals(code, "ja", StringComparison.OrdinalIgnoreCase) && japaneseLike)
        {
            scriptBonus += 0.9;
        }
        else if (string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase)
                 && counts.Kana >= 1
                 && text.Length <= 1200)
        {
            scriptBonus -= 0.6;
        }

        if (string.Equals(code, "ko", StringComparison.OrdinalIgnoreCase)
            && counts.Hangul >= 5
            && counts.Latin <= 40
            && text.Length <= 1200)
        {
            scriptBonus += 0.8;
        }

        if (string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase) && counts.Cyrillic >= 2)
        {
            scriptBonus += 0.5;
        }

        return scriptRatio + lengthBonus + agreement + scriptBonus - otherPenalty - shortPenalty;
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
        var max = OcrEngine.MaxImageDimension;
        var scale = 1.0;

        if (image.PixelWidth < 350 && image.PixelHeight < 350)
        {
            scale = 2.0;
        }
        else if (image.PixelWidth < 600)
        {
            scale = 1.25;
        }

        if (image.PixelWidth * scale > max || image.PixelHeight * scale > max)
        {
            scale = Math.Min(max / (double)image.PixelWidth, max / (double)image.PixelHeight);
        }

        if (Math.Abs(scale - 1.0) < 0.01)
        {
            return image;
        }

        var transformed = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string NormalizeTag(string code)
        => code switch
        {
            "zh" => "zh-Hans",
            "en" => "en",
            "ja" => "ja",
            "ko" => "ko",
            "ru" => "ru",
            _ => code,
        };

    private static bool IsPreferred(string availableTag, string requested) => TagMatches(availableTag, NormalizeTag(requested));

    private static bool TagMatches(string a, string b)
        => !string.IsNullOrEmpty(a)
           && !string.IsNullOrEmpty(b)
           && (a.Equals(b, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b + "-", StringComparison.OrdinalIgnoreCase)
               || b.StartsWith(a + "-", StringComparison.OrdinalIgnoreCase));

    private readonly record struct ScriptCounts(int Han, int Kana, int Hangul, int Cyrillic, int Latin, int Other)
    {
        public static ScriptCounts Analyze(string text)
        {
            var han = 0;
            var kana = 0;
            var hangul = 0;
            var cyrillic = 0;
            var latin = 0;
            var other = 0;

            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsDigit(c))
                {
                    continue;
                }

                if (InRange(c, 0x4E00, 0x9FFF) || InRange(c, 0x3400, 0x4DBF))
                {
                    han++;
                }
                else if (InRange(c, 0x3040, 0x30FF))
                {
                    kana++;
                }
                else if (InRange(c, 0xAC00, 0xD7A3) || InRange(c, 0x1100, 0x11FF))
                {
                    hangul++;
                }
                else if (InRange(c, 0x0400, 0x04FF))
                {
                    cyrillic++;
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || InRange(c, 0x00C0, 0x024F))
                {
                    latin++;
                }
                else
                {
                    other++;
                }
            }

            return new ScriptCounts(han, kana, hangul, cyrillic, latin, other);
        }

        private static bool InRange(char c, int from, int to) => c >= from && c <= to;
    }
}
