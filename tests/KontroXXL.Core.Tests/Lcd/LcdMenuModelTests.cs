using KontroXXL.Core.Lcd;
using Xunit;

namespace KontroXXL.Core.Tests.Lcd;

public class LcdMenuModelTests
{
    static readonly MenuCounts Counts = new(Apps: 3, Pools: 2, Shortcuts: 4);
    static readonly MenuCounts Empty  = new(Apps: 0, Pools: 0, Shortcuts: 0);

    static LcdTransition Apply(LcdMenuState s, LcdInput i, MenuCounts? c = null)
        => LcdMenuModel.Apply(s, i, c ?? Counts);

    static int MaxFor(LcdMode mode, MenuCounts c) => mode switch
    {
        LcdMode.Menu => LcdMenuModel.MenuItemCount,
        LcdMode.Apps => c.Apps,
        LcdMode.Pools => c.Pools,
        LcdMode.Shortcuts => c.Shortcuts,
        LcdMode.NasPower => LcdMenuModel.NasPowerItemCount,
        _ => 0,
    };

    [Fact]
    public void Home_up_and_down_change_volume_without_changing_state()
    {
        var t = Apply(LcdMenuState.Initial, LcdInput.Up);
        Assert.Equal(LcdEffect.VolumeUp, t.Effect);
        Assert.Equal(LcdMenuState.Initial, t.State);

        Assert.Equal(LcdEffect.VolumeDown, Apply(LcdMenuState.Initial, LcdInput.Down).Effect);
    }

    [Fact]
    public void Home_back_cycles_through_four_pages()
    {
        var s = LcdMenuState.Initial;
        for (int expected = 1; expected <= 4; expected++)
        {
            s = Apply(s, LcdInput.Back).State;
            Assert.Equal(expected % 4, s.Page);
            Assert.Equal(LcdMode.Home, s.Mode);
        }
    }

    [Fact]
    public void Home_click_opens_menu_at_index_zero()
    {
        var s = Apply(new LcdMenuState(LcdMode.Home, 7, 2), LcdInput.Click).State;
        Assert.Equal(LcdMode.Menu, s.Mode);
        Assert.Equal(0, s.Index);
        Assert.Equal(2, s.Page); // sayfa hatırlanır
    }

    [Theory]
    [InlineData(0, LcdMode.Apps,      LcdEffect.RequestSync)]
    [InlineData(1, LcdMode.Pools,     LcdEffect.RequestSync)]
    [InlineData(2, LcdMode.Shortcuts, LcdEffect.None)]
    [InlineData(3, LcdMode.NasPower,  LcdEffect.None)]
    public void Menu_click_enters_the_selected_submode(int index, LcdMode mode, LcdEffect effect)
    {
        var t = Apply(new LcdMenuState(LcdMode.Menu, index, 0), LcdInput.Click);
        Assert.Equal(mode, t.State.Mode);
        Assert.Equal(0, t.State.Index);
        Assert.Equal(effect, t.Effect);
    }

