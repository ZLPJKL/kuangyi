using System;
using System.Security.Cryptography;
using System.Text;

namespace KuangYi.Core.Services;

public static class SecretProtector
{
	private const string Prefix = "dpapi:v1:";

	public static string Protect(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return value ?? string.Empty;
		}
		try
		{
			byte[] inArray = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
			return "dpapi:v1:" + Convert.ToBase64String(inArray);
		}
		catch
		{
			return value;
		}
	}

	public static string Unprotect(string? value)
	{
		if (string.IsNullOrEmpty(value) || !value.StartsWith("dpapi:v1:", StringComparison.Ordinal))
		{
			return value ?? string.Empty;
		}
		try
		{
			byte[] encryptedData = Convert.FromBase64String(value.Substring("dpapi:v1:".Length));
			return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser));
		}
		catch
		{
			return string.Empty;
		}
	}
}
