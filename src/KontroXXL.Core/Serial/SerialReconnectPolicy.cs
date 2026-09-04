using System;

namespace KontroXXL.Core.Serial;

/// <summary>Bir bağlantı denemesi başarısız olduğunda ne kadar beklenecegi ve loglanip loglanmayacagi.</summary>
public sealed class SerialReconnectDecision
{
    public SerialReconnectDecision(int delayMs, bool shouldLog, string message)
    {
        DelayMs = delayMs;
        ShouldLog = shouldLog;
        Message = message;
    }

    /// <summary>Bir sonraki denemeden once beklenecek sure.</summary>
    public int DelayMs { get; }

    /// <summary>true ise bu basarisizlik loga yazilmali.</summary>
    public bool ShouldLog { get; }

    /// <summary>Loga yazilacak metin. <see cref="ShouldLog"/> false ise anlamsizdir.</summary>
    public string Message { get; }
}

/// <summary>
/// Seri baglantinin yeniden deneme ritmi: ustel geri cekilme + log kismasi.
/// </summary>
/// <remarks>
/// 2026-09-04 CANLI HATA: baglanti her ~2 saniyede bir kopup yeniden aciliyordu ve her
/// tur loga 4 satir yaziyordu — app.log'da 822 kayit. Iki ayri sorun vardi: (1) kopmanin
/// kendisi (SerialLink'te async okuma), (2) sabit 2 saniyelik yeniden deneme her
/// basarisizligi tam gurultuyle loglayarak asil olaylari gomuyordu.
///
/// Burasi (2)'nin karar katmani: saf, zamandan bagimsiz, test edilebilir. AYNI hata
/// art arda tekrarlarken yalnizca ILK kez loglanir — hata metni degisirse yeniden
/// loglanir, cunku o YENI bir olaydir. Basarili baglanti her seyi sifirlar.
/// </remarks>
public sealed class SerialReconnectPolicy
{
    private readonly int _baseDelayMs;
    private readonly int _maxDelayMs;
    private string? _lastError;

    public SerialReconnectPolicy(int baseDelayMs = 2000, int maxDelayMs = 30000)
    {
        if (baseDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(baseDelayMs));
        if (maxDelayMs < baseDelayMs) throw new ArgumentOutOfRangeException(nameof(maxDelayMs));
        _baseDelayMs = baseDelayMs;
        _maxDelayMs = maxDelayMs;
    }

    /// <summary>Basarili baglantidan bu yana ust uste kac deneme basarisiz oldu.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>Baglanti kuruldu: gecikme ve log kismasi bastan baslar.</summary>
    public void OnConnected()
    {
        ConsecutiveFailures = 0;
        _lastError = null;
    }

    /// <summary>Bir deneme basarisiz oldu; ne kadar beklenecegini ve loglanip loglanmayacagini soyler.</summary>
    public SerialReconnectDecision OnFailure(string? error)
    {
        string detail = string.IsNullOrWhiteSpace(error) ? "bilinmeyen hata" : error!.Trim();

        // Ilk basarisizlik her zaman loglanir; ayni hata tekrarlarken susuyoruz.
        bool shouldLog = ConsecutiveFailures == 0 || detail != _lastError;

        ConsecutiveFailures++;
        _lastError = detail;

        string message = shouldLog
            ? "Seri baglanti koptu: " + detail
            : "";

        return new SerialReconnectDecision(DelayFor(ConsecutiveFailures), shouldLog, message);
    }

    /// <summary>
    /// Ustel geri cekilme: 1. basarisizlikta taban gecikme, sonra her turda iki kati,
    /// tavana ulasinca sabit kalir. Tasma olmasin diye carpim tavanda kesilir.
    /// </summary>
    public int DelayFor(int consecutiveFailures)
    {
        if (consecutiveFailures <= 1) return _baseDelayMs;

        long delay = _baseDelayMs;
        for (int i = 1; i < consecutiveFailures; i++)
        {
            delay *= 2;
            if (delay >= _maxDelayMs) return _maxDelayMs;
        }
        return (int)delay;
    }
}
