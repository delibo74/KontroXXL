using System;
using KontroXXL.Core.Serial;
using Xunit;

namespace KontroXXL.Core.Tests.Serial;

public class SerialReconnectPolicyTests
{
    [Fact]
    public void First_failure_waits_the_base_delay_and_is_logged()
    {
        var p = new SerialReconnectPolicy(baseDelayMs: 2000, maxDelayMs: 30000);

        var d = p.OnFailure("port yok");

        Assert.Equal(2000, d.DelayMs);
        Assert.True(d.ShouldLog);
        Assert.Contains("port yok", d.Message);
        Assert.Equal(1, p.ConsecutiveFailures);
    }

    [Fact]
    public void Delay_doubles_on_each_repeated_failure()
    {
        var p = new SerialReconnectPolicy(baseDelayMs: 2000, maxDelayMs: 30000);

        Assert.Equal(2000, p.OnFailure("x").DelayMs);
        Assert.Equal(4000, p.OnFailure("x").DelayMs);
        Assert.Equal(8000, p.OnFailure("x").DelayMs);
        Assert.Equal(16000, p.OnFailure("x").DelayMs);
    }

    [Fact]
    public void Delay_is_capped_at_the_maximum()
    {
        var p = new SerialReconnectPolicy(baseDelayMs: 2000, maxDelayMs: 30000);

        for (int i = 0; i < 20; i++) p.OnFailure("x");

        Assert.Equal(30000, p.DelayFor(p.ConsecutiveFailures));
        Assert.Equal(30000, p.OnFailure("x").DelayMs);
    }

    // Asil sikayet buydu: app.log'da 822 kayit. Ayni hata tekrarlarken susmali.
    [Fact]
    public void Repeated_identical_failure_is_logged_only_once()
    {
        var p = new SerialReconnectPolicy();

        Assert.True(p.OnFailure("I/O aborted").ShouldLog);
        Assert.False(p.OnFailure("I/O aborted").ShouldLog);
        Assert.False(p.OnFailure("I/O aborted").ShouldLog);
    }

    [Fact]
    public void A_different_error_is_logged_again_because_it_is_a_new_event()
    {
        var p = new SerialReconnectPolicy();

        Assert.True(p.OnFailure("I/O aborted").ShouldLog);
        Assert.False(p.OnFailure("I/O aborted").ShouldLog);
        Assert.True(p.OnFailure("port bulunamadi").ShouldLog);
    }

    [Fact]
    public void A_successful_connection_resets_delay_and_log_throttling()
    {
        var p = new SerialReconnectPolicy(baseDelayMs: 2000, maxDelayMs: 30000);
        p.OnFailure("x"); p.OnFailure("x"); p.OnFailure("x");

        p.OnConnected();

        Assert.Equal(0, p.ConsecutiveFailures);
        var d = p.OnFailure("x");
        Assert.Equal(2000, d.DelayMs);
        Assert.True(d.ShouldLog);   // yeni bir kopma olayi, yeniden loglanir
    }

    [Fact]
    public void Blank_error_still_produces_a_usable_message()
    {
        var p = new SerialReconnectPolicy();

        var d = p.OnFailure("   ");

        Assert.True(d.ShouldLog);
        Assert.Contains("bilinmeyen hata", d.Message);
    }

    [Fact]
    public void Null_error_is_treated_like_a_blank_one()
    {
        var p = new SerialReconnectPolicy();
        Assert.Contains("bilinmeyen hata", p.OnFailure(null).Message);
    }

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(5, 30000)]
    [InlineData(100, 30000)]
    public void DelayFor_is_a_pure_function_of_the_failure_count(int failures, int expected)
    {
        var p = new SerialReconnectPolicy(baseDelayMs: 2000, maxDelayMs: 30000);
        Assert.Equal(expected, p.DelayFor(failures));
    }

    [Fact]
    public void Invalid_construction_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerialReconnectPolicy(0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerialReconnectPolicy(2000, 1000));
    }
}
