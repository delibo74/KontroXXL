using KontroXXL.Core.Lcd;
using Xunit;

namespace KontroXXL.Core.Tests.Lcd;

public class LcdTextTests
{
    [Theory]
    [InlineData("Müzik Çalar", "Muzik Calar")]
    [InlineData("İŞĞÜÖÇ", "ISGUOC")]
    [InlineData("ışğüöç", "isguoc")]
    [InlineData("CİFS, SSH", "CIFS, SSH")]
    [InlineData("Obsidian", "Obsidian")]
    public void Sanitize_transliterates_turkish(string input, string expected)
        => Assert.Equal(expected, LcdText.Sanitize(input));

    [Fact]
    public void Sanitize_preserves_custom_arrow_bytes()
        => Assert.Equal("\x01 12Mb \x02 34Mb", LcdText.Sanitize("\x01 12Mb \x02 34Mb"));

    [Fact]
    public void Sanitize_replaces_other_non_ascii_with_question_mark()
        => Assert.Equal("a?b", LcdText.Sanitize("a€b")); // euro sign

    [Fact]
    public void Sanitize_never_changes_length()
        => Assert.Equal(11, LcdText.Sanitize("Müzik Çalar").Length);

    [Fact]
    public void Sanitize_of_null_is_empty()
        => Assert.Equal("", LcdText.Sanitize(null));

    [Fact]
    public void Fit_pads_short_text_to_16()
        => Assert.Equal("CPU:76%         ", LcdText.Fit("CPU:76%"));

    [Fact]
    public void Fit_truncates_long_text_to_16()
        => Assert.Equal("0123456789ABCDEF", LcdText.Fit("0123456789ABCDEF_TASMA"));

    [Fact]
    public void Fit_always_returns_exactly_16()
    {
        Assert.Equal(16, LcdText.Fit("").Length);
        Assert.Equal(16, LcdText.Fit(null).Length);
        Assert.Equal(16, LcdText.Fit("kısa").Length);
        Assert.Equal(16, LcdText.Fit(new string('x', 100)).Length);
    }

    [Fact]
    public void Scroll_returns_text_unchanged_when_it_fits()
        => Assert.Equal("kisa            ", LcdText.Scroll("kısa", offset: 7));

    [Fact]
    public void Scroll_shifts_long_text_by_offset()
    {
        // "ABCDEFGHIJKLMNOPQRS" (19) -> kaydırma penceresi "  " ile birleştirilmiş metin üzerinde
        var s = LcdText.Scroll("ABCDEFGHIJKLMNOPQRS", offset: 3);
        Assert.Equal(16, s.Length);
        Assert.Equal("DEFGHIJKLMNOPQRS", s);
    }

    [Fact]
    public void Scroll_wraps_around_without_throwing()
    {
        for (int i = 0; i < 200; i++)
            Assert.Equal(16, LcdText.Scroll("ABCDEFGHIJKLMNOPQRS", i).Length);
    }
}
