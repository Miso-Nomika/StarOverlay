using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace StarOverlay.Core.Capture;

public enum CaptureState
{
    /// <summary>The helper exe, the scheduled task or the firewall rule is missing.</summary>
    NotInstalled,

    /// <summary>Installed, but the packaged exe is a different build.</summary>
    Outdated,

    Ready,
}

public sealed record CaptureSetupResult(bool Success, int ExitCode, string Message);

/// <summary>
/// One-time setup for the raw-socket helper: copy it to Program Files, register a manual-start scheduled task that
/// runs it with the highest privileges, and open the inbound firewall rule it needs (without the rule no packets
/// from the game server arrive). Needs one UAC confirmation. Afterwards starting the helper needs no UAC.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class CaptureSetup
{
    public const string TaskName = "StarOverlay.Capture";
    public const string FirewallRuleName = "StarOverlay Capture";
    public const string ExeName = "StarOverlay.Capture.exe";

    // exit codes of the elevated script
    public const int ExitHashMismatch = 10;
    public const int ExitCopyFailed = 11;
    public const int ExitTaskFailed = 12;
    public const int ExitFirewallFailed = 13;

    public static string InstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "StarOverlay", "Capture");

    public static string InstalledExe => Path.Combine(InstallDirectory, ExeName);

    /// <summary>Where the packaged helper is: next to the app (capture\), or the build output of this checkout.</summary>
    public static string? FindPackageExe(string appBase, string? projectRoot)
    {
        var candidates = new List<string> { Path.Combine(appBase, "capture", ExeName) };
        if (projectRoot is not null) candidates.Add(Path.Combine(projectRoot, "artifacts", "capture", ExeName));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string PipeName(string userSid) => "StarOverlay.Capture." + userSid;

    /// <summary>The state of the installed helper compared with the packaged one (the exe is the same build when the hashes match).</summary>
    public static CaptureState Check(string? packageExe)
    {
        if (!File.Exists(InstalledExe) || !TaskExists() || !RuleExists()) return CaptureState.NotInstalled;
        if (packageExe is null || !File.Exists(packageExe)) return CaptureState.Ready;
        return Sha256(InstalledExe).Equals(Sha256(packageExe), StringComparison.OrdinalIgnoreCase)
            ? CaptureState.Ready
            : CaptureState.Outdated;
    }

    public static bool TaskExists() => RunQuiet("schtasks.exe", $"/Query /TN \"{TaskName}\"") == 0;

    public static bool RuleExists() =>
        RunQuiet("netsh.exe", $"advfirewall firewall show rule name=\"{FirewallRuleName}\"") == 0;

    /// <summary>The PowerShell script the elevated process runs. Exposed for tests.</summary>
    public static string BuildSetupScript(string packageExe, string packageSha256, string userSid, string userName)
    {
        var dir = Quote(InstallDirectory);
        var exe = Quote(InstalledExe);
        var arguments = Quote($"--pipe {PipeName(userSid)} --wait 120");
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try {");
        sb.AppendLine($"  New-Item -ItemType Directory -Force -Path {dir} | Out-Null");
        sb.AppendLine($"  Copy-Item -LiteralPath {Quote(packageExe)} -Destination {exe} -Force");
        sb.AppendLine("} catch { exit " + ExitCopyFailed + " }");
        // the hash of the copy must equal the hash the app saw before asking for elevation
        sb.AppendLine($"if ((Get-FileHash -LiteralPath {exe} -Algorithm SHA256).Hash -ne {Quote(packageSha256)}) {{ Remove-Item -LiteralPath {exe} -Force; exit {ExitHashMismatch} }}");
        sb.AppendLine("try {");
        sb.AppendLine($"  $action = New-ScheduledTaskAction -Execute {exe} -Argument {arguments}");
        sb.AppendLine($"  $principal = New-ScheduledTaskPrincipal -UserId {Quote(userName)} -LogonType Interactive -RunLevel Highest");
        sb.AppendLine("  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew");
        sb.AppendLine($"  Register-ScheduledTask -TaskName {Quote(TaskName)} -Action $action -Principal $principal -Settings $settings -Force | Out-Null");
        // the user may start (run) the task without elevation: read + execute for that user only
        sb.AppendLine("  $service = New-Object -ComObject Schedule.Service");
        sb.AppendLine("  $service.Connect()");
        sb.AppendLine($"  $task = $service.GetFolder('\\').GetTask({Quote(TaskName)})");
        sb.AppendLine($"  $task.SetSecurityDescriptor($task.GetSecurityDescriptor(4) + '(A;;GRGX;;;{userSid})', 0)");
        sb.AppendLine("} catch { exit " + ExitTaskFailed + " }");
        sb.AppendLine("try {");
        sb.AppendLine($"  Get-NetFirewallRule -DisplayName {Quote(FirewallRuleName)} -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
        sb.AppendLine($"  New-NetFirewallRule -DisplayName {Quote(FirewallRuleName)} -Direction Inbound -Action Allow -Program {exe} -Protocol TCP -Profile Any | Out-Null");
        sb.AppendLine("} catch { exit " + ExitFirewallFailed + " }");
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    /// <summary>Removes the task, the firewall rule and the installed helper. Also needs elevation.</summary>
    public static string BuildRemoveScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine($"Unregister-ScheduledTask -TaskName {Quote(TaskName)} -Confirm:$false");
        sb.AppendLine($"Get-NetFirewallRule -DisplayName {Quote(FirewallRuleName)} | Remove-NetFirewallRule");
        sb.AppendLine($"Stop-Process -Name 'StarOverlay.Capture' -Force");
        sb.AppendLine($"Remove-Item -LiteralPath {Quote(InstallDirectory)} -Recurse -Force");
        // the "StarOverlay" folder above it goes too when nothing else is in it
        sb.AppendLine($"$parent = {Quote(Path.GetDirectoryName(InstallDirectory)!)}");
        sb.AppendLine("if ((Test-Path -LiteralPath $parent) -and -not (Get-ChildItem -LiteralPath $parent -Force)) { Remove-Item -LiteralPath $parent -Force }");
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    /// <summary>Single-quoted PowerShell string.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>The command line arguments that run a script without a file (so nothing in a user folder is executed elevated).</summary>
    public static string EncodedArguments(string script) =>
        "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    /// <summary>Asks for elevation (UAC), runs the setup script and waits for it.</summary>
    public static CaptureSetupResult Install(string packageExe)
    {
        if (!File.Exists(packageExe)) return new CaptureSetupResult(false, -1, "수신 도우미 파일을 찾지 못했습니다: " + packageExe);
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? "";
        var script = BuildSetupScript(packageExe, Sha256(packageExe), sid, identity.Name);
        return RunElevated(script);
    }

    public static CaptureSetupResult Remove() => RunElevated(BuildRemoveScript());

    static CaptureSetupResult RunElevated(string script)
    {
        try
        {
            var start = new ProcessStartInfo("powershell.exe", EncodedArguments(script))
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(start);
            if (process is null) return new CaptureSetupResult(false, -1, "관리자 권한 창을 열지 못했습니다.");
            process.WaitForExit();
            return new CaptureSetupResult(process.ExitCode == 0, process.ExitCode, Describe(process.ExitCode));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new CaptureSetupResult(false, 1223, "관리자 권한 확인이 취소되었습니다.");
        }
    }

    public static string Describe(int exitCode) => exitCode switch
    {
        0 => "수신 설정을 마쳤습니다.",
        ExitHashMismatch => "복사한 수신 도우미가 원본과 달라 중단했습니다.",
        ExitCopyFailed => "수신 도우미를 Program Files에 복사하지 못했습니다.",
        ExitTaskFailed => "예약 작업을 등록하지 못했습니다.",
        ExitFirewallFailed => "방화벽 규칙을 등록하지 못했습니다.",
        1223 => "관리자 권한 확인이 취소되었습니다.",
        _ => "수신 설정에 실패했습니다(코드 " + exitCode + ").",
    };

    public static bool IsHelperRunning() => Process.GetProcessesByName("StarOverlay.Capture").Length > 0;

    /// <summary>Starts the installed helper through its scheduled task (no UAC). True when it is running afterwards.</summary>
    public static bool StartHelper(int waitMilliseconds = 4000)
    {
        if (IsHelperRunning()) return true;
        if (RunQuiet("schtasks.exe", $"/Run /TN \"{TaskName}\"") != 0) return false;
        var until = DateTime.UtcNow.AddMilliseconds(waitMilliseconds);
        while (DateTime.UtcNow < until)
        {
            if (IsHelperRunning()) return true;
            Thread.Sleep(100);
        }

        return IsHelperRunning();
    }

    static int RunQuiet(string file, string arguments)
    {
        try
        {
            var start = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(start);
            if (process is null) return -1;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
