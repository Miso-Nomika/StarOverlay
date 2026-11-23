using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace StarOverlay.Ingame;

/// <summary>Keeps the Host service (--agent mode) running next to the game: starts it, and again after it died.</summary>
internal static class Service
{
    static Thread _watch;

    public static void Start(string hostExe, string gameRoot)
    {
        if (string.IsNullOrEmpty(hostExe)) return;
        _watch = new Thread(() => Watch(hostExe, gameRoot)) { IsBackground = true, Name = "staroverlay-service-watch" };
        _watch.Start();
    }

    static void Watch(string hostExe, string gameRoot)
    {
        while (true)
        {
            try
            {
                if (File.Exists(hostExe) && !IsRunning(gameRoot)) Launch(hostExe, gameRoot);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("service start failed: " + e.Message);
            }

            Thread.Sleep(5000);
        }
    }

    static Process _launched;

    /// <summary>
    /// Whether the service of THIS game is there: the service holds a lock named after its game folder (the same name Host/Program.cs makes), so another game's
    /// service does not count and a second client gets its own. One that was just started counts too, before it has made its lock.
    /// </summary>
    static bool IsRunning(string gameRoot)
    {
        try
        {
            if (_launched != null && !_launched.HasExited) return true;
        }
        catch (Exception)
        {
            return true;
        }

        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        string name;
        using (var sha = SHA256.Create())
            name = @"Local\StarOverlay.Agent." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(game.ToLowerInvariant()))).Replace("-", "");
        if (!Mutex.TryOpenExisting(name, out var held)) return false;
        held.Dispose();
        return true;
    }

    static void Launch(string hostExe, string gameRoot)
    {
        var start = new ProcessStartInfo(hostExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(hostExe) ?? "",
        };
        start.ArgumentList.Add("--agent");
        start.ArgumentList.Add("--game");
        start.ArgumentList.Add(gameRoot);
        _launched = Process.Start(start);
        Plugin.L.LogInfo("service started: " + hostExe);
    }
}
