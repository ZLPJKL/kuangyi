using System.Collections.Generic;

namespace KuangYi.App.Services;

public sealed record OcrOutput(string Text, string LanguageTag, IReadOnlyList<string> Lines, IReadOnlyList<string>? Paragraphs = null, double Confidence = 0.0);
