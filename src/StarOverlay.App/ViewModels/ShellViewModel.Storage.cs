using System.Diagnostics;
using StarOverlay.Core;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

/// <summary>"저장 위치": tells the user where the settings and records live and opens that folder.</summary>
public partial class ShellViewModel
{
    /// <summary>The folder with the settings, the saved jobs, the fight records and the translation model.</summary>
    public string DataFolder { get; } = AppStorage.Root();

    /// <summary>What the data folder holds, in the user's words.</summary>
    public string StorageData => "창 위치와 플러그인 설정, 세부직업 기록, 전투 기록, 번역 모델이 들어 있습니다. 제거해도 지워지지 않으니, 완전히 지우려면 이 폴더를 직접 삭제하세요.";

    /// <summary>What goes into the game folder.</summary>
    public string StorageGame => "오버레이 파일(로더와 연결 정보)만 들어갑니다. 제거하면 설치 전 상태로 돌아갑니다.";

    /// <summary>Where the receiving helper is.</summary>
    public string StorageHelper => @"C:\Program Files\StarOverlay\Capture에 설치되고, 작업 스케줄러에 'StarOverlay.Capture' 항목이 하나 등록됩니다.";

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            if (!OperatingSystem.IsWindows())
            {
                Status = "폴더 열기는 Windows에서만 쓸 수 있습니다. 위치: " + DataFolder;
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DataFolder + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Status = "폴더를 열지 못했습니다. 위치: " + DataFolder;
        }
    }
}
