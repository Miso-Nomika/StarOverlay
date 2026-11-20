using StarOverlay.Core;
using StarOverlay.Core.Capture;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

/// <summary>"수신 설정": the one-time elevated setup of the raw-socket helper (T04).</summary>
public partial class ShellViewModel
{
    [ObservableProperty]
    private string _captureText = "";

    [ObservableProperty]
    private string _captureActionLabel = "수신 설정";

    [ObservableProperty]
    private bool _showCaptureAction;

    [ObservableProperty]
    private bool _captureBusy;

    private string? CapturePackage() =>
        !OperatingSystem.IsWindows() ? null
        : _payload is { } payload && File.Exists(payload.CaptureExe) ? payload.CaptureExe
        : CaptureSetup.FindPackageExe(AppContext.BaseDirectory, AppStorage.ProjectRoot());

    public void RefreshCapture()
    {
        if (!OperatingSystem.IsWindows())
        {
            CaptureText = "게임 통신 수신 설정은 Windows에서만 쓸 수 있습니다.";
            ShowCaptureAction = false;
            return;
        }

        var package = CapturePackage();
        switch (CaptureSetup.Check(package))
        {
            case CaptureState.Ready:
                CaptureText = "게임 통신 수신: 설정이 끝났습니다. 게임을 켜면 관리자 확인 없이 자동으로 받습니다.";
                ShowCaptureAction = false;
                break;
            case CaptureState.Outdated:
                CaptureText = "게임 통신 수신: 수신 도우미가 새 버전입니다. 한 번 더 설정해야 합니다(관리자 확인 1회).";
                CaptureActionLabel = "수신 설정 갱신";
                ShowCaptureAction = package is not null;
                break;
            default:
                CaptureText = package is null
                    ? "게임 통신 수신: 수신 도우미 파일을 찾지 못했습니다."
                    : "게임 통신 수신: 처음 한 번 설정이 필요합니다. 관리자 확인이 1회 뜨고, 이후에는 뜨지 않습니다. Npcap은 필요 없습니다.";
                CaptureActionLabel = "수신 설정";
                ShowCaptureAction = package is not null;
                break;
        }
    }

    [RelayCommand]
    private async Task SetupCapture()
    {
        if (!OperatingSystem.IsWindows() || CaptureBusy) return;
        var package = CapturePackage();
        if (package is null)
        {
            Status = "수신 도우미 파일을 찾지 못했습니다.";
            return;
        }

        CaptureBusy = true;
        try
        {
            var result = await Task.Run(() => CaptureSetup.Install(package));
            Status = result.Message;
        }
        finally
        {
            CaptureBusy = false;
            RefreshCapture();
        }
    }
}
