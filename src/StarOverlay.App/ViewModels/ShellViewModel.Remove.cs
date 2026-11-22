using StarOverlay.Core;
using StarOverlay.Core.Capture;
using StarOverlay.Core.Mods;
using StarOverlay.Core.Setup;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

/// <summary>"전부 삭제": takes the overlay off every listed game, removes the receiving helper and clears the data folder.</summary>
public partial class ShellViewModel
{
    private bool _removedAll;

    [RelayCommand]
    private async Task RemoveAllAsync()
    {
        if (IngameBusy || _removedAll) return;
        IngameBusy = true;
        NotifyIngameCommands();
        try
        {
            var games = Clients.Where(client => Directory.Exists(client.InstallPath)
                && (IngameDeployer.State(client.InstallPath) != IngameState.NotInstalled
                    || Directory.Exists(ModLayout.Root(client.InstallPath)))).ToList(); // also a game whose loader is gone but whose bpsr folder is left
            foreach (var client in games)
            {
                Status = $"{client.DisplayName}에서 오버레이를 제거하는 중...";
                var report = await Task.Run(() => GameFolderAccess.NeedsElevation(client.InstallPath)
                    ? ToReport(GameFolderAccess.RunElevated($"--uninstall --purge --game \"{client.InstallPath}\""))
                    : ModInstaller.Uninstall(client.InstallPath, _isGameRunning, purge: true));
                if (!report.Success)
                {
                    Status = $"{client.DisplayName}: {report.Message} 전부 삭제를 멈췄습니다.";
                    return;
                }
            }

            if (OperatingSystem.IsWindows() && (File.Exists(CaptureSetup.InstalledExe) || CaptureSetup.TaskExists() || CaptureSetup.RuleExists()))
            {
                Status = "전투 정보 수신 도우미를 지우는 중입니다. 관리자 확인이 한 번 뜹니다...";
                var capture = await Task.Run(CaptureSetup.Remove);
                if (!capture.Success)
                {
                    Status = capture.Message + " 전부 삭제를 멈췄습니다.";
                    return;
                }
            }

            Status = "저장 위치를 지우는 중...";
            _removedAll = true; // nothing is written back into the folder from here on
            var left = await Task.Run(() =>
            {
                DataRemoval.StopService();
                return DataRemoval.DeleteDataFolder(AppStorage.Root());
            });
            foreach (var client in Clients.ToList()) _catalog.Remove(client.Id); // the list is empty again, so "게임 찾기" adds the games anew
            Clients.Clear();
            SelectedClient = null;
            Library.Clear();
            OnPropertyChanged(nameof(HasLibrary));
            OnPropertyChanged(nameof(ShowEmptyLibrary));
            RefreshSpecs();
            _removedAll = false;
            RefreshIngame();
            Status = left == 0
                ? "모두 지웠습니다. 이 프로그램 파일(StarOverlay.exe)만 직접 삭제하면 끝납니다."
                : $"거의 다 지웠습니다. 사용 중인 파일 {left}개는 남았으니 프로그램을 닫고 {AppStorage.Root()} 폴더를 직접 지워 주세요.";
        }
        finally
        {
            IngameBusy = false;
            NotifyIngameCommands();
        }
    }

    private static InstallReport ToReport(ElevatedRunResult result) => new(result.Success, result.Message, []);
}
