using System.Runtime.InteropServices;
using StarOverlay.Core;
using StarOverlay.Core.Capture;
using StarOverlay.Core.Clients;
using StarOverlay.Core.Mods;
using StarOverlay.Core.Setup;

namespace StarOverlay.App;

/// <summary>
/// Command line use of the installer (no window): <c>--install</c>, <c>--uninstall</c>, <c>--status</c>, with <c>--game "path"</c> for
/// a game folder (otherwise the first one found). <c>--no-capture</c> skips the administrator step of the receiving helper.
/// The result is printed and also written to <c>setup.log</c> in the data folder; the exit code is 0 on success.
/// </summary>
static class Cli
{
    static readonly string[] Commands = ["--install", "--uninstall", "--status"];

    public static bool Handles(string[] args) => args.Any(arg => Commands.Contains(arg, StringComparer.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        AttachConsole(-1); // print into the console the exe was started from
        var lines = new List<string>();
        void Say(string text)
        {
            lines.Add(text);
            Console.WriteLine(text);
        }

        var code = Execute(args, Say);
        try
        {
            Directory.CreateDirectory(AppStorage.Root());
            File.AppendAllLines(Path.Combine(AppStorage.Root(), "setup.log"), lines.Select(line => $"{DateTime.Now:MM-dd HH:mm:ss} {line}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return code;
    }

    static int Execute(string[] args, Action<string> say)
    {
        var game = ValueOf(args, "--game") ?? FirstGame();
        if (game is null || !Directory.Exists(game))
        {
            say("게임 폴더를 찾지 못했습니다. --game \"경로\"로 알려 주세요.");
            return 2;
        }

        var payload = PayloadStore.Find(typeof(Cli).Assembly, AppStorage.Root(), AppContext.BaseDirectory, AppStorage.ProjectRoot());
        say($"게임 폴더: {game}");
        if (args.Contains("--status", StringComparer.OrdinalIgnoreCase))
        {
            say("인게임 표시: " + IngameDeployer.State(game, payload?.IngamePackage));
            say("수신 도우미: " + (OperatingSystem.IsWindows() ? CaptureSetup.Check(payload?.CaptureExe).ToString() : "-"));
            say("옛 설치 흔적: " + (LegacyInstall.Find(game).Count == 0 ? "없음" : string.Join(", ", LegacyInstall.Find(game))));
            return 0;
        }

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            var removed = ModInstaller.Uninstall(game);
            say(removed.Message);
            return removed.Success ? 0 : 1;
        }

        if (payload is null)
        {
            say("설치 파일을 찾지 못했습니다.");
            return 3;
        }

        var report = ModInstaller.Install(game, payload);
        foreach (var step in report.Done) say("- " + step);
        say(report.Message);
        if (!report.Success) return 1;
        if (OperatingSystem.IsWindows() && !args.Contains("--no-capture", StringComparer.OrdinalIgnoreCase) && CaptureSetup.Check(payload.CaptureExe) != CaptureState.Ready)
        {
            say("게임 통신 수신을 설정합니다(관리자 확인 1회)...");
            var capture = CaptureSetup.Install(payload.CaptureExe);
            say(capture.Message);
            if (!capture.Success) return 1;
        }

        return 0;
    }

    static string? ValueOf(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    static string? FirstGame()
    {
        var roster = ClientRosterStore.Load(ClientRosterStore.DefaultPath());
        var saved = roster.Clients.FirstOrDefault(client => client.Id == roster.SelectedId) ?? roster.Clients.FirstOrDefault();
        if (saved is not null && Directory.Exists(saved.InstallPath)) return saved.InstallPath;
        var finder = new GameInstallFinder(new SystemInstallFileSystem(), WindowsGameInstallRoots.Collect());
        return finder.Find().FirstOrDefault()?.Path;
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
