using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace StarOverlay.Host;

static class MediumLaunch
{
    public static bool HandOff(string[] args)
    {
        if (!IsElevated()) return false;
        return StartMedium(args);
    }

    static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static bool StartMedium(string[] args)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault(process => process.SessionId == session);
        if (explorer is null) return false;
        var process = OpenProcess(0x1000, false, (uint)explorer.Id);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, 0x000A, out var token)) return false;
            try
            {
                if (!DuplicateTokenEx(token, 0x02000000, IntPtr.Zero, 2, 1, out var primary)) return false;
                try
                {
                    var command = new StringBuilder();
                    command.Append('"').Append(Environment.ProcessPath).Append('"');
                    foreach (var arg in args)
                    {
                        command.Append(' ');
                        if (arg.Contains(' ') || arg.Contains('"'))
                            command.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
                        else
                            command.Append(arg);
                    }

                    var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
                    var directory = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
                    if (!CreateProcessWithTokenW(primary, 0, null, command, 0, IntPtr.Zero, directory, ref startup, out var info))
                        return false;
                    CloseHandle(info.Thread);
                    CloseHandle(info.Process);
                    return true;
                }
                finally
                {
                    CloseHandle(primary);
                }
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

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr primary);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessWithTokenW(IntPtr token, uint flags, string? application, StringBuilder command, uint creation, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved3;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
