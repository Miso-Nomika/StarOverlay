namespace BPSR_ZDPSLib;

public class NetCapConfig
{
    public string CaptureDeviceName { get; set; } = string.Empty;
    public string[] ExeNames { get; set; } = ["BPSR", "BPSR_STEAM"];
    public TimeSpan ConnectionScanInterval { get; set; } = TimeSpan.FromSeconds(10);

    // BPSR-SL addition: true when the packet source already filtered the game's connections
    // (raw-socket helper filters by PID, recorded frames in tests). Skips the exe-name TCP table lookup.
    public bool TrustSource { get; set; } = false;
}
