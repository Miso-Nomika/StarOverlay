using System.Security.Cryptography;
using System.Text;

namespace StarOverlay.Host;

internal static class Program
{
    /// <summary>
    /// StarOverlay.Host.exe --agent --game &lt;folder&gt;       no windows, serves the in-game agent (the only mode)
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        var agent = args.Contains("--agent", StringComparer.OrdinalIgnoreCase);
        var folder = GameFolder(args);
        if (folder is null || !Directory.Exists(folder)) return;
        if (!agent)
        {
            // the old overlay windows are gone: the display lives inside the game now
            MessageBox.Show("화면 표시는 게임 안에서 나옵니다. StarOverlay.exe로 설치한 뒤 게임을 켜세요.", "StarOverlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MediumLaunch.HandOff(args)) return;

        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var name = @"Local\StarOverlay.Agent." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game.ToLowerInvariant())));
        using var mutex = new Mutex(false, name);
        try
        {
            if (!mutex.WaitOne(8000)) return;
        }
        catch (AbandonedMutexException)
        {
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(true);
        // 플러그인·설정·기록은 게임 폴더가 아니라 데이터 폴더(mods)에 둔다. 이전 위치의 내용은 처음 한 번 복사한다.
        var home = StarOverlay.Core.Mods.ModHome.Prepare(game);
        StarOverlay.Framework.TranslationModel.Root = StarOverlay.Core.AppStorage.Root(); // translation programs and models live in the data folder, never in the project
        Application.Run(new AgentContext(game, home));
    }

    private static string? GameFolder(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--game", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }

        return args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
    }
}
