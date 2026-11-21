using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KuangYi.Core.Models;

public static class Languages
{
	public const string Auto = "auto";

	public static readonly LanguageInfo AutoInfo = new LanguageInfo("auto", string.Empty, "自动检测", "Auto", IsAuto: true);

	public static readonly LanguageInfo Chinese = new LanguageInfo("zh", "zh-Hans", "中文", "Chinese");

	public static readonly LanguageInfo English = new LanguageInfo("en", "en-US", "英语", "English");

	public static readonly LanguageInfo Japanese = new LanguageInfo("ja", "ja-JP", "日语", "Japanese");

	public static readonly LanguageInfo Korean = new LanguageInfo("ko", "ko-KR", "韩语", "Korean");

	public static readonly LanguageInfo Russian = new LanguageInfo("ru", "ru-RU", "俄语", "Russian");

	public static readonly IReadOnlyList<LanguageInfo> All = new LanguageInfo[5] { Chinese, English, Japanese, Korean, Russian };

	public static readonly IReadOnlyList<LanguageInfo> SourceChoices = new LanguageInfo[6] { AutoInfo, Chinese, English, Japanese, Korean, Russian };

	public static LanguageInfo? Find(string? code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return null;
		}
		return All.FirstOrDefault((LanguageInfo l) => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
	}

	public static bool IsSupported(string? code)
	{
		return (object)Find(code) != null;
	}

	public static LanguageInfo Resolve(string? code)
	{
		return Find(code) ?? Chinese;
	}

	public static string DefaultTargetForCurrentSystem()
	{
		string twoLetterISOLanguageName = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
		if (!IsSupported(twoLetterISOLanguageName))
		{
			return Chinese.Code;
		}
		return twoLetterISOLanguageName;
	}
}
