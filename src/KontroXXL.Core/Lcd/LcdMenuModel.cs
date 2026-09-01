namespace KontroXXL.Core.Lcd;

public enum LcdMode { Home, Menu, Apps, Pools, Shortcuts, NasPower }

public enum LcdInput { Up, Down, Click, Back }

/// <summary>Durum makinesinin çağırandan yapmasını istediği tek yan etki.</summary>
public enum LcdEffect
{
    None, VolumeUp, VolumeDown, RequestSync,
    ToggleApp, RunShortcut, NasReboot, NasShutdown,
}

/// <summary>Girdi anındaki liste uzunlukları. Model bunun dışında hiçbir dünya bilgisine sahip değildir.</summary>
public readonly record struct MenuCounts(int Apps, int Pools, int Shortcuts);

public sealed record LcdMenuState(LcdMode Mode, int Index, int Page)
{
    public static readonly LcdMenuState Initial = new(LcdMode.Home, 0, 0);
}

public readonly record struct LcdTransition(LcdMenuState State, LcdEffect Effect, int EffectIndex);

/// <summary>
/// LCD menüsünün tamamı. Saf fonksiyon: aynı girdi her zaman aynı çıktıyı verir,
/// zamana veya paylaşılan duruma dokunmaz. v2'de bu mantık iki thread'den kilitsiz
/// erişilen alanlara dağılmıştı (A7).
/// </summary>
public static class LcdMenuModel
{
    public const int MenuItemCount = 4;      // NAS APPS / NAS POOLS / SHORTCUTS / NAS POWER
    public const int NasPowerItemCount = 3;  // REBOOT / SHUTDOWN / CANCEL
    public const int HomePageCount = 4;      // CPU / GPU / NAS / ALERTS

    public static LcdTransition Apply(LcdMenuState state, LcdInput input, MenuCounts counts)
    {
        int max = MaxFor(state.Mode, counts);
        int index = Clamp(state.Index, max);
        var s = state with { Index = index };

        return input switch
        {
            LcdInput.Back => Back(s),
            LcdInput.Up => Step(s, +1, max),
            LcdInput.Down => Step(s, -1, max),
            LcdInput.Click => Click(s, max),
            _ => new LcdTransition(s, LcdEffect.None, 0),
        };
    }

    static int MaxFor(LcdMode mode, MenuCounts c) => mode switch
    {
        LcdMode.Menu => MenuItemCount,
        LcdMode.Apps => c.Apps,
        LcdMode.Pools => c.Pools,
        LcdMode.Shortcuts => c.Shortcuts,
        LcdMode.NasPower => NasPowerItemCount,
        _ => 0,
    };

    static int Clamp(int index, int max)
    {
        if (max <= 0) return 0;
        if (index < 0) return 0;
        return index >= max ? max - 1 : index;
    }

    static LcdTransition Back(LcdMenuState s) =>
        s.Mode == LcdMode.Home
            ? new LcdTransition(s with { Page = (s.Page + 1) % HomePageCount }, LcdEffect.None, 0)
            : new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, LcdEffect.None, 0);

    static LcdTransition Step(LcdMenuState s, int delta, int max)
    {
        if (s.Mode == LcdMode.Home)
            return new LcdTransition(s, delta > 0 ? LcdEffect.VolumeUp : LcdEffect.VolumeDown, 0);

        if (max <= 0)
            return new LcdTransition(s with { Index = 0 }, LcdEffect.None, 0);

        int next = ((s.Index + delta) % max + max) % max;
        return new LcdTransition(s with { Index = next }, LcdEffect.None, 0);
    }

    static LcdTransition Click(LcdMenuState s, int max)
    {
        switch (s.Mode)
        {
            case LcdMode.Home:
                return new LcdTransition(s with { Mode = LcdMode.Menu, Index = 0 }, LcdEffect.None, 0);

            case LcdMode.Menu:
                return s.Index switch
                {
                    0 => new LcdTransition(s with { Mode = LcdMode.Apps, Index = 0 }, LcdEffect.RequestSync, 0),
                    1 => new LcdTransition(s with { Mode = LcdMode.Pools, Index = 0 }, LcdEffect.RequestSync, 0),
                    2 => new LcdTransition(s with { Mode = LcdMode.Shortcuts, Index = 0 }, LcdEffect.None, 0),
                    _ => new LcdTransition(s with { Mode = LcdMode.NasPower, Index = 0 }, LcdEffect.None, 0),
                };

            case LcdMode.Apps:
                return max <= 0
                    ? new LcdTransition(s with { Index = 0 }, LcdEffect.None, 0)
                    : new LcdTransition(s, LcdEffect.ToggleApp, s.Index);

            case LcdMode.Shortcuts:
                return max <= 0
                    ? new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, LcdEffect.None, 0)
                    : new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, LcdEffect.RunShortcut, s.Index);

            case LcdMode.NasPower:
                var effect = s.Index switch
                {
                    0 => LcdEffect.NasReboot,
                    1 => LcdEffect.NasShutdown,
                    _ => LcdEffect.None,
                };
                return new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, effect, 0);

            default: // Pools — v2'de de tıklamanın etkisi yok
                return new LcdTransition(s, LcdEffect.None, 0);
        }
    }
}
