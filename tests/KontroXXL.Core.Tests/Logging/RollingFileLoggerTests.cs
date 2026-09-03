using KontroXXL.Core.Logging;
using Xunit;

namespace KontroXXL.Core.Tests.Logging;

public class RollingFileLoggerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kx-log-" + Guid.NewGuid().ToString("N"));

    public RollingFileLoggerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string Path0 => Path.Combine(_dir, "app.log");

    [Fact]
    public void Info_writes_a_timestamped_line()
    {
        using (var log = new RollingFileLogger(Path0))
            log.Info("merhaba");

        string content = File.ReadAllText(Path0);
        Assert.Contains("merhaba", content);
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] \[INF\] merhaba", content);
    }

    [Fact]
    public void Debug_is_suppressed_below_min_level()
    {
        using (var log = new RollingFileLogger(Path0, LogLevel.Info))
        {
            log.Debug("gorunmemeli");
            log.Info("gorunmeli");
        }

        string content = File.ReadAllText(Path0);
        Assert.DoesNotContain("gorunmemeli", content);
        Assert.Contains("gorunmeli", content);
    }

    [Fact]
    public void Rotation_moves_current_to_app1_and_truncates_current()
    {
        using (var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 200, keep: 3))
            for (int i = 0; i < 40; i++)
                log.Info(new string('x', 40));

        Assert.True(File.Exists(Path0));
        Assert.True(File.Exists(Path.Combine(_dir, "app.1.log")));
        // Kritik: mevcut dosya kesilmiş olmalı — eski kod bunu yapmıyordu (A3).
        Assert.True(new FileInfo(Path0).Length <= 400,
            $"app.log dondurulmedi, boyut: {new FileInfo(Path0).Length}");
    }

    [Fact]
    public void Rotation_keeps_at_most_the_configured_number_of_archives()
    {
        using (var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 100, keep: 2))
            for (int i = 0; i < 200; i++)
                log.Info(new string('y', 40));

        Assert.True(File.Exists(Path.Combine(_dir, "app.1.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "app.2.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "app.3.log")));
    }

    [Fact]
    public void Creates_missing_directory()
    {
        string nested = Path.Combine(_dir, "a", "b", "app.log");
        using (var log = new RollingFileLogger(nested))
            log.Info("olustu");
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Concurrent_writes_do_not_throw_and_lose_nothing()
    {
        using (var log = new RollingFileLogger(Path0))
            Parallel.For(0, 200, i => log.Info("satir" + i));

        Assert.Equal(200, File.ReadAllLines(Path0).Length);
    }

    [Fact]
    public void Error_appends_exception_text()
    {
        using (var log = new RollingFileLogger(Path0))
            log.Error("patladi", new InvalidOperationException("sebep"));

        string content = File.ReadAllText(Path0);
        Assert.Contains("patladi", content);
        Assert.Contains("sebep", content);
    }

    [Fact]
    public void Recovers_when_the_post_rotation_reopen_throws()
    {
        int calls = 0;
        StreamWriter Factory(string p)
        {
            calls++;
            // 1. cagri: ctor. 2. cagri: rotasyon sonrasi reopen -> PATLAT.
            // 3. cagri: Write'in retry'i -> basarili olmali.
            if (calls == 2) throw new IOException("simule edilmis kilit");
            return new StreamWriter(p, append: true, System.Text.Encoding.UTF8) { AutoFlush = false };
        }

        using (var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 100, keep: 2, writerFactory: Factory))
        {
            for (int i = 0; i < 10; i++) log.Info(new string('a', 40));   // rotasyonu tetikle
            log.Info("reopen-hatasindan-sonra");
        }

        Assert.True(calls >= 3, $"reopen hic denenmemis, factory cagri sayisi: {calls}");
        string all = string.Join("\n", Directory.GetFiles(_dir).Select(File.ReadAllText));
        Assert.Contains("reopen-hatasindan-sonra", all);
    }

    [Fact]
    public void Retries_rotation_immediately_after_a_failed_one_instead_of_deferring()
    {
        // app.1.log'u paylaşımsız aç -> Rotate() içindeki File.Move ona çarpıp patlar.
        string blocker = Path.Combine(_dir, "app.1.log");
        File.WriteAllText(blocker, "engel");
        var hold = new FileStream(blocker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 200, keep: 2);

        // Rotasyon defalarca denenir ve her seferinde başarısız olur; app.log büyür.
        for (int i = 0; i < 30; i++) log.Info(new string('b', 40));
        Assert.True(new FileInfo(Path0).Length > 200,
            "test kurulumu hatali: rotasyon engellenmemis");

        // Engeli kaldır ve TEK bir satır yaz.
        // Hata geri gelseydi _size sifirlanmis olurdu, bu tek satir maxBytes'in
        // cok altinda kalirdi ve rotasyon tetiklenmezdi.
        // Duzeltmeyle _size gercek (buyuk) boyutta kaldigi icin rotasyon hemen tetiklenir.
        hold.Dispose();
        log.Info("tetikleyici");

        Assert.True(new FileInfo(Path0).Length < 200,
            $"rotasyon yeniden denenmedi, app.log hala {new FileInfo(Path0).Length} bayt");
    }
}
