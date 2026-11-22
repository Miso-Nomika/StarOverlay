using System.Diagnostics;

namespace StarOverlay.Ingame;

/// <summary>
/// Tools for developing the agent inside the running game. They exist only in a development build (build\release.ps1 -Dev, which defines DEV_TOOLS);
/// in a release build the calls below are removed by the compiler and the tools are not in the dll.
/// </summary>
static class DevTools
{
    /// <summary>Every frame while the chat window is shown.</summary>
    [Conditional("DEV_TOOLS")]
    internal static void Tick()
    {
#if DEV_TOOLS
        LuaDev.Tick();
#endif
    }
}
