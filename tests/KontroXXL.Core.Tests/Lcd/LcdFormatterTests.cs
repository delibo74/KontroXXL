using KontroXXL.Core.Lcd;
using Xunit;

namespace KontroXXL.Core.Tests.Lcd;

public class LcdFormatterTests
{
    static readonly DateTime Now = new(2026, 9, 1, 4, 35, 0);

    static LcdViewData Data(bool nasOnline = true) => new(
        Cpu: 76, CpuGhz: 3.6, Ram: 42,
        Gpu: 45, GpuTemp: 68, GpuFan: 30, NetMbps: 7,
        NasCpu: 55, NasTemp: 42, NasRx: 3, NasTx: 0, NasAlerts: 2, NasOnline: nasOnline,
        AppNames: new[] { "plex", "sonarr" }, AppRunning: new[] { true, false },
        PoolNames: new[] { "NasServer", "ssd-app" }, PoolUsed: new[] { 67, 68 },
        ShortcutNames: new[] { "Obsidian", "Müzik Çalar" });

    static LcdRenderContext Ctx(bool volume = false, int volumePct = 0,
                                string? ticker = null, int scroll = 0) =>
        new(Now, ScrollOffset: scroll, VolumeActive: volume, VolumePercent: volumePct,
            TickerText: ticker, TickerOffset: 0);

    // --- EN ÖNEMLİ TEST: 16 karakter invaryantı ---

