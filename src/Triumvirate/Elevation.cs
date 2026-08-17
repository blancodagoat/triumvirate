using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Triumvirate;

/// <summary>
/// Whether a process runs as administrator. The suite has to care because it stops and
/// starts the tools: Windows withholds a non-elevated app's registered hotkeys while an
/// elevated window has focus, so a tool that was running elevated and comes back normal
/// looks perfectly healthy in the UI while its hotkey silently does nothing over every
/// anti-cheat game — the exact failure DejaVu's own Elevation.cs documents.
/// </summary>
internal static class Elevation
{
    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>
    /// Elevation of another process. A refusal is an answer, not an error: only a process
    /// at a higher integrity level than ours denies the query, and from a normal suite
    /// that IS elevated. From an elevated suite nothing is refused, so a failure there
    /// means the process is simply gone.
    /// </summary>
    public static bool IsProcessElevated(int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
        {
            return !IsElevated;
        }

        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token))
            {
                return !IsElevated;
            }

            try
            {
                return GetTokenInformation(token, TokenElevation, out uint elevated, sizeof(uint), out _)
                    && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token, int infoClass, out uint info, uint length, out uint returned);

    /// <summary>Convenience for the one caller that has a <see cref="Process"/> in hand.</summary>
    public static bool IsProcessElevated(Process process) => IsProcessElevated(process.Id);
}
