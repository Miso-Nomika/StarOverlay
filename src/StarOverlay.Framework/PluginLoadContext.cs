using System.Reflection;
using System.Runtime.Loader;

namespace StarOverlay.Framework;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true) =>
        _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "StarOverlay.Framework") return null;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    public static List<Type> PluginTypes(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray(); }

        return types.Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(IPlugin).IsAssignableFrom(type)).ToList();
    }
}
