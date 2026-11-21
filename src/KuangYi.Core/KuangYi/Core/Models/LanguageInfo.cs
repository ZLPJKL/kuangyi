namespace KuangYi.Core.Models;

public sealed record LanguageInfo(string Code, string Tag, string NameZh, string NameEn, bool IsAuto = false)
{
	public override string ToString()
	{
		return NameZh;
	}
}
