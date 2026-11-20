using StarOverlay.Core;
using StarOverlay.Core.Capture;
using StarOverlay.Core.Mods;
using StarOverlay.Core.Setup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

/// <summary>"오버레이 설치": puts the in-game loader, the standard plugins and the receiving helper on the PC, and takes them off again.</summary>
public partial class ShellViewModel
{
    private PayloadPaths? _payload;
    private readonly Func<string, bool>? _isGameRunning;
    private readonly Func<string, Task<string>>? _captureInstaller; // tests replace the administrator step

    [ObservableProperty]
    private string _ingameText = "게임을 선택하면 오버레이를 설치할 수 있습니다.";

    [ObservableProperty]
    private string _ingameActionLabel = "설치";

    [ObservableProperty]
    private bool _showIngameAction;

    [ObservableProperty]
    private bool _canUninstall;

    [ObservableProperty]
    private bool _ingameBusy;

    private bool CanInstallIngame() => SelectedClient is not null && !IngameBusy && _payload is not null;

    private bool CanUninstallIngame() => SelectedClient is not null && !IngameBusy && CanUninstall;

    public bool HasPayload => _payload is not null;

    /// <summary>Called when the install files are ready (they are unpacked after the window opened).</summary>
    public void UsePayload(PayloadPaths? payload)
    {
        _payload = payload;
        if (payload is not null) IncludeBundledPlugins(payload.Plugins);
        RefreshClient();
    }

    public void RefreshIngame()
    {
        InstallIngameCommand.NotifyCanExecuteChanged();
        UninstallIngameCommand.NotifyCanExecuteChanged();
        if (SelectedClient is null || !Directory.Exists(SelectedClient.InstallPath))
        {
            IngameText = SelectedClient is null
                ? "게임을 선택하면 오버레이를 설치할 수 있습니다."
                : "게임 폴더를 찾지 못했습니다.";
            ShowIngameAction = false;
            CanUninstall = false;
            RefreshCapture();
            return;
        }

        if (_payload is null)
        {
            IngameText = "설치 파일을 찾지 못했습니다. 이 프로그램을 다시 내려받아 주세요.";
            ShowIngameAction = false;
            CanUninstall = false;
            RefreshCapture();
            return;
        }

        var state = IngameDeployer.State(SelectedClient.InstallPath, _payload.IngamePackage);
        ShowIngameAction = true;
        CanUninstall = state != IngameState.NotInstalled;
        switch (state)
        {
            case IngameState.InstalledOn:
                IngameText = "설치되어 있습니다. 게임을 켜면 자동으로 화면에 나타납니다.";
                IngameActionLabel = "다시 설치";
                break;
            case IngameState.InstalledOff:
                IngameText = "설치되어 있지만 꺼져 있습니다. 다시 설치하면 켜집니다.";
                IngameActionLabel = "다시 설치";
                break;
            case IngameState.Outdated:
                IngameText = "새 버전이 있습니다. 업데이트를 누르면 바뀝니다.";
                IngameActionLabel = "업데이트";
                break;
            default:
                IngameText = "아직 설치되지 않았습니다.";
                IngameActionLabel = "설치";
                break;
        }

        var legacy = LegacyInstall.Find(SelectedClient.InstallPath);
        if (legacy.Count > 0 && state == IngameState.NotInstalled)
            IngameText += " 예전 방식으로 설치한 파일이 있어 설치할 때 함께 정리합니다.";
        InstallIngameCommand.NotifyCanExecuteChanged();
        UninstallIngameCommand.NotifyCanExecuteChanged();
        RefreshCapture();
    }

    [RelayCommand(CanExecute = nameof(CanInstallIngame))]
    private async Task InstallIngameAsync()
    {
        if (SelectedClient is not { } client || _payload is not { } payload) return;
        IngameBusy = true;
        NotifyIngameCommands();
        try
        {
            if (GameFolderAccess.NeedsElevation(client.InstallPath))
            {
                // a protected place (Program Files): the same install runs again with administrator rights, one confirmation for all of it
                Status = "게임 폴더가 보호된 위치에 있어 관리자 확인이 한 번 뜹니다...";
                var elevated = await Task.Run(() => GameFolderAccess.RunElevated($"--install --game \"{client.InstallPath}\""));
                Status = elevated.Success ? "설치를 마쳤습니다. 게임을 켜면 화면에 나타납니다." : elevated.Message;
                return;
            }

            Status = "설치하는 중...";
            var report = await Task.Run(() => ModInstaller.Install(client.InstallPath, payload, _isGameRunning));
            if (!report.Success)
            {
                Status = report.Message;
                return;
            }

            var message = report.Message;
            if (_captureInstaller is not null)
            {
                message += " " + await _captureInstaller(payload.CaptureExe);
            }
            else if (OperatingSystem.IsWindows() && CaptureSetup.Check(payload.CaptureExe) != CaptureState.Ready)
            {
                Status = "전투 정보 수신을 설정합니다. 관리자 확인이 한 번 뜹니다...";
                var capture = await Task.Run(() => CaptureSetup.Install(payload.CaptureExe));
                message += " " + capture.Message;
            }

            Status = message + " 게임을 켜면 화면에 나타납니다.";
        }
        finally
        {
            IngameBusy = false;
            RefreshClient();
            NotifyIngameCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUninstallIngame))]
    private async Task UninstallIngameAsync()
    {
        if (SelectedClient is not { } client) return;
        IngameBusy = true;
        NotifyIngameCommands();
        try
        {
            if (GameFolderAccess.NeedsElevation(client.InstallPath))
            {
                Status = "게임 폴더가 보호된 위치에 있어 관리자 확인이 한 번 뜹니다...";
                var elevated = await Task.Run(() => GameFolderAccess.RunElevated($"--uninstall --game \"{client.InstallPath}\""));
                Status = elevated.Success ? "제거했습니다. 플러그인 설정과 전투 기록은 데이터 폴더에 그대로 있습니다." : elevated.Message;
                return;
            }

            var report = await Task.Run(() => ModInstaller.Uninstall(client.InstallPath, _isGameRunning));
            Status = report.Message;
        }
        finally
        {
            IngameBusy = false;
            RefreshClient();
            NotifyIngameCommands();
        }
    }

    private void NotifyIngameCommands()
    {
        InstallIngameCommand.NotifyCanExecuteChanged();
        UninstallIngameCommand.NotifyCanExecuteChanged();
        SetupCaptureCommand.NotifyCanExecuteChanged();
    }
}
