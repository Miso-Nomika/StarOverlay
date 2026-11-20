using System.Diagnostics;
using System.Security.Principal;

namespace StarOverlay.Core.Setup;

public sealed record ElevatedRunResult(bool Success, string Message);

/// <summary>
/// A game installed under a protected place (Program Files) cannot be written by a normal program. The installer then runs its own
/// command line install again with administrator rights (one confirmation) instead of failing with "access denied".
/// </summary>
public static class GameFolderAccess
{
    const int ErrorCancelled = 1223;

    /// <summary>Whether a file can be created in the folder (and in the loader's own folder when that already exists).</summary>
    public static bool CanWrite(string folder)
    {
        return Probe(folder) && (!Directory.Exists(Path.Combine(folder, "bpsr")) || Probe(Path.Combine(folder, "bpsr")));
    }

    static bool Probe(string folder)
    {
        try
        {
            var path = Path.Combine(folder, ".staroverlay-" + Guid.NewGuid().ToString("N"));
            using (File.Create(path, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return true;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>True when the folder cannot be written now and administrator rights would change that.</summary>
    public static bool NeedsElevation(string folder) =>
        OperatingSystem.IsWindows() && Directory.Exists(folder) && !IsElevated() && !CanWrite(folder);

    /// <summary>Runs this program again with the given arguments and administrator rights (one confirmation) and waits for it.</summary>
    public static ElevatedRunResult RunElevated(string arguments)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return new ElevatedRunResult(false, "프로그램 경로를 알 수 없어 관리자 권한으로 다시 실행하지 못했습니다.");
        try
        {
            var start = new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var process = Process.Start(start);
            if (process is null) return new ElevatedRunResult(false, "관리자 권한 창을 열지 못했습니다.");
            process.WaitForExit();
            return process.ExitCode == 0
                ? new ElevatedRunResult(true, "")
                : new ElevatedRunResult(false, LastLogLine() ?? "관리자 권한으로 실행했지만 마치지 못했습니다(코드 " + process.ExitCode + ").");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ElevatedRunResult(false, "관리자 권한 확인이 취소되었습니다.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ElevatedRunResult(false, "관리자 권한 창을 열지 못했습니다.");
        }
    }

    /// <summary>What the elevated run wrote last into the setup log (without its time stamp), if it can be read.</summary>
    static string? LastLogLine()
    {
        try
        {
            var line = File.ReadLines(Path.Combine(AppStorage.Root(), "setup.log")).LastOrDefault(text => !string.IsNullOrWhiteSpace(text));
            return line is { Length: > 15 } ? line[15..].Trim() : line;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
