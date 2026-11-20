using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StarOverlay.Core.Clients;

public static class WindowsGameInstallRoots
{
    public static string PerUserLauncherDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "StarLauncher");

    public static IReadOnlyList<string> Collect()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return CollectWindows();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> CollectWindows()
    {
        var roots = new List<string> { PerUserLauncherDirectory() };
        AddDrives(roots);
        AddSteamLibraries(roots);
        AddUninstallLocations(roots);
        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static void AddDrives(List<string> roots)
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (IOException) { return; }

        foreach (var drive in drives)
        {
            try
            {
                if (drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
                    roots.Add(drive.RootDirectory.FullName);
            }
            catch (IOException) { /* skip a drive that disappears mid-scan */ }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddSteamLibraries(List<string> roots)
    {
        using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steam = steamKey?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steam))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            steam = Path.Combine(programFiles, "Steam");
        }

        var manifest = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(manifest))
        {
            try
            {
                foreach (var library in SteamLibraryList.ReadLibraryPaths(File.ReadAllText(manifest)))
                    roots.Add(Path.Combine(library, "steamapps", "common"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        roots.Add(Path.Combine(steam, "steamapps", "common"));
    }

    [SupportedOSPlatform("windows")]
    private static void AddUninstallLocations(List<string> roots)
    {
        var keys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var sub in keys)
                AddInstallLocations(roots, hive, sub);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddInstallLocations(List<string> roots, RegistryKey hive, string subKey)
    {
        try
        {
            using var uninstall = hive.OpenSubKey(subKey);
            if (uninstall is null) return;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                var location = (entry?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(location))
                    roots.Add(location);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
