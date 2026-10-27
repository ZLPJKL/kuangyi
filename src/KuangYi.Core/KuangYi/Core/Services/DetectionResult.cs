using KuangYi.Core.Models;

namespace KuangYi.Core.Services;

public sealed record DetectionResult(LanguageInfo Language, double Confidence, bool IsConfident)
{
	public static DetectionResult Unknown { get; } = new DetectionResult(Languages.AutoInfo, 0.0, IsConfident: false);
}
