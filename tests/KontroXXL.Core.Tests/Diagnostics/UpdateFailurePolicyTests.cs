using KontroXXL.Core.Diagnostics;
using Xunit;

namespace KontroXXL.Core.Tests.Diagnostics;

public class UpdateFailurePolicyTests
{
    [Fact]
    public void Failure_before_teardown_keeps_the_app_running()
    {
        // Indirme/denetim asamasinda hata: uygulama hala saglam, kapanmasi gerekmiyor.
        var r = UpdateFailurePolicy.Describe(tornDown: false, error: "sunucuya ulasilamadi");

        Assert.False(r.MustExit);
        Assert.StartsWith(UpdateFailurePolicy.RecoverableHeader, r.Message);
        Assert.Contains("sunucuya ulasilamadi", r.Message);
    }

    [Fact]
    public void Failure_after_teardown_must_exit()
    {
        // ApplyUpdatesAndRestart firlatti: seri port kapali, LCD veda yazdi, tepsi gizli.
        // Bu noktada "uyar ve devam et" gizli, kullanicinin ulasamadigi bir surec birakir.
        var r = UpdateFailurePolicy.Describe(tornDown: true, error: "dosya kilitli");

        Assert.True(r.MustExit);
        Assert.StartsWith(UpdateFailurePolicy.FatalHeader, r.Message);
        Assert.Contains("dosya kilitli", r.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_error_text_still_says_something(string? error)
    {
        // Spec 9: sessiz basarisizlik yasak — bos mesajli bir kutu de sessizliktir.
        foreach (var tornDown in new[] { false, true })
        {
            var r = UpdateFailurePolicy.Describe(tornDown, error);
            Assert.Contains(UpdateFailurePolicy.UnknownError, r.Message);
        }
    }

    [Fact]
    public void Error_text_is_trimmed_into_the_message()
    {
        var r = UpdateFailurePolicy.Describe(tornDown: false, error: "  bosluklu  ");

        Assert.EndsWith("bosluklu", r.Message);
    }

    [Fact]
    public void The_two_headers_are_distinguishable()
    {
        // Ayni metin cikarsa kullanici "kapaniyor" uyarisini "denetlenemedi" sanir.
        Assert.NotEqual(UpdateFailurePolicy.RecoverableHeader, UpdateFailurePolicy.FatalHeader);
    }
}
