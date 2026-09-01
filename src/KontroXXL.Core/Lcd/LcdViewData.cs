namespace KontroXXL.Core.Lcd;

/// <summary>Formatter'ın ihtiyaç duyduğu dünyanın anlık görüntüsü. Değişmez.</summary>
public sealed record LcdViewData(
    int Cpu, double CpuGhz, int Ram,
    int Gpu, int GpuTemp, int GpuFan, double NetMbps,
    int NasCpu, int NasTemp, double NasRx, double NasTx, int NasAlerts, bool NasOnline,
    IReadOnlyList<string> AppNames, IReadOnlyList<bool> AppRunning,
    IReadOnlyList<string> PoolNames, IReadOnlyList<int> PoolUsed,
    IReadOnlyList<string> ShortcutNames)
{
    public MenuCounts Counts => new(AppNames.Count, PoolNames.Count, ShortcutNames.Count);
}

/// <summary>
/// Zamana bağlı her şey buradan içeri verilir; böylece Render saf ve deterministik kalır.
/// </summary>
public sealed record LcdRenderContext(
    DateTime Now, int ScrollOffset,
    bool VolumeActive, int VolumePercent,
    string? TickerText, int TickerOffset);
