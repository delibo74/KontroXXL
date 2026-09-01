using System.Globalization;

namespace KontroXXL.Core.Lcd;

/// <summary>
/// Durum + veri -> 16x2 kare. Saf fonksiyon. Tüm çıktı LcdText.Fit'ten geçer,
/// dolayısıyla 16 karakter invaryantı yapısal olarak garanti altındadır.
/// </summary>
public static class LcdFormatter
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static LcdFrame Render(LcdMenuState state, LcdViewData d, LcdRenderContext ctx) => state.Mode switch
    {
        LcdMode.Home => RenderHome(state, d, ctx),
        LcdMode.Menu => new LcdFrame(LcdText.Fit("> SYSTEM MENU"), MenuItem(state.Index), null),
        LcdMode.Apps => RenderApps(state, d, ctx),
        LcdMode.Pools => RenderPools(state, d, ctx),
        LcdMode.Shortcuts => RenderShortcuts(state, d, ctx),
        LcdMode.NasPower => new LcdFrame(LcdText.Fit("> NAS POWER"), NasPowerItem(state.Index), null),
        _ => new LcdFrame(LcdText.Fit(""), LcdText.Fit(""), null),
    };

    static LcdFrame RenderHome(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (ctx.VolumeActive)
            return new LcdFrame(LcdText.Fit(" SYSTEM VOLUME"), LcdText.Fit(""), Clamp0To100(ctx.VolumePercent));

        (string l0, string l1) = s.Page switch
        {
            0 => (SplitLine($"CPU:{d.Cpu}%", d.CpuGhz.ToString("0.00", Inv) + "G"),
                  SplitLine($"RAM:{d.Ram}%", ctx.Now.ToString("HH:mm", Inv))),

            1 => ($"GPU:{d.Gpu}% {d.GpuTemp}C",
                  $"Fan:{d.GpuFan}% {(int)d.NetMbps}Mbps"),

            2 => d.NasOnline
                 ? ($"NAS:{d.NasCpu}% {d.NasTemp}C",
                    string.Format(Inv, "{0}{1,3}Mb {2}{3,3}Mb", LcdText.RxArrow, (int)d.NasRx, LcdText.TxArrow, (int)d.NasTx))
                 : ("  NAS: OFFLINE", " No Connection"),

            _ => ("> NAS DASHBOARD",
                  d.NasAlerts == 0 ? "No active alerts" : $"{d.NasAlerts} SYSTEM ALERTS!"),
        };

        // Ticker, alarm sayfası hariç üst satırı devralır.
        if (s.Page != 3 && !string.IsNullOrEmpty(ctx.TickerText))
            l0 = LcdText.Scroll(ctx.TickerText, ctx.TickerOffset);

        return new LcdFrame(LcdText.Fit(l0), LcdText.Fit(l1), null);
    }

    static LcdFrame RenderApps(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (d.AppNames.Count == 0)
            return new LcdFrame(LcdText.Fit(" APPLICATIONS"), LcdText.Fit(" Syncing..."), null);

        int i = SafeIndex(s.Index, d.AppNames.Count);
        bool running = i < d.AppRunning.Count && d.AppRunning[i];
        return new LcdFrame(
            LcdText.Scroll(d.AppNames[i], ctx.ScrollOffset),
            LcdText.Fit(running ? ">> RUNNING <<" : ">> STOPPED <<"),
            null);
    }

    static LcdFrame RenderPools(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (d.PoolNames.Count == 0)
            return new LcdFrame(LcdText.Fit(" STORAGE POOLS"), LcdText.Fit(" Syncing..."), null);

        int i = SafeIndex(s.Index, d.PoolNames.Count);
        int used = i < d.PoolUsed.Count ? d.PoolUsed[i] : 0;
        return new LcdFrame(LcdText.Scroll(d.PoolNames[i], ctx.ScrollOffset), LcdText.Fit(""), Clamp0To100(used));
    }

    static LcdFrame RenderShortcuts(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (d.ShortcutNames.Count == 0)
            return new LcdFrame(LcdText.Fit("ACTIONS:"), LcdText.Fit(" No Shortcuts"), null);

        int i = SafeIndex(s.Index, d.ShortcutNames.Count);
        return new LcdFrame(LcdText.Fit("ACTIONS:"), LcdText.Scroll(d.ShortcutNames[i], ctx.ScrollOffset), null);
    }

    static string MenuItem(int index) => LcdText.Fit(index switch
    {
        0 => "1. NAS APPS",
        1 => "2. NAS POOLS",
        2 => "3. SHORTCUTS",
        _ => "4. NAS POWER",
    });

    static string NasPowerItem(int index) => LcdText.Fit(index switch
    {
        0 => "1. NAS REBOOT",
        1 => "2. NAS SHUTDOWN",
        _ => "3. CANCEL",
    });

    /// <summary>Sol kısım sola, sağ kısım sağa yaslı; toplam 16 karakter.</summary>
    static string SplitLine(string left, string right)
    {
        int gap = LcdText.Width - left.Length - right.Length;
        return gap > 0 ? left + new string(' ', gap) + right : left + right;
    }

    static int SafeIndex(int index, int count) => count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
    static int Clamp0To100(int v) => Math.Clamp(v, 0, 100);
}
