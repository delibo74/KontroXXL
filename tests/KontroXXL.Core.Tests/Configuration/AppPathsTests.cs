using KontroXXL.Core.Configuration;
using Xunit;

namespace KontroXXL.Core.Tests.Configuration;

public class AppPathsTests
{
    [Fact]
    public void All_paths_live_under_the_given_root()
    {
        var p = new AppPaths(@"X:\kok");
        Assert.Equal(@"X:\kok", p.Root);
        Assert.StartsWith(@"X:\kok", p.ConfigFile);
        Assert.StartsWith(@"X:\kok", p.LogFile);
        Assert.StartsWith(@"X:\kok", p.CrashLog);
    }

    [Fact]
    public void Config_and_logs_have_the_expected_names()
    {
        var p = new AppPaths(@"X:\kok");
        Assert.Equal(@"X:\kok\config.json", p.ConfigFile);
        Assert.Equal(@"X:\kok\logs", p.LogDir);
        Assert.Equal(@"X:\kok\logs\app.log", p.LogFile);
        Assert.Equal(@"X:\kok\crash.log", p.CrashLog);
    }

    [Fact]
    public void ForCurrentUser_points_at_roaming_appdata_not_the_install_dir()
    {
        var p = AppPaths.ForCurrentUser();
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(roaming, p.Root);
        Assert.EndsWith("KontroXXL", p.Root);

        // Velopack kurulum kokunu %LOCALAPPDATA%\KontroXXL yapiyor; carpismamali.
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.False(p.Root.StartsWith(local, StringComparison.OrdinalIgnoreCase),
            $"config koku Velopack kurulum koku ile ayni dalda: {p.Root}");
    }
}
