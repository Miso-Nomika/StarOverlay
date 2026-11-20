namespace StarOverlay.Framework;

public static class PluginInspector
{
    public static bool TryRead(string dllPath, out PluginInfo? info, out string error)
    {
        info = null;
        if (!File.Exists(dllPath))
        {
            error = "플러그인 DLL을 찾지 못했습니다.";
            return false;
        }

        var loadContext = new PluginLoadContext(dllPath);
        try
        {
            var assembly = loadContext.LoadFromStream(new MemoryStream(File.ReadAllBytes(dllPath)));
            var type = PluginLoadContext.PluginTypes(assembly).FirstOrDefault();
            if (type is null || Activator.CreateInstance(type) is not IPlugin plugin)
            {
                error = "이 DLL은 플러그인이 아닙니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(plugin.Info.Id) || string.IsNullOrWhiteSpace(plugin.Info.Name))
            {
                error = "플러그인 정보가 비어 있습니다.";
                return false;
            }

            info = plugin.Info;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or InvalidOperationException)
        {
            error = "플러그인 DLL을 읽지 못했습니다.";
            return false;
        }
        finally
        {
            try { loadContext.Unload(); }
            catch { /* collectible unload is best-effort */ }
        }
    }
}
