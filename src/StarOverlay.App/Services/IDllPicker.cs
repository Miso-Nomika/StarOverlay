namespace StarOverlay.App.Services;

public interface IDllPicker
{
    Task<string?> PickDllAsync();
}
