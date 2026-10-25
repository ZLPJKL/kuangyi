using KuangYi.Core.Models;

namespace KuangYi.Core.Services;

public sealed record DetectionResult(LanguageInfo Language, double Confidence, bool IsConfident)
{
    public static DetectionResult Unknown { get; } = new(Languages.AutoInfo, 0, false);
}

public static class LanguageDetector
{
    public static DetectionResult Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DetectionResult.Unknown;
        }

        var han = 0;
        var hiragana = 0;
        var katakana = 0;
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
            else if (InRange(c, 0x3040, 0x309F))
            {
                hiragana++;
            }
            else if (InRange(c, 0x30A0, 0x30FF))
            {
                katakana++;
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

        var kana = hiragana + katakana;
        var meaningful = han + kana + hangul + cyrillic + latin;
        if (meaningful == 0)
        {
            return DetectionResult.Unknown;
        }

        if (kana >= 2 || (kana > 0 && han > 0 && hiragana > 0))
        {
            return new DetectionResult(Languages.Japanese, 0.95, true);
        }

        if (hangul >= 2)
        {
            return new DetectionResult(Languages.Korean, 0.95, true);
        }

        if (han >= 1 && kana == 0 && hangul == 0)
        {
            var confidence = han >= 4 ? 0.85 : 0.6;
            return new DetectionResult(Languages.Chinese, confidence, han >= 4);
        }

        if (cyrillic > 0 && cyrillic >= latin)
        {
            return new DetectionResult(Languages.Russian, 0.9, cyrillic >= 3);
        }

        if (latin > 0 && han == 0 && kana == 0 && hangul == 0 && cyrillic == 0)
        {
            return new DetectionResult(Languages.English, 0.9, latin >= 3);
        }

        // Mixed scripts: choose the dominant one and mark the result as low confidence.
        var dominant = new (LanguageInfo Language, int Count)[]
        {
            (Languages.Chinese, han),
            (Languages.Japanese, kana),
            (Languages.Korean, hangul),
            (Languages.Russian, cyrillic),
            (Languages.English, latin),
        }.OrderByDescending(x => x.Count).First();

        return new DetectionResult(dominant.Language, 0.5, false);
    }

    private static bool InRange(char c, int from, int to) => c >= from && c <= to;
}
