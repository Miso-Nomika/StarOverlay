namespace StarOverlay.Framework;

public static class TranslationDevice
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";

    public static bool UseGpu { get; private set; }

    public static event Action? Changed;

    public static void Apply(string? value)
    {
        var gpu = string.Equals(value?.Trim(), Gpu, StringComparison.OrdinalIgnoreCase);
        if (gpu == UseGpu) return;
        UseGpu = gpu;
        Changed?.Invoke();
    }
}
