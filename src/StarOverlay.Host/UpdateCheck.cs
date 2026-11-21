using System.Net;
using System.Net.Http;
using System.Reflection;
using StarOverlay.Core;
using StarOverlay.Framework;

namespace StarOverlay.Host;

/// <summary>Asks once, when the game starts, whether a newer release exists (the "latest" page redirects to the newest tag).</summary>
internal static class UpdateCheck
{
    internal const string LatestPage = "https://github.com/Miso-Nomika/StarOverlay/releases/latest";

    public static void Start() => _ = Task.Run(async () =>
    {
        try
        {
            var current = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = LiveUpdate.Parse(current) ?? typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0);
            var file = Path.Combine(AppStorage.Root(), "mods", "bpsr", "state", "update.json");
            await LiveUpdate.CheckAsync(Fetch, file, version, DateTime.UtcNow);
        }
        catch (Exception)
        {
        }
    });

    private static async Task<string?> Fetch()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StarOverlay");
        using var response = await http.GetAsync(LatestPage);
        if ((int)response.StatusCode is < 300 or >= 400) return null;
        var location = response.Headers.Location?.ToString();
        var at = location?.LastIndexOf("/tag/", StringComparison.Ordinal) ?? -1;
        return at < 0 ? null : location![(at + 5)..];
    }
}
