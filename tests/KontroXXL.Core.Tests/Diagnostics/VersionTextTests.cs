using System;
using KontroXXL.Core.Diagnostics;
using Xunit;

namespace KontroXXL.Core.Tests.Diagnostics;

// Spec §8.9: surum numarasi TEK olmali — Directory.Build.props, kurulum paketi ve
// Ayarlar'daki "Hakkinda" ayni degeri gostermeli. pack.ps1 paketi
// Directory.Build.props'taki <Version> ile damgaliyor ("2.2.0"), oysa
// Assembly.GetName().Version her zaman 4 parcali ("2.2.0.0"). Bu tipin isi,
// gosterilecek metni paket damgasiyla ayni yazima indirgemek.
public class VersionTextTests
{
    [Fact]
    public void Prefers_informational_version_because_that_is_what_the_package_is_stamped_with()
        => Assert.Equal("2.2.0", VersionText.Describe("2.2.0", new Version(2, 2, 0, 0)));

    [Fact]
    public void Strips_source_link_build_metadata_after_plus()
        => Assert.Equal("2.2.0", VersionText.Describe("2.2.0+ea4e77801a", new Version(2, 2, 0, 0)));

    [Fact]
    public void Keeps_a_prerelease_suffix_because_that_is_part_of_the_package_version()
        => Assert.Equal("2.2.0-beta.1", VersionText.Describe("2.2.0-beta.1+abc", new Version(2, 2, 0, 0)));

    [Fact]
    public void Falls_back_to_the_assembly_version_trimmed_to_three_parts()
        => Assert.Equal("2.2.0", VersionText.Describe(null, new Version(2, 2, 0, 0)));

    [Fact]
    public void Keeps_the_fourth_part_when_it_carries_information()
        => Assert.Equal("2.2.0.7", VersionText.Describe("", new Version(2, 2, 0, 7)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+sadece-metadata")]
    public void Blank_informational_values_fall_through_instead_of_showing_an_empty_version(string? informational)
        => Assert.Equal("1.0.0", VersionText.Describe(informational, new Version(1, 0, 0, 0)));

    [Fact]
    public void Says_it_does_not_know_rather_than_returning_empty_when_nothing_is_available()
        => Assert.Equal("bilinmiyor", VersionText.Describe(null, null));

    [Fact]
    public void Trims_surrounding_whitespace()
        => Assert.Equal("2.2.0", VersionText.Describe("  2.2.0  ", null));

    [Fact]
    public void Two_part_assembly_versions_still_render_three_parts()
        => Assert.Equal("2.2.0", VersionText.Describe(null, new Version(2, 2)));
}
