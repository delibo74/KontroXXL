using System;
using System.Collections.Generic;
using System.Linq;
using KontroXXL.Core.Diagnostics;
using Xunit;

namespace KontroXXL.Core.Tests.Diagnostics;

public class AlertNotificationPolicyTests
{
    static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    static NasAlert A(string id, string level = "CRITICAL", string? title = null) =>
        new NasAlert(id, level, title ?? ("alarm " + id));

    static AlertNotificationOptions NoThrottle() =>
        new AlertNotificationOptions { Throttle = TimeSpan.Zero };

    /// <summary>Ilk tick'i taban olarak isler, sonraki tick'lerin baslayacagi durumu verir.</summary>
    static AlertNotificationState Primed(params NasAlert[] existing) =>
        AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial, existing, NoThrottle(), T0).NextState;

    // ---- 1. Acilista taban ------------------------------------------------

    [Fact]
    public void First_read_is_silent_even_with_open_alerts()
    {
        // Bozukluk 3: eski sayac 0'dan basliyordu, mevcut alarmlar "yeni" sayiliyordu.
        var d = AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial,
            new[] { A("1"), A("2") },
            NoThrottle(), T0);

        Assert.False(d.ShouldNotify);
        Assert.Equal(0, d.NewAlertCount);
        Assert.True(d.NextState.Primed);
        Assert.Equal(new[] { "1", "2" }, d.NextState.NotifiedIds.OrderBy(x => x));
    }

    [Fact]
    public void Alert_arriving_after_the_baseline_notifies()
    {
        var state = Primed(A("1"));

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("1"), A("2", title: "havuz bozuldu") }, NoThrottle(), T0);

        Assert.True(d.ShouldNotify);
        Assert.Equal(1, d.NewAlertCount);
        Assert.Contains("havuz bozuldu", d.Body);
        Assert.Contains("2", d.NextState.NotifiedIds);
    }

    [Fact]
    public void Empty_first_read_still_primes()
    {
        var d = AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial, Array.Empty<NasAlert>(), NoThrottle(), T0);

        Assert.False(d.ShouldNotify);
        Assert.True(d.NextState.Primed);
        Assert.Empty(d.NextState.NotifiedIds);
    }

    // ---- 2. Kimlik tabanli takip ------------------------------------------

    [Fact]
    public void Same_alert_is_not_announced_twice()
    {
        // SKT bildirimindeki "dakikada bir" hatasinin tekrarlanmamasi.
        var state = Primed();

        var first = AlertNotificationPolicy.Decide(state, new[] { A("7") }, NoThrottle(), T0);
        Assert.True(first.ShouldNotify);

        var second = AlertNotificationPolicy.Decide(
            first.NextState, new[] { A("7") }, NoThrottle(), T0.AddHours(1));

        Assert.False(second.ShouldNotify);
        Assert.Equal(0, second.NewAlertCount);
    }

    [Fact]
    public void Closed_then_reopened_alert_counts_as_new()
    {
        var state = Primed();

        var opened = AlertNotificationPolicy.Decide(state, new[] { A("7") }, NoThrottle(), T0);
        Assert.True(opened.ShouldNotify);

        // Alarm kapandi (listeden dustu) — kimlik takip kumesinden de dusmeli.
        var closed = AlertNotificationPolicy.Decide(
            opened.NextState, Array.Empty<NasAlert>(), NoThrottle(), T0.AddMinutes(1));
        Assert.False(closed.ShouldNotify);
        Assert.Empty(closed.NextState.NotifiedIds);

        // Ayni alarm yeniden acildi: bu YENI bir olaydir.
        var reopened = AlertNotificationPolicy.Decide(
            closed.NextState, new[] { A("7") }, NoThrottle(), T0.AddMinutes(2));

        Assert.True(reopened.ShouldNotify);
        Assert.Equal(1, reopened.NewAlertCount);
    }

    [Fact]
    public void Swap_within_one_tick_is_caught_even_though_the_count_is_unchanged()
    {
        // Bozukluk 1: eski tetik "na > _prevNasAlertCount" idi. Burada sayi 1 -> 1.
        var state = Primed(A("eski"));

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("yeni", title: "disk arizasi") }, NoThrottle(), T0);

        Assert.True(d.ShouldNotify);
        Assert.Equal(1, d.NewAlertCount);
        Assert.Contains("disk arizasi", d.Body);
        Assert.DoesNotContain("eski", d.NextState.NotifiedIds);
    }

    [Fact]
    public void Duplicate_ids_in_one_payload_are_announced_once()
    {
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("7"), A("7") }, NoThrottle(), T0);

        Assert.Equal(1, d.NewAlertCount);
        Assert.Single(d.NextState.NotifiedIds);
    }

    // ---- 3. Seviye filtresi -----------------------------------------------

    [Theory]
    [InlineData("INFO", false)]
    [InlineData("notice", false)]
    [InlineData("WARNING", true)]
    [InlineData("error", true)]
    [InlineData("CRITICAL", true)]
    [InlineData("EMERGENCY", true)]
    public void Level_threshold_decides_who_gets_a_balloon(string level, bool expected)
    {
        // Bozukluk 2: 'level' hic okunmuyordu, "kritik alarm" = "herhangi bir alarm" idi.
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("1", level) }, NoThrottle(), T0);

        Assert.Equal(expected, d.ShouldNotify);
    }

    [Fact]
    public void Threshold_can_be_raised_to_critical_only()
    {
        var opts = new AlertNotificationOptions
        {
            MinimumLevel = NasAlertLevel.Critical,
            Throttle = TimeSpan.Zero,
        };
        var state = AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial, Array.Empty<NasAlert>(), opts, T0).NextState;

        var warn = AlertNotificationPolicy.Decide(state, new[] { A("1", "WARNING") }, opts, T0);
        var crit = AlertNotificationPolicy.Decide(state, new[] { A("2", "CRITICAL") }, opts, T0);

        Assert.False(warn.ShouldNotify);
        Assert.True(crit.ShouldNotify);
    }

    [Fact]
    public void An_alert_escalating_past_the_threshold_notifies()
    {
        // Ayni kimlik INFO iken takip edilmiyordu; CRITICAL'e cikinca yeni sayilir.
        var state = Primed(A("1", "INFO"));

        var d = AlertNotificationPolicy.Decide(state, new[] { A("1", "CRITICAL") }, NoThrottle(), T0);

        Assert.True(d.ShouldNotify);
    }

    [Theory]
    [InlineData(null, NasAlertLevel.Unknown)]
    [InlineData("", NasAlertLevel.Unknown)]
    [InlineData("   ", NasAlertLevel.Unknown)]
    [InlineData("wat", NasAlertLevel.Unknown)]
    [InlineData(" critical ", NasAlertLevel.Critical)]
    public void Level_parsing_never_throws(string? raw, NasAlertLevel expected)
    {
        Assert.Equal(expected, AlertNotificationPolicy.ParseLevel(raw));
    }

    [Fact]
    public void Unreadable_level_is_treated_as_warning_not_swallowed()
    {
        // Sessiz yutma spec bolum 9'a aykiri: bilinmeyen seviye WARNING gibi davranir.
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(state, new[] { A("1", "SOMETHING_NEW") }, NoThrottle(), T0);

        Assert.True(d.ShouldNotify);
        Assert.Equal(
            AlertNotificationPolicy.SeverityRank(NasAlertLevel.Warning),
            AlertNotificationPolicy.SeverityRank(NasAlertLevel.Unknown));
    }

    // ---- 4. Kisma (throttle) ----------------------------------------------

    [Fact]
    public void Second_burst_inside_the_throttle_window_is_held_not_lost()
    {
        var opts = new AlertNotificationOptions { Throttle = TimeSpan.FromMinutes(5) };
        var state = AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial, Array.Empty<NasAlert>(), opts, T0).NextState;

        var first = AlertNotificationPolicy.Decide(state, new[] { A("1") }, opts, T0);
        Assert.True(first.ShouldNotify);

        // Pencere icinde: susulur ama kimlik kumeye GIRMEZ.
        var held = AlertNotificationPolicy.Decide(
            first.NextState, new[] { A("1"), A("2") }, opts, T0.AddMinutes(1));
        Assert.False(held.ShouldNotify);
        Assert.DoesNotContain("2", held.NextState.NotifiedIds);

        // Pencere dolunca birikenler TEK balonda cikar.
        var released = AlertNotificationPolicy.Decide(
            held.NextState, new[] { A("1"), A("2"), A("3") }, opts, T0.AddMinutes(6));
        Assert.True(released.ShouldNotify);
        Assert.Equal(2, released.NewAlertCount);
    }

    [Fact]
    public void Throttle_does_not_delay_the_very_first_balloon()
    {
        var opts = new AlertNotificationOptions { Throttle = TimeSpan.FromHours(1) };
        var state = AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial, Array.Empty<NasAlert>(), opts, T0).NextState;

        var d = AlertNotificationPolicy.Decide(state, new[] { A("1") }, opts, T0);

        Assert.True(d.ShouldNotify);
        Assert.Equal(T0, d.NextState.LastNotifiedAt);
    }

    // ---- 5. Metin ---------------------------------------------------------

    [Fact]
    public void Body_lists_at_most_max_lines_and_says_how_many_are_hidden()
    {
        var opts = new AlertNotificationOptions { Throttle = TimeSpan.Zero, MaxBodyLines = 2 };
        var state = AlertNotificationPolicy.Decide(
            AlertNotificationState.Initial, Array.Empty<NasAlert>(), opts, T0).NextState;

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("1"), A("2"), A("3"), A("4") }, opts, T0);

        Assert.Equal(4, d.NewAlertCount);
        Assert.Contains("4 yeni uyarı", d.Title);
        Assert.Contains("2 tane daha", d.Body);
    }

    [Fact]
    public void Turkish_characters_survive_the_body()
    {
        // Balon Unicode tasir: LcdText.Sanitize BURADA kullanilmamali.
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("1", title: "Havuz ÇÖKTÜ: şişme algılandı") }, NoThrottle(), T0);

        Assert.Contains("Havuz ÇÖKTÜ: şişme algılandı", d.Body);
    }

    [Fact]
    public void Body_is_capped_so_the_shell_balloon_does_not_truncate_mid_word()
    {
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(
            state, new[] { A("1", title: new string('x', 500)) }, NoThrottle(), T0);

        Assert.True(d.Body.Length <= AlertNotificationPolicy.MaxBodyLength);
        Assert.EndsWith("…", d.Body);
    }

    // ---- 6. Bos / bozuk girdi ---------------------------------------------

    [Fact]
    public void Null_inputs_are_tolerated()
    {
        var d = AlertNotificationPolicy.Decide(null, null, null, T0);

        Assert.False(d.ShouldNotify);
        Assert.True(d.NextState.Primed);
        Assert.Empty(d.NextState.NotifiedIds);
    }

    [Fact]
    public void Null_entries_inside_the_list_are_skipped()
    {
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(
            state, new NasAlert?[] { null, A("1"), null }, NoThrottle(), T0);

        Assert.True(d.ShouldNotify);
        Assert.Equal(1, d.NewAlertCount);
    }

    [Fact]
    public void Alert_without_an_id_is_tracked_by_its_text_so_it_neither_vanishes_nor_spams()
    {
        var state = Primed();
        var noId = new NasAlert(id: "  ", level: "CRITICAL", title: "kimliksiz ariza");

        var first = AlertNotificationPolicy.Decide(state, new[] { noId }, NoThrottle(), T0);
        Assert.True(first.ShouldNotify);
        Assert.Contains("kimliksiz ariza", first.Body);

        var second = AlertNotificationPolicy.Decide(
            first.NextState, new[] { noId }, NoThrottle(), T0.AddHours(1));
        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void Alert_with_neither_id_nor_text_is_ignored()
    {
        var state = Primed();

        var d = AlertNotificationPolicy.Decide(
            state, new[] { new NasAlert(null, "CRITICAL", null) }, NoThrottle(), T0);

        Assert.False(d.ShouldNotify);
        Assert.Empty(d.NextState.NotifiedIds);
    }

    [Fact]
    public void State_is_not_mutated_by_a_decision()
    {
        var state = Primed(A("1"));
        var before = state.NotifiedIds.ToArray();

        AlertNotificationPolicy.Decide(state, new[] { A("2") }, NoThrottle(), T0);

        Assert.Equal(before, state.NotifiedIds.ToArray());
    }
}
