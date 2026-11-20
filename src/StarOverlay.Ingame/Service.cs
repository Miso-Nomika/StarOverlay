using System;
using System.Diagnostics;
using System.IO;
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
                if (File.Exists(hostExe) && !IsRunning()) Launch(hostExe, gameRoot);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("service start failed: " + e.Message);
            }

            Thread.Sleep(5000);
        }
    }

    static bool IsRunning()
    {
        foreach (var process in Process.GetProcessesByName("StarOverlay.Host"))
        {
            try
            {
                if (!process.HasExited) return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        return false;
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
        Process.Start(start);
        Plugin.L.LogInfo("service started: " + hostExe);
    }
}
