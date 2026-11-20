using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StarOverlay.App.Services;
using StarOverlay.App.ViewModels;
using StarOverlay.App.Views;
using StarOverlay.Core;
using StarOverlay.Core.Clients;
using StarOverlay.Core.Setup;

namespace StarOverlay.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? window = null;
            var finder = new GameInstallFinder(new SystemInstallFileSystem(), WindowsGameInstallRoots.Collect());
            var shell = new ShellViewModel(
                finder: finder,
                browser: new AvaloniaFolderBrowser(() => window),
                dllPicker: new AvaloniaDllPicker(() => window),
                rosterPath: ClientRosterStore.DefaultPath());
            window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            shell.RefreshCapture();
            // the install files travel inside the exe: unpack them (first start of a version only) without freezing the window
            shell.Status = "설치 파일을 준비하는 중...";
            _ = Task.Run(() => PayloadStore.Find(typeof(App).Assembly, AppStorage.Root(), AppContext.BaseDirectory, AppStorage.ProjectRoot()))
                .ContinueWith(task => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    shell.UsePayload(task.IsCompletedSuccessfully ? task.Result : null);
                    shell.Status = shell.HasPayload ? "" : "설치 파일을 찾지 못했습니다. 이 프로그램을 다시 내려받아 주세요.";
                }));
            if (shell.Clients.Count == 0)
                _ = shell.DiscoverInstallsAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
