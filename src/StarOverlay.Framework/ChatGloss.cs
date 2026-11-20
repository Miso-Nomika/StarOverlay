namespace StarOverlay.Framework;

public static class ChatGloss
{
    private static int _ime = 1;
    private static int _translation = 1;
    private static int _instances; // chat plugins running now (the service loads the new plugin set before it unloads the old one, so two can overlap)
    private static int _attachedOnce;

    public static bool ShowIme => Volatile.Read(ref _ime) != 0;

    public static bool ShowTranslation => Volatile.Read(ref _translation) != 0;

    /// <summary>The chat plugin is running (loaded and switched on in the system window). Before any plugin was loaded it counts as running.</summary>
    public static bool Active => Volatile.Read(ref _attachedOnce) == 0 || Volatile.Read(ref _instances) > 0;

    /// <summary>
    /// The translator is needed: the chat plugin runs and shows translations. When this turns false the model (and the graphics memory it holds)
    /// is released; the translator starts again on the first line that needs it.
    /// </summary>
    public static bool TranslationWanted => Active && ShowTranslation;

    public static event Action? Changed;

    /// <summary>A chat plugin was loaded.</summary>
    public static void Attach()
    {
        var before = Active;
        Volatile.Write(ref _attachedOnce, 1);
        Interlocked.Increment(ref _instances);
        if (!before) Changed?.Invoke();
    }

    /// <summary>A chat plugin was unloaded. When it was the last one, the listeners are told (the translator is released).</summary>
    public static void Detach()
    {
        var left = Interlocked.Decrement(ref _instances);
        if (left <= 0)
        {
            Interlocked.Exchange(ref _instances, 0);
            Changed?.Invoke();
        }
    }

    public static void SetIme(bool show) => Set(ref _ime, show);

    public static void SetTranslation(bool show) => Set(ref _translation, show);

    private static void Set(ref int slot, bool show)
    {
        var next = show ? 1 : 0;
        if (Interlocked.Exchange(ref slot, next) == next) return;
        Changed?.Invoke();
    }
}
