using StarOverlay.Core.Platform;

namespace StarOverlay.Core.Launch;

public enum LaunchRequestError
{
    ClientRequired,
    ExecutableNotAbsolute,
    ExecutableInvalid,
    WorkingDirectoryNotAbsolute,
    WorkingDirectoryInvalid,
}

public readonly record struct LaunchRequest
{
    public Guid ClientId { get; }
    public string ExecutablePath { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyList<string> Arguments { get; }

    private LaunchRequest(Guid clientId, string executablePath, string workingDirectory, IReadOnlyList<string> arguments)
    {
        ClientId = clientId;
        ExecutablePath = executablePath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
    }

    public static bool TryCreate(
        Guid clientId,
        string? executablePath,
        string? workingDirectory,
        IReadOnlyList<string>? arguments,
        out LaunchRequest request,
        out LaunchRequestError error)
    {
        request = default;
        if (clientId == Guid.Empty)
        {
            error = LaunchRequestError.ClientRequired;
            return false;
        }

        if (!TryPath(executablePath, LaunchRequestError.ExecutableNotAbsolute, LaunchRequestError.ExecutableInvalid, out var executable, out error))
            return false;
        if (!TryPath(workingDirectory, LaunchRequestError.WorkingDirectoryNotAbsolute, LaunchRequestError.WorkingDirectoryInvalid, out var directory, out error))
            return false;

        request = new LaunchRequest(clientId, executable, directory, arguments?.ToArray() ?? []);
        error = default;
        return true;
    }

    private static bool TryPath(
        string? path,
        LaunchRequestError notAbsolute,
        LaunchRequestError invalid,
        out string normalized,
        out LaunchRequestError error)
    {
        if (AbsolutePath.TryNormalize(path, out normalized, out var pathError))
        {
            error = default;
            return true;
        }

        error = pathError switch
        {
            PathNormalizeError.NotAbsolute => notAbsolute,
            PathNormalizeError.Invalid => invalid,
            _ => notAbsolute,
        };
        normalized = "";
        return false;
    }
}
