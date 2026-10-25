using System.Runtime.InteropServices;

namespace KuangYi.App.Services;

/// <summary>
/// Detects low-integrity launches (sandboxed file card, some launchers). Such a process
/// cannot register a tray icon or write the normal profile; we relaunch through Explorer
/// so the app runs as a normal interactive process.
/// </summary>
public static class IntegrityCheck
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    public static bool IsLowIntegrity()
    {
        var token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return false;
            }

            var size = 256;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
                {
                    return false;
                }

                var sid = Marshal.ReadIntPtr(buffer);
                var countPtr = GetSidSubAuthorityCount(sid);
                var count = Marshal.ReadByte(countPtr);
                var ridPtr = GetSidSubAuthority(sid, count - 1);
                var rid = Marshal.ReadInt32(ridPtr);

                // SECURITY_MANDATORY_LOW_RID = 0x1000, MEDIUM = 0x2000
                return rid < 0x2000;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
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
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, int index);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
