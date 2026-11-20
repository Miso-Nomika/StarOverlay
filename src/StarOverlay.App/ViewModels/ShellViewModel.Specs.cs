using System.Collections.ObjectModel;
using System.Globalization;
using StarOverlay.Core;
using StarOverlay.Framework;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

/// <summary>One line of the saved detailed-job list.</summary>
public sealed class SpecRowViewModel(SpecRow row)
{
    public string Title { get; } = $"{row.Id} · {row.Spec}";

    public string Detail { get; } = Describe(row);

    static string Describe(SpecRow row)
    {
        var parts = new List<string> { row.Confirmed ? "확정" : "추정" };
        if (row.FightPoint > 0) parts.Add("전투력 " + row.FightPoint.ToString("N0", CultureInfo.InvariantCulture));
        if (row.FarSea) parts.Add("먼바다");
        return string.Join(" · ", parts);
    }
}

public partial class ShellViewModel
{
    private readonly string _specPath;

    /// <summary>The detailed jobs the service has saved (spec.db), shown read-only, by uid.</summary>
    public ObservableCollection<SpecRowViewModel> Specs { get; } = [];

    public bool HasSpecs => Specs.Count > 0;

    [ObservableProperty]
    private string _specsText = "";

    [RelayCommand]
    private void RefreshSpecs()
    {
        var rows = SpecStore.ReadAll(_specPath);
        Specs.Clear();
        foreach (var row in rows) Specs.Add(new SpecRowViewModel(row));
        SpecsText = rows.Count == 0
            ? "아직 저장된 세부직업이 없습니다. 게임에서 파티 신청이나 전투를 보면 쌓입니다."
            : $"저장된 세부직업 {rows.Count.ToString("N0", CultureInfo.InvariantCulture)}명 (uid · 세부직업, 확정은 직접 확인한 값)";
        OnPropertyChanged(nameof(HasSpecs));
    }

    private static string DefaultSpecPath() => Path.Combine(AppStorage.Root(), "spec.db");
}
