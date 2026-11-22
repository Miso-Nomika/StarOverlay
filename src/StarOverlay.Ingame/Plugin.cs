using System;
using System.IO;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using StarOverlay.Ingame.Ui;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace StarOverlay.Ingame;

/// <summary>
/// The in-game agent. It draws what the Host service tells it to and reports what the user pressed.
/// It does nothing when bpsr\ingame.off exists, and a failure in it must never reach the game.
/// </summary>
[BepInPlugin("staroverlay.ingame", "Star Overlay in-game", "0.4.9")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;
    internal static string GameRoot;
    internal static string PluginDirectory;

    public override void Load()
    {
        L = Log;
        try
        {
            GameRoot = Paths.GameRootPath;
            PluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
            if (File.Exists(Path.Combine(GameRoot, "bpsr", "ingame.off")))
            {
                L.LogInfo("bpsr\\ingame.off exists: the agent does nothing.");
                return;
            }

            var host = ReadHostPath();
            L.LogInfo($"Star Overlay agent starts. game={GameRoot} host={host ?? "(none)"}");
            Service.Start(host, GameRoot);
            var link = new PipeLink();
            link.Start();

            ClassInjector.RegisterTypeInIl2Cpp<AgentBehaviour>();
            var go = new GameObject("StarOverlayAgent");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            var behaviour = go.AddComponent<AgentBehaviour>();
            behaviour.Link = link;
            AgentBehaviour.Instance = behaviour;
        }
        catch (Exception e)
        {
            // never throw into the game
            L.LogError("agent failed to start: " + e);
        }
    }

    static string ReadHostPath()
    {
        try
        {
            var path = Path.Combine(GameRoot, "bpsr", "service.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("host", out var host) ? host.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
