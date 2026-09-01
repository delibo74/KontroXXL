using System.Text;

namespace KontroXXL.Core.Logging;

/// <summary>
/// Boyut sınırına gelince gerçekten dönen dosya logu.
/// v2'deki hata (A3): eski kod .bak'a KOPYALIYOR, orijinali kesmiyordu — dosya sonsuz büyüyordu.
/// Burada döndürme sırası: app.2.log -> app.3.log, app.1.log -> app.2.log, app.log -> app.1.log, yeni app.log.
/// </summary>
public sealed class RollingFileLogger : ILog, IDisposable
{
    readonly string _path;
    readonly string _dir;
    readonly string _stem;      // "app"
    readonly string _ext;       // ".log"
    readonly long _maxBytes;
    readonly int _keep;
    readonly LogLevel _minLevel;
    readonly object _gate = new();

    StreamWriter? _writer;
    long _size;
    bool _disposed;

    public RollingFileLogger(string path, LogLevel minLevel = LogLevel.Info,
                             long maxBytes = 1_048_576, int keep = 3)
    {
        _path = path;
        _dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        _stem = Path.GetFileNameWithoutExtension(path);
        _ext = Path.GetExtension(path);
        _minLevel = minLevel;
        _maxBytes = maxBytes;
        _keep = Math.Max(1, keep);

        Directory.CreateDirectory(_dir);
        Open();
    }

    public void Debug(string msg) => Write(LogLevel.Debug, "DBG", msg);
    public void Info(string msg) => Write(LogLevel.Info, "INF", msg);

    public void Error(string msg, Exception? ex = null)
        => Write(LogLevel.Error, "ERR", ex is null ? msg : $"{msg} :: {ex}");

    void Write(LogLevel level, string tag, string msg)
    {
        if (level < _minLevel || _disposed) return;

        string line = $"[{DateTime.Now:HH:mm:ss}] [{tag}] {msg}";
        lock (_gate)
        {
            if (_writer is null)
            {
                try { Open(); } catch { return; }   // hâlâ açılamıyorsa bu satırı düşür
                if (_writer is null) return;
            }
            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
                _size += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (_size >= _maxBytes) Rotate();
            }
            catch
            {
                // Log yazamamak uygulamayı düşürmemeli.
            }
        }
    }

    void Open()
    {
        var fi = new FileInfo(_path);
        _size = fi.Exists ? fi.Length : 0;
        _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = false };
    }

    string Archive(int n) => Path.Combine(_dir, $"{_stem}.{n}{_ext}");

    void Rotate()
    {
        _writer?.Dispose();
        _writer = null;

        try
        {
            // En eskiyi at, kalanları bir kaydır.
            string oldest = Archive(_keep);
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int n = _keep - 1; n >= 1; n--)
                if (File.Exists(Archive(n)))
                    File.Move(Archive(n), Archive(n + 1), overwrite: true);

            if (File.Exists(_path))
                File.Move(_path, Archive(1), overwrite: true);
        }
        catch
        {
            // Döndürme başarısızsa loglamaya devam et; dosya büyür ama uygulama yaşar.
            // _size'ı SIFIRLAMA — Open() diskteki gerçek boyutu okuyacak ve
            // bir sonraki yazımda yeniden döndürme denenecek.
        }

        try { Open(); }
        catch { _writer = null; }   // bir sonraki Write() yeniden dener
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
