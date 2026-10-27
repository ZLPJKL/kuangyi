using System;
using System.Runtime.InteropServices;

namespace KuangYi.App.Services;

public static class IntegrityCheck
{
	private const uint TokenQuery = 8u;

	private const int TokenIntegrityLevel = 25;

	public static bool IsLowIntegrity()
	{
		nint token = IntPtr.Zero;
		try
		{
			if (!OpenProcessToken(GetCurrentProcess(), 8u, out token))
			{
				return false;
			}
			int num = 256;
			nint num2 = Marshal.AllocHGlobal(num);
			try
			{
				if (!GetTokenInformation(token, 25, num2, num, out var _))
				{
					return false;
				}
				nint sid = Marshal.ReadIntPtr(num2);
				byte b = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
				return Marshal.ReadInt32(GetSidSubAuthority(sid, b - 1)) < 8192;
			}
			finally
			{
				Marshal.FreeHGlobal(num2);
			}
		}
		catch
		{
			return false;
		}
		finally
		{
			if (token != IntPtr.Zero)
			{
				CloseHandle(token);
			}
		}
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, uint access, out nint token);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(nint token, int infoClass, nint info, int length, out int returnLength);

	[DllImport("advapi32.dll")]
	private static extern nint GetSidSubAuthorityCount(nint sid);

	[DllImport("advapi32.dll")]
	private static extern nint GetSidSubAuthority(nint sid, int index);

	[DllImport("kernel32.dll")]
	private static extern nint GetCurrentProcess();

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(nint handle);
}
