using System;

namespace KuangYi.App.Services;

public sealed class OcrLanguageUnavailableException : Exception
{
	public string LanguageCode { get; }

	public OcrLanguageUnavailableException(string languageCode)
		: base("未安装 " + languageCode + " 识别语言包")
	{
		LanguageCode = languageCode;
	}
}
