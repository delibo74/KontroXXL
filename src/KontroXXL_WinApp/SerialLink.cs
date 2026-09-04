using System;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using KontroXXL.Core.Logging;
using KontroXXL.Core.Serial;

namespace KontroXXL_WinApp
{
    /// <summary>
    /// Seri bağlantıyı kendi başına ayakta tutar. v2'de (A2) port bir kez açılıyordu;
    /// Arduino çıkarılınca uygulama sessizce ölüyordu.
    /// </summary>
    /// <remarks>
    /// 2026-09-04 CANLI HATA — okuma NEDEN kendi thread'inde ve SENKRON:
    /// Eskiden okuma <c>_port.BaseStream.ReadAsync(...)</c> ile bir ThreadPool
    /// devamlilik zincirinde yapiliyordu. Windows'ta seri portun okumasi ORTAK
    /// (overlapped) G/C'dir ve isletim sistemi bekleyen bir okumayi, onu BASLATAN
    /// thread sonlandiginda iptal eder. ThreadPool thread'i geri donusturuldugu anda
    /// okuma "The I/O operation has been aborted because of either a thread exit or an
    /// application request." (ERROR_OPERATION_ABORTED) ile duserdi. Sonuc: port aciliyor,
    /// AYNI SANIYE kopuyor, 2 saniye sonra tekrar — sonsuz acil-kop dongusu
    /// (app.log'da 822 kayit) ve LCD hicbir zaman kararli veri gormuyordu.
    /// OLCUM: ayni porttan SENKRON okuyan bagimsiz bir istemci 26 saniye kesintisiz
    /// calisti; yani donanim/surucu saglamdi, hata bizim okuma modelimizdeydi.
    /// Bu yuzden okuma artik OMRU BOYUNCA yasayan tek bir adanmis thread'de, senkron
    /// yapiliyor: okumayi baslatan thread okuma bitene kadar yasiyor.
    /// </remarks>
    public sealed class SerialLink : IDisposable
    {
        const int ReconnectBaseDelayMs = 2000;
        const int ReconnectMaxDelayMs = 30000;
        const int ReadBufferSize = 256;

        // Senkron okumanin iptal bayragini ne siklikta gorecegi. Kisa tutulur ki
        // Stop() beklemede takilmasin; timeout normal bir olaydir, hata degil.
        const int ReadTimeoutMs = 500;

        readonly ILog _log;
        readonly Func<string> _preferredPort;   // config'ten anlık okunur
        readonly Func<int> _baud;
        readonly Func<bool> _autoDetect;
        readonly SerialLineBuffer _lineBuffer = new();
        readonly object _writeGate = new();
        readonly SerialReconnectPolicy _reconnect =
            new SerialReconnectPolicy(ReconnectBaseDelayMs, ReconnectMaxDelayMs);

        CancellationTokenSource _cts;
        SerialPort _port;
        Thread _loop;
        string _lastOpenError = "";

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

            // ADANMIS thread — ThreadPool DEGIL. Bekleyen seri okuma onu baslatan
            // thread'in omrune bagli (bkz. sinif aciklamasi).
            _loop = new Thread(() => Run(_cts.Token))
            {
                IsBackground = true,
                Name = "KontroXXL.SerialLink"
            };
            _loop.Start();
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }

            // Takili bir okumayi gercekte cozen sey portun dispose edilmesidir.
            // Beklemeden ONCE kapat, yoksa cikista UI ReadTimeout kadar donuyor.
            ClosePort();

            try { _loop?.Join(3000); } catch { }
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

        void Run(CancellationToken ct)
        {
            var buffer = new byte[ReadBufferSize];

            while (!ct.IsCancellationRequested)
            {
                if (!IsConnected)
                {
                    if (!TryOpen())
                    {
                        Backoff("port acilamadi (" + _lastOpenError + ")", ct);
                        continue;
                    }
                    _reconnect.OnConnected();
                    Connected?.Invoke();
                }

                try
                {
                    // SENKRON okuma, bu adanmis thread'in uzerinde. ReadTimeout dolarsa
                    // TimeoutException gelir — bu NORMALDIR, sadece iptal bayragini
                    // kontrol edip devam ederiz; baglanti kopmus sayilmaz.
                    int n = _port.Read(buffer, 0, buffer.Length);
                    if (n <= 0) continue;

                    foreach (string line in _lineBuffer.Feed(buffer.AsSpan(0, n)))
                    {
                        try { LineReceived?.Invoke(line); }
                        catch (Exception ex) { _log.Error("Seri satir isleme hatasi", ex); }
                    }
                }
                catch (TimeoutException) { /* veri yok; kopma degil */ }
                catch (Exception ex)
                {
                    // Stop() portu dispose ettiginde de buraya duseriz; o bir hata degil.
                    if (ct.IsCancellationRequested) break;

                    ClosePort();
                    Backoff(ex.Message, ct);
                }
            }

            ClosePort();
        }

        /// <summary>
        /// Basarisiz denemeyi politikaya bildirir, kararina gore loglar ve bekler.
        /// </summary>
        /// <remarks>
        /// Gecikme ve log kismasi kararlari <see cref="SerialReconnectPolicy"/> icinde,
        /// birim testleriyle. Burasi yalnizca o karari uygular: ayni hata art arda
        /// tekrarlarken loga tek satir yazilir, gecikme ustel olarak buyur.
        /// </remarks>
        void Backoff(string error, CancellationToken ct)
        {
            var decision = _reconnect.OnFailure(error);
            if (decision.ShouldLog) _log.Info(decision.Message);
            else _log.Debug("Seri yeniden deneme #" + _reconnect.ConsecutiveFailures +
                            " (" + decision.DelayMs + " ms): " + error);
            Sleep(decision.DelayMs, ct);
        }

        static void Sleep(int ms, CancellationToken ct)
        {
            try { ct.WaitHandle.WaitOne(ms); } catch { }
        }

        bool TryOpen()
        {
            string target = ResolvePort();
            if (string.IsNullOrEmpty(target)) { _lastOpenError = "uygun port bulunamadi"; return false; }

            SerialPort p = null;
            try
            {
                p = new SerialPort(target, _baud())
                {
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadTimeout = ReadTimeoutMs
                };
                p.Open();
                _port = p;
                CurrentPort = target;
                _lineBuffer.Reset();
                _lastOpenError = "";
                _log.Info($"Seri port acildi: {target} @ {_baud()} baud");
                return true;
            }
            catch (Exception ex)
            {
                // Acilamayan port nesnesi sizmasin — bu yol 2 saniyede bir tekrarlaniyor.
                try { p?.Dispose(); } catch { }
                _lastOpenError = ex.Message;
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