    public static IEnumerable<object[]> AllStates()
    {
        foreach (var mode in Enum.GetValues<LcdMode>())
            for (int page = 0; page < 4; page++)
                for (int index = 0; index < 3; index++)
                    yield return new object[] { new LcdMenuState(mode, index, page) };
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Every_frame_is_exactly_16_characters(LcdMenuState state)
    {
        foreach (var data in new[] { Data(true), Data(false) })
        foreach (var ctx in new[] { Ctx(), Ctx(volume: true, volumePct: 55), Ctx(ticker: "! YENI ALARM !") })
        {
            var f = LcdFormatter.Render(state, data, ctx);
            Assert.Equal(16, f.Line0.Length);
            Assert.Equal(16, f.Line1.Length);
        }
    }

    [Fact]
    public void Empty_lists_do_not_throw_and_still_produce_16_chars()
    {
        var empty = Data() with
        {
            AppNames = Array.Empty<string>(), AppRunning = Array.Empty<bool>(),
            PoolNames = Array.Empty<string>(), PoolUsed = Array.Empty<int>(),
            ShortcutNames = Array.Empty<string>(),
        };

        foreach (var mode in new[] { LcdMode.Apps, LcdMode.Pools, LcdMode.Shortcuts })
        {
            var f = LcdFormatter.Render(new LcdMenuState(mode, 0, 0), empty, Ctx());
            Assert.Equal(16, f.Line0.Length);
            Assert.Equal(16, f.Line1.Length);
        }
    }

    // --- Sayfa içerikleri (v2 davranışıyla birebir) ---

    [Fact]
    public void Home_page0_shows_cpu_left_and_frequency_right()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 0), Data(), Ctx());
        Assert.Equal("CPU:76%    3.60G", f.Line0);
        Assert.Equal("RAM:42%    04:35", f.Line1);
    }

    [Fact]
    public void Home_page0_still_fits_at_100_percent()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 0),
                                    Data() with { Cpu = 100, Ram = 100 }, Ctx());
        Assert.Equal("CPU:100%   3.60G", f.Line0);
        Assert.Equal(16, f.Line1.Length);
    }

    [Fact]
    public void Home_page1_shows_gpu_and_network()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 1), Data(), Ctx());
        Assert.Equal("GPU:45% 68C     ", f.Line0);
        Assert.Equal("Fan:30% 7Mbps   ", f.Line1);
    }

    [Fact]
    public void Home_page2_shows_offline_banner_when_nas_is_down()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 2), Data(nasOnline: false), Ctx());
        Assert.Equal("  NAS: OFFLINE  ", f.Line0);
        Assert.Equal(" No Connection  ", f.Line1);
    }

    [Fact]
    public void Home_page2_uses_the_custom_arrow_bytes_for_rx_and_tx()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 2), Data(), Ctx());
        Assert.Equal("NAS:55% 42C     ", f.Line0);
        Assert.StartsWith("\x01", f.Line1);
        Assert.Contains("\x02", f.Line1);
    }

    [Fact]
    public void Home_page3_shows_alert_count()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 3), Data(), Ctx());
        Assert.Equal("> NAS DASHBOARD ", f.Line0);
        Assert.Equal("2 SYSTEM ALERTS!", f.Line1);
    }

    [Fact]
    public void Home_page3_shows_the_calm_message_at_zero_alerts()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 3), Data() with { NasAlerts = 0 }, Ctx());
        Assert.Equal("No active alerts", f.Line1);
    }

    // --- Kaplamalar ---

    [Fact]
    public void Volume_overlay_takes_over_home_and_sets_the_bar()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 1), Data(), Ctx(volume: true, volumePct: 55));
        Assert.Equal(" SYSTEM VOLUME  ", f.Line0);
        Assert.Equal(55, f.BarValue);
    }

    [Fact]
    public void Ticker_overrides_line0_except_on_the_alert_page()
    {
        var withTicker = Ctx(ticker: "! YENI ALARM: 2 uyari aktif !  ");

        var onPage0 = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 0), Data(), withTicker);
        Assert.StartsWith("! YENI", onPage0.Line0);

        var onPage3 = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 3), Data(), withTicker);
        Assert.Equal("> NAS DASHBOARD ", onPage3.Line0);
    }

    // --- Alt modlar ---

    [Fact]
    public void Menu_lists_the_four_entries()
    {
        Assert.Equal("1. NAS APPS     ", LcdFormatter.Render(new LcdMenuState(LcdMode.Menu, 0, 0), Data(), Ctx()).Line1);
        Assert.Equal("4. NAS POWER    ", LcdFormatter.Render(new LcdMenuState(LcdMode.Menu, 3, 0), Data(), Ctx()).Line1);
    }

    [Fact]
    public void Apps_shows_the_name_and_running_state()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 0, 0), Data(), Ctx());
        Assert.Equal("plex            ", f.Line0);
        Assert.Equal(">> RUNNING <<   ", f.Line1);

        var stopped = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 1, 0), Data(), Ctx());
        Assert.Equal(">> STOPPED <<   ", stopped.Line1);
    }

    [Fact]
    public void Pools_puts_the_usage_on_the_bar()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Pools, 1, 0), Data(), Ctx());
        Assert.Equal("ssd-app         ", f.Line0);
        Assert.Equal(68, f.BarValue);
    }

    [Fact]
    public void Shortcut_names_are_transliterated_before_reaching_the_lcd()
    {
        // v2 hatası (A10): "Müzik Çalar" ekrana "M?zik ?alar" olarak gidiyordu.
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Shortcuts, 1, 0), Data(), Ctx());
        Assert.Equal("ACTIONS:        ", f.Line0);
        Assert.Equal("Muzik Calar     ", f.Line1);
    }

    [Fact]
    public void Nas_power_lists_its_three_entries()
    {
        Assert.Equal("1. NAS REBOOT   ", LcdFormatter.Render(new LcdMenuState(LcdMode.NasPower, 0, 0), Data(), Ctx()).Line1);
        Assert.Equal("2. NAS SHUTDOWN ", LcdFormatter.Render(new LcdMenuState(LcdMode.NasPower, 1, 0), Data(), Ctx()).Line1);
        Assert.Equal("3. CANCEL       ", LcdFormatter.Render(new LcdMenuState(LcdMode.NasPower, 2, 0), Data(), Ctx()).Line1);
    }

    [Fact]
    public void Long_names_scroll_with_the_supplied_offset()
    {
        var data = Data() with { AppNames = new[] { "cok-uzun-uygulama-adi-buraya" }, AppRunning = new[] { true } };
        var a = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 0, 0), data, Ctx(scroll: 0)).Line0;
        var b = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 0, 0), data, Ctx(scroll: 4)).Line0;
        Assert.NotEqual(a, b);
        Assert.Equal(16, b.Length);
    }
}
