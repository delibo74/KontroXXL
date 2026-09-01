using System;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using KontroXXL.Core.Logging;
using KontroXXL.Core.Serial;

namespace KontroXXL_WinApp
{
    /// <summary>
    /// Seri bağlantıyı kendi başına ayakta tutar. v2'de (A2) port bir kez açılıyordu;
    /// Arduino çıkarılınca uygulama sessizce ölüyordu. Burada 2 saniyede bir yeniden dener.
    /// </summary>
    public sealed class SerialLink : IDisposable
    {
        const int ReconnectDelayMs = 2000;
        const int ReadBufferSize = 256;

        readonly ILog _log;
        readonly Func<string> _preferredPort;   // config'ten anlık okunur
        readonly Func<int> _baud;
        readonly Func<bool> _autoDetect;
        readonly SerialLineBuffer _lineBuffer = new();
        readonly object _writeGate = new();

        CancellationTokenSource _cts;
        SerialPort _port;
        Task _loop;

        public event Action<string> LineReceived;
        public event Action Connected;

        public bool IsConnected => _port != null && _port.IsOpen;
        public string CurrentPort { get; private set; }

        public SerialLink(ILog log, Func<string> preferredPort, Func<int> baud, Func<bool> autoDetect)
        {
            _log = log ?? NullLog.Instance;
            _preferredPort = preferredPort;
            _baud = baud;
            _autoDetect = autoDetect;
        }

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _loop?.Wait(3000); } catch { }
            ClosePort();
            _loop = null;
        }

        public void Send(string msg)
        {
            lock (_writeGate)
            {
                var p = _port;
                if (p == null || !p.IsOpen) return;
                try { p.Write(msg + "\n"); }
                catch (Exception ex) { _log.Debug("Seri yazma hatasi: " + ex.Message); ClosePort(); }
            }
        }

        async Task RunAsync(CancellationToken ct)
        {
            var buffer = new byte[ReadBufferSize];

            while (!ct.IsCancellationRequested)
            {
                if (!IsConnected)
                {
                    if (!TryOpen()) { await Delay(ReconnectDelayMs, ct); continue; }
                    Connected?.Invoke();
                }

                try
                {
                    int n = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (n <= 0) { ClosePort(); continue; }

                    foreach (string line in _lineBuffer.Feed(buffer.AsSpan(0, n)))
                    {
                        try { LineReceived?.Invoke(line); }
                        catch (Exception ex) { _log.Error("Seri satir isleme hatasi", ex); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.Info("Seri baglanti koptu: " + ex.Message);
                    ClosePort();
                    await Delay(ReconnectDelayMs, ct);
                }
            }

            ClosePort();
        }

        static async Task Delay(int ms, CancellationToken ct)
        {
            try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
        }

        bool TryOpen()
        {
            string target = ResolvePort();
            if (string.IsNullOrEmpty(target)) return false;

            try
            {
                var p = new SerialPort(target, _baud()) { DtrEnable = true, RtsEnable = true };
                p.Open();
                _port = p;
                CurrentPort = target;
                _lineBuffer.Reset();
                _log.Info($"Seri port acildi: {target} @ {_baud()} baud");
                return true;
            }
            catch (Exception ex)
            {
                _log.Debug($"Seri port acilamadi ({target}): {ex.Message}");
                return false;
            }
        }

        string ResolvePort()
        {
            string preferred = _preferredPort();
            var available = SafePortNames();

            if (!_autoDetect() && !string.IsNullOrEmpty(preferred) && available.Contains(preferred))
                return preferred;

            string detected = DetectArduinoPort(available);
            if (!string.IsNullOrEmpty(detected)) return detected;

            // Otomatik algılama tutmadıysa tercih edilen porta yine de bir şans ver.
            return available.Contains(preferred) ? preferred : null;
        }

        static string[] SafePortNames()
        {
            try { return SerialPort.GetPortNames(); } catch { return Array.Empty<string>(); }
        }

        /// <summary>WMI ile Arduino/CH340/CP210x cihazının COM adını bulur.</summary>
        public static string DetectArduinoPort(string[] available)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Caption FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'"))
                foreach (var device in searcher.Get())
                {
                    string caption = device["Caption"]?.ToString();
                    if (string.IsNullOrEmpty(caption)) continue;
                    if (!(caption.Contains("Arduino") || caption.Contains("USB Serial") ||
                          caption.Contains("CH340") || caption.Contains("CP210"))) continue;

                    int start = caption.LastIndexOf("(COM", StringComparison.Ordinal) + 1;
                    int end = caption.LastIndexOf(')');
                    if (start <= 0 || end <= start) continue;

                    string name = caption.Substring(start, end - start);
                    if (available.Length == 0 || available.Contains(name)) return name;
                }
            }
            catch { }
            return null;
        }

        void ClosePort()
        {
            lock (_writeGate)
            {
                var p = _port;
                _port = null;
                if (p == null) return;
                try { p.Dispose(); } catch { }
                _lineBuffer.Reset();
            }
        }

        public void Dispose() { Stop(); _cts?.Dispose(); }
    }
}
