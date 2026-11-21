namespace StarOverlay.Framework;

public static class TranslationPrompt
{
    /// <summary>There is no built-in prompt: the player writes their own (the README has an example). Empty means the model gets no instructions.</summary>
    public const string Default = "";

    private static int _generation;

    public static string Current { get; private set; } = Default;

    public static int Generation => Volatile.Read(ref _generation);

    public static event Action? Changed;

    public static void Apply(string? value)
    {
        var next = string.IsNullOrWhiteSpace(value) ? Default : value.Trim();
        if (next == Current) return;
        Current = next;
        Interlocked.Increment(ref _generation);
        Changed?.Invoke();
    }
}
