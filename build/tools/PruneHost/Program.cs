using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Deletes the .NET framework assemblies that nothing in the service refers to, from the service folder of the payload (build\make-payload.ps1 runs it).
//   PruneHost <service folder> <root> [<root> ...]
// A root is the name of an assembly in the folder (StarOverlay.Host) or the path of a dll outside it (a plugin): everything they refer to, directly or through
// other assemblies, stays. What is left is only deleted when it is a part of .NET itself (signed by Microsoft's framework keys): third-party assemblies, that
// are sometimes loaded by name (the SQLite provider), are never touched. Computed again at every build, so a feature that starts to use a framework
// assembly keeps it without anybody editing a list. An assembly loaded by a name given as text (reflection) cannot be seen: the build tests the result.
var folder = Path.GetFullPath(args[0]);
var files = Directory.GetFiles(folder, "*.dll").ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f, StringComparer.OrdinalIgnoreCase);

IEnumerable<string> References(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata) yield break;
    var md = pe.GetMetadataReader();
    foreach (var handle in md.AssemblyReferences) yield return md.GetString(md.GetAssemblyReference(handle).Name);
}

var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var queue = new Queue<string>();
foreach (var root in args.Skip(1))
{
    if (File.Exists(root))
    {
        foreach (var name in References(root)) queue.Enqueue(name); // a plugin outside the folder: what it refers to counts
        queue.Enqueue(Path.GetFileNameWithoutExtension(root));
    }
    else queue.Enqueue(root);
}

while (queue.Count > 0)
{
    var name = queue.Dequeue();
    if (!seen.Add(name) || !files.TryGetValue(name, out var path)) continue;
    try
    {
        foreach (var reference in References(path)) queue.Enqueue(reference);
    }
    catch (BadImageFormatException)
    {
    }
}

// the keys that sign .NET itself (System.*, Microsoft.* of the framework, netstandard, mscorlib, System.Private.CoreLib)
string[] frameworkTokens = ["b03f5f7f11d50a3a", "31bf3856ad364e35", "cc7b13ffcd2ddd51", "7cec85d7bea7798e", "b77a5c561934e089"];
bool IsFramework(string path)
{
    try
    {
        var token = AssemblyName.GetAssemblyName(path).GetPublicKeyToken();
        return token is { Length: > 0 } && frameworkTokens.Contains(Convert.ToHexString(token).ToLowerInvariant());
    }
    catch (BadImageFormatException)
    {
        return false; // a native dll
    }
}

long freed = 0;
var removed = new List<string>();
foreach (var (name, path) in files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
{
    if (seen.Contains(name) || !IsFramework(path)) continue;
    freed += new FileInfo(path).Length;
    File.Delete(path);
    removed.Add(name);
}

Console.WriteLine($"pruned {removed.Count} framework assemblies nothing refers to ({freed / 1048576.0:N1} MB): {string.Join(", ", removed.Take(12))}{(removed.Count > 12 ? ", ..." : "")}");
