using System.Net.Http.Headers;
using KontroXXL.Core.Security;
using Xunit;

namespace KontroXXL.Core.Tests.Security;

public class ApiKeyPolicyTests
{
    // 2026-09-04 canli hata: kullanicinin yapistirdigi anahtarin SONUNDA satir sonu vardi
    // ve uygulama HIC acilmiyordu. En kritik vaka bu.
    [Fact]
    public void Trailing_newline_is_stripped_and_key_stays_usable()
    {
        var e = ApiKeyPolicy.Evaluate("1-abcDEF123\n");

        Assert.Equal("1-abcDEF123", e.Key);
        Assert.Equal(ApiKeyStatus.Repaired, e.Status);
        Assert.True(e.IsUsable);
        Assert.Equal(ApiKeyPolicy.RepairedMessage, e.Message);
    }

    [Fact]
    public void Interior_crlf_is_joined_back_into_one_key()
    {
        // Web arayuzunden kopyalanan anahtar satira bolunmus olabilir; birlestirmek dogru onarim.
        var e = ApiKeyPolicy.Evaluate("1-abcDEF\r\n123ghi");

        Assert.Equal("1-abcDEF123ghi", e.Key);
        Assert.Equal(ApiKeyStatus.Repaired, e.Status);
        Assert.True(e.IsUsable);
    }

    [Fact]
    public void Empty_key_is_missing_not_a_crash()
    {
        var e = ApiKeyPolicy.Evaluate("");

        Assert.Equal(ApiKeyStatus.Missing, e.Status);
        Assert.False(e.IsUsable);
        Assert.Equal("", e.Key);
        Assert.Equal(ApiKeyPolicy.MissingMessage, e.Message);
    }

    [Fact]
    public void Null_key_is_missing()
    {
        var e = ApiKeyPolicy.Evaluate(null);

        Assert.Equal(ApiKeyStatus.Missing, e.Status);
        Assert.False(e.IsUsable);
    }

    [Fact]
    public void Whitespace_only_key_is_missing()
    {
        var e = ApiKeyPolicy.Evaluate("   \r\n\t  ");

        Assert.Equal(ApiKeyStatus.Missing, e.Status);
        Assert.False(e.IsUsable);
        Assert.Equal("", e.Key);
    }

    [Fact]
    public void Clean_key_is_ready_and_unchanged()
    {
        var e = ApiKeyPolicy.Evaluate("1-abcDEF123ghiJKL456");

        Assert.Equal("1-abcDEF123ghiJKL456", e.Key);
        Assert.Equal(ApiKeyStatus.Ready, e.Status);
        Assert.True(e.IsUsable);
        Assert.Equal("", e.Message);
    }

    [Fact]
    public void Key_with_a_control_character_is_unusable_and_returns_no_key()
    {
        // Bosluk olmayan ama baslik degeri olamayacak bir karakter: yarim bir deger
        // gondermek yerine anahtari REDDEDIYORUZ.
        var e = ApiKeyPolicy.Evaluate("1-abc\u0001def");

        Assert.Equal(ApiKeyStatus.Unusable, e.Status);
        Assert.False(e.IsUsable);
        Assert.Equal("", e.Key);
        Assert.Equal(ApiKeyPolicy.UnusableMessage, e.Message);
    }

    [Fact]
    public void Non_ascii_key_is_unusable()
    {
        var e = ApiKeyPolicy.Evaluate("1-abcÖZdef");

        Assert.Equal(ApiKeyStatus.Unusable, e.Status);
        Assert.False(e.IsUsable);
    }

    // Politikanin tek isi var: AuthenticationHeaderValue'nun ATMAMASINI garanti etmek.
    // Bu testler asil kaziyi (canli cokme) dogrudan yeniden uretir.
    [Theory]
    [InlineData("1-abcDEF123\n")]
    [InlineData("\r\n1-abcDEF123")]
    [InlineData("1-abc\r\nDEF123 ")]
    [InlineData("  1-abcDEF123\t")]
    public void Usable_keys_never_throw_when_put_into_an_auth_header(string raw)
    {
        var e = ApiKeyPolicy.Evaluate(raw);

        Assert.True(e.IsUsable);
        var header = new AuthenticationHeaderValue("Bearer", e.Key);   // firlarsa test duser
        Assert.Equal(e.Key, header.Parameter);
    }

    [Fact]
    public void Normalize_returns_empty_for_null()
    {
        Assert.Equal("", ApiKeyPolicy.Normalize(null));
    }

    [Fact]
    public void IsValidHeaderValue_rejects_empty_and_newlines()
    {
        Assert.False(ApiKeyPolicy.IsValidHeaderValue(""));
        Assert.False(ApiKeyPolicy.IsValidHeaderValue(null));
        Assert.False(ApiKeyPolicy.IsValidHeaderValue("abc\ndef"));
        Assert.True(ApiKeyPolicy.IsValidHeaderValue("1-abcDEF"));
    }
}
