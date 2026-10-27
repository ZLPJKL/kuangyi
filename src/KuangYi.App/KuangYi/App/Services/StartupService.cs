using System;
using KuangYi.Core.Services;
using Microsoft.Win32;

namespace KuangYi.App.Services;

public static class StartupService
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ValueName = "KuangYi";

	public static void SetEnabled(bool enabled)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			if (enabled)
			{
				string processPath = Environment.ProcessPath;
				if (!string.IsNullOrEmpty(processPath))
				{
					registryKey.SetValue("KuangYi", "\"" + processPath + "\"");
				}
			}
			else
			{
				registryKey.DeleteValue("KuangYi", throwOnMissingValue: false);
			}
		}
		catch (Exception ex)
		{
			AppLog.Warn("startup registration failed: " + ex.Message);
		}
	}
}
