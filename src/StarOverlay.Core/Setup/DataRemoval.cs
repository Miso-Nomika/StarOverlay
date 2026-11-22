using System.Diagnostics;

namespace StarOverlay.Core.Setup;

/// <summary>"전부 삭제": the part that clears the data folder (settings, records, translation model, unpacked install files).</summary>
public static class DataRemoval
{
    /// <summary>Stops the background service (it runs from the data folder) so its files can go.</summary>
    public static void StopService()
    {
        foreach (var process in Process.GetProcessesByName("StarOverlay.Host"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Deletes the data folder with everything in it. A file that is in use is skipped and the rest still goes; the number of
    /// entries left behind is returned (0 = the folder is gone). A folder that is empty of meaning (a drive root, a project checkout) is refused.
    /// </summary>
    public static int DeleteDataFolder(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;
        var full = Path.GetFullPath(root);
        if (Path.GetPathRoot(full) == full || File.Exists(Path.Combine(full, "StarOverlay.slnx"))) return 0;
        var left = 0;
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left++;
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            try
            {
                Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            Directory.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return left;
    }
}
