using KontroXXL.Core.Security;
using Xunit;

namespace KontroXXL.Core.Tests.Security;

public class PlaintextSecretProtectorTests
{
    readonly ISecretProtector _p = new PlaintextSecretProtector();

    [Fact]
    public void Round_trips_a_value()
        => Assert.Equal("7-3gnEG", _p.Unprotect(_p.Protect("7-3gnEG")));

    [Fact]
    public void Protecting_null_or_empty_yields_empty()
    {
        Assert.Equal("", _p.Protect(null));
        Assert.Equal("", _p.Protect(""));
    }

    [Fact]
    public void Unprotecting_null_or_empty_yields_null()
    {
        Assert.Null(_p.Unprotect(null));
        Assert.Null(_p.Unprotect(""));
    }

    [Fact]
    public void Unprotect_returns_null_rather_than_throwing_on_garbage()
        => Assert.Null(_p.Unprotect("bu-gecerli-bir-sifreli-metin-degil-!!!"));
}
