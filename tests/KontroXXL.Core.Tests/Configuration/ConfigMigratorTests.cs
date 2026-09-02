using KontroXXL.Core.Configuration;
using Xunit;

namespace KontroXXL.Core.Tests.Configuration;

public class ConfigMigratorTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kx-mig-" + Guid.NewGuid().ToString("N"));

    public ConfigMigratorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string Legacy => Path.Combine(_dir, "eski", "config.json");
    string Target => Path.Combine(_dir, "yeni", "config.json");

    void WriteLegacy(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Legacy)!);
        File.WriteAllText(Legacy, content);
    }

    [Fact]
    public void Copies_the_legacy_file_when_the_target_is_absent()
    {
        WriteLegacy("{\"TruenasIp\":\"192.168.50.163\"}");

        Assert.True(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.Equal("{\"TruenasIp\":\"192.168.50.163\"}", File.ReadAllText(Target));
    }

    [Fact]
    public void Creates_the_target_directory()
    {
        WriteLegacy("{}");
        ConfigMigrator.MigrateIfNeeded(Legacy, Target);
        Assert.True(File.Exists(Target));
    }

    [Fact]
    public void Never_overwrites_an_existing_target()
    {
        WriteLegacy("ESKI");
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.WriteAllText(Target, "YENI");

        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.Equal("YENI", File.ReadAllText(Target));
    }

    [Fact]
    public void Does_nothing_when_there_is_no_legacy_file()
        => Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));

    [Fact]
    public void Is_idempotent_across_repeated_startups()
    {
        WriteLegacy("{}");
        Assert.True(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
    }

    [Fact]
    public void Leaves_the_legacy_file_in_place_as_a_fallback()
    {
        WriteLegacy("{}");
        ConfigMigrator.MigrateIfNeeded(Legacy, Target);
        Assert.True(File.Exists(Legacy), "eski dosya silinmemeli — geri donus yolu");
    }

    [Fact]
    public void Returns_false_instead_of_throwing_when_the_legacy_file_is_unreadable()
    {
        WriteLegacy("{}");
        using var hold = new FileStream(Legacy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.False(File.Exists(Target));
    }
}
