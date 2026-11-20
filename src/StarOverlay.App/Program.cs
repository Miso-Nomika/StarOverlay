using Avalonia;
using Avalonia.Media;
using System;

namespace StarOverlay.App;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // "StarOverlay.exe --install" / "--uninstall" / "--status": the same steps as the buttons, without a window
        if (Cli.Handles(args)) return Cli.Run(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://StarOverlay.App/Assets/Fonts#Pretendard",
            })
            .LogToTrace();
}