    [Fact]
    public void Menu_index_wraps_in_both_directions()
    {
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Menu, 3, 0), LcdInput.Up).State.Index);
        Assert.Equal(3, Apply(new LcdMenuState(LcdMode.Menu, 0, 0), LcdInput.Down).State.Index);
    }

    [Fact]
    public void Apps_index_wraps_over_the_actual_list_length()
    {
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Apps, 2, 0), LcdInput.Up).State.Index);
        Assert.Equal(2, Apply(new LcdMenuState(LcdMode.Apps, 0, 0), LcdInput.Down).State.Index);
    }

    [Fact]
    public void Index_is_clamped_to_zero_when_the_list_is_empty()
    {
        // v2 hatası (A7): liste boşken index bırakılıyordu, formatter appsList[index] ile patlıyordu.
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Apps, 5, 0), LcdInput.Up, Empty).State.Index);
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Apps, 5, 0), LcdInput.Down, Empty).State.Index);
    }

    [Fact]
    public void Index_is_clamped_when_the_list_shrank_since_the_last_input()
    {
        // 5. uygulamada duruyorduk, liste 3'e düştü.
        var t = Apply(new LcdMenuState(LcdMode.Apps, 5, 0), LcdInput.Click);
        Assert.Equal(LcdEffect.ToggleApp, t.Effect);
        Assert.InRange(t.EffectIndex, 0, 2);
    }

    [Fact]
    public void Apps_click_toggles_the_selected_app()
    {
        var t = Apply(new LcdMenuState(LcdMode.Apps, 1, 0), LcdInput.Click);
        Assert.Equal(LcdEffect.ToggleApp, t.Effect);
        Assert.Equal(1, t.EffectIndex);
        Assert.Equal(LcdMode.Apps, t.State.Mode); // listede kalır
    }

    [Fact]
    public void Apps_click_does_nothing_when_the_list_is_empty()
        => Assert.Equal(LcdEffect.None, Apply(new LcdMenuState(LcdMode.Apps, 0, 0), LcdInput.Click, Empty).Effect);

    [Fact]
    public void Pools_click_does_nothing()
        => Assert.Equal(LcdEffect.None, Apply(new LcdMenuState(LcdMode.Pools, 1, 0), LcdInput.Click).Effect);

    [Fact]
    public void Shortcut_click_runs_it_and_returns_home()
    {
        var t = Apply(new LcdMenuState(LcdMode.Shortcuts, 2, 0), LcdInput.Click);
        Assert.Equal(LcdEffect.RunShortcut, t.Effect);
        Assert.Equal(2, t.EffectIndex);
        Assert.Equal(LcdMode.Home, t.State.Mode);
    }

    [Theory]
    [InlineData(0, LcdEffect.NasReboot)]
    [InlineData(1, LcdEffect.NasShutdown)]
    [InlineData(2, LcdEffect.None)]
    public void NasPower_click_fires_the_right_action_and_returns_home(int index, LcdEffect effect)
    {
        var t = Apply(new LcdMenuState(LcdMode.NasPower, index, 0), LcdInput.Click);
        Assert.Equal(effect, t.Effect);
        Assert.Equal(LcdMode.Home, t.State.Mode);
    }

    [Theory]
    [InlineData(LcdMode.Menu)]
    [InlineData(LcdMode.Apps)]
    [InlineData(LcdMode.Pools)]
    [InlineData(LcdMode.Shortcuts)]
    [InlineData(LcdMode.NasPower)]
    public void Back_from_any_submode_returns_home(LcdMode mode)
    {
        var t = Apply(new LcdMenuState(mode, 2, 1), LcdInput.Back);
        Assert.Equal(LcdMode.Home, t.State.Mode);
        Assert.Equal(0, t.State.Index);
        Assert.Equal(LcdEffect.None, t.Effect);
    }

    [Fact]
    public void Apply_never_returns_a_negative_or_out_of_range_index()
    {
        var rng = new Random(1234);
        var modes = Enum.GetValues<LcdMode>();
        var inputs = Enum.GetValues<LcdInput>();
        var s = LcdMenuState.Initial;

        for (int i = 0; i < 5000; i++)
        {
            var counts = new MenuCounts(rng.Next(0, 6), rng.Next(0, 6), rng.Next(0, 6));
            var before = s with { Mode = modes[rng.Next(modes.Length)] };
            var t = LcdMenuModel.Apply(before, inputs[rng.Next(inputs.Length)], counts);
            s = t.State;

            Assert.True(s.Index >= 0, $"negatif index: {s}");
            Assert.InRange(s.Page, 0, 3);

            // Asıl koruma: index, SONUÇ modunun liste uzunluğunu asla aşmamalı.
            // Boş listede 0 konvansiyondur, tek istisna odur.
            int max = MaxFor(s.Mode, counts);
            Assert.True(s.Index == 0 || s.Index < max,
                $"index tasti: {s}, max={max}, counts={counts}");

            // Yan etki indeksi de gerçek listenin içine düşmeli — çağıran
            // bununla doğrudan appsList[i] / Shortcuts[i] erişimi yapıyor.
            if (t.Effect == LcdEffect.ToggleApp)
                Assert.InRange(t.EffectIndex, 0, Math.Max(0, counts.Apps - 1));
            if (t.Effect == LcdEffect.RunShortcut)
                Assert.InRange(t.EffectIndex, 0, Math.Max(0, counts.Shortcuts - 1));
        }
    }
}
