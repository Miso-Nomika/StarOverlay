namespace StarOverlay.Core.Platform;

public static class HostPlatform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
