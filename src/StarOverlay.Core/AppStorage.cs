namespace StarOverlay.Core;

public static class AppStorage
{
    public const string FolderName = "local";
    public const string MarkerFile = "data.root";

    /// <summary>Environment variable that overrides the data folder (tests use it to stay out of real data).</summary>
    public const string OverrideVariable = "STAROVERLAY_DATA";

    public static string Root()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden;
        var project = ProjectRoot();
        if (project is not null)
            return Path.Combine(project, FolderName);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarOverlay");
    }

    public static string? ProjectRoot()
    {
        var marked = ReadMarker(AppContext.BaseDirectory);
        if (marked is not null) return marked;
        return FindUp(AppContext.BaseDirectory) ?? FindUp(Environment.CurrentDirectory);
    }

    public static void WriteMarker(string destinationDirectory, string sourceDirectory)
    {
        var project = FindUp(sourceDirectory);
        if (project is null) return;
        Directory.CreateDirectory(destinationDirectory);
        File.WriteAllText(Path.Combine(destinationDirectory, MarkerFile), project);
    }

    public static string? FindUp(string start)
    {
        if (string.IsNullOrWhiteSpace(start)) return null;
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(start);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return null;
        }

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StarOverlay.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? ReadMarker(string directory)
    {
        var path = Path.Combine(directory, MarkerFile);
        if (!File.Exists(path)) return null;
        string text;
        try
        {
            text = File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (text.Length == 0 || !File.Exists(Path.Combine(text, "StarOverlay.slnx"))) return null;
        return text;
    }
}
