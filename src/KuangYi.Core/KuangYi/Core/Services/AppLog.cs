using System;
using System.IO;

namespace KuangYi.Core.Services;

public static class AppLog
{
	private const long MaxBytes = 2097152L;

	private static readonly object Gate = new object();

	public static void Info(string message)
	{
		Write("INFO", message, null);
	}

	public static void Warn(string message)
	{
		Write("WARN", message, null);
	}

	public static void Error(string message, Exception? ex = null)
	{
		Write("ERROR", message, ex);
	}

	private static void Write(string level, string message, Exception? ex)
	{
		try
		{
			lock (Gate)
			{
				Directory.CreateDirectory(AppPaths.LogDir);
				string logFile = AppPaths.LogFile;
				if (File.Exists(logFile) && new FileInfo(logFile).Length > 2097152)
				{
					string destFileName = logFile + ".1";
					File.Move(logFile, destFileName, overwrite: true);
				}
				string text = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
				if (ex != null)
				{
					text = text + Environment.NewLine + ex;
				}
				File.AppendAllText(logFile, text + Environment.NewLine);
			}
		}
		catch (Exception ex2)
		{
			try
			{
				File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "applog-error.txt"), $"{DateTimeOffset.Now:HH:mm:ss.fff} {ex2.GetType().Name}: {ex2.Message}{Environment.NewLine}file={AppPaths.LogFile}{Environment.NewLine}{ex2}{Environment.NewLine}");
			}
			catch
			{
			}
		}
	}
}
