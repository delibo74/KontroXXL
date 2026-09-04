using System;
using System.Collections.Generic;
using System.Linq;

namespace KontroXXL.Core.Diagnostics;

/// <summary>
/// TrueNAS'in <c>level</c> alanindaki onem dereceleri, dusukten yuksege.
/// <see cref="NasAlertLevel.Unknown"/> okunamayan/bos bir seviyeyi temsil eder.
/// </summary>
public enum NasAlertLevel
{
    Info,
    Notice,
    Warning,
    Error,
    Critical,
    Alert,
    Emergency,

    /// <summary>
    /// Seviye okunamadi. Sirasi bilinmedigi icin ayri bir deger; siralamasi
    /// <see cref="AlertNotificationPolicy.SeverityRank"/> icinde WARNING'e esitlenir —
    /// bkz. orada ki gerekce.
    /// </summary>
    Unknown,
}

/// <summary>Bildirim karari icin gereken en az alan: kimlik, seviye, baslik.</summary>
public sealed class NasAlert
{
    public NasAlert(string? id, string? level, string? title)
    {
        Id = id;
        Level = level;
        Title = title;
    }

    /// <summary>TrueNAS alarm kimligi. Bos olabilir — politika o durumu kendi ele alir.</summary>
    public string? Id { get; }

    /// <summary>Ham <c>level</c> metni ("CRITICAL", "warning", null...).</summary>
    public string? Level { get; }

    /// <summary>Kullaniciya gosterilecek metin (TrueNAS'ta <c>formatted</c>).</summary>
    public string? Title { get; }
}

/// <summary>Politikanin ayarlanabilir tarafi.</summary>
public sealed class AlertNotificationOptions
{
    /// <summary>Bu seviyenin ALTINDAKI alarmlar bildirim uretmez. Varsayilan: WARNING.</summary>
    public NasAlertLevel MinimumLevel { get; set; } = NasAlertLevel.Warning;

    /// <summary>
    /// Iki balon arasindaki en kisa sure. Bu pencere icinde dogan alarmlar
    /// KAYBEDILMEZ, biriktirilir ve pencere dolunca TEK balonda cikar.
    /// </summary>
    public TimeSpan Throttle { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Balon govdesinde en fazla kac alarm basligi listelenir.</summary>
    public int MaxBodyLines { get; set; } = 3;
}

/// <summary>
/// Politikanin turlar arasi tasidigi durum. Degismezdir: her karar YENI bir durum uretir,
/// cagiran taraf onu saklar. Boylece testte durum elle kurulabilir.
/// </summary>
public sealed class AlertNotificationState
{
    /// <summary>Hicbir sey okunmamis baslangic durumu.</summary>
    public static readonly AlertNotificationState Initial =
        new AlertNotificationState(Array.Empty<string>(), primed: false, lastNotifiedAt: null);

    public AlertNotificationState(
        IEnumerable<string> notifiedIds, bool primed, DateTimeOffset? lastNotifiedAt)
    {
        NotifiedIds = new HashSet<string>(
            notifiedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        Primed = primed;
        LastNotifiedAt = lastNotifiedAt;
    }

    /// <summary>Halen ACIK olan ve daha once bildirilmis alarmlarin kimlikleri.</summary>
    public IReadOnlySet<string> NotifiedIds { get; }

    /// <summary>
    /// Ilk okuma yapildi mi. false iken hicbir bildirim uretilmez — acilista
    /// zaten var olan alarmlar "yeni" degildir.
    /// </summary>
    public bool Primed { get; }

    /// <summary>Son balonun zamani; kismanin (throttle) dayanagi.</summary>
    public DateTimeOffset? LastNotifiedAt { get; }
}

/// <summary>Bir tick'in sonucu.</summary>
public sealed class AlertNotificationDecision
{
    public AlertNotificationDecision(
        bool shouldNotify, string title, string body, int newAlertCount,
        AlertNotificationState nextState)
    {
        ShouldNotify = shouldNotify;
        Title = title;
        Body = body;
        NewAlertCount = newAlertCount;
        NextState = nextState;
    }

    /// <summary>true ise balon gosterilmeli.</summary>
    public bool ShouldNotify { get; }

    /// <summary>Balon basligi. <see cref="ShouldNotify"/> false iken bos.</summary>
    public string Title { get; }

    /// <summary>Balon govdesi. <see cref="ShouldNotify"/> false iken bos.</summary>
    public string Body { get; }

    /// <summary>Bu balonun kapsadigi yeni alarm sayisi.</summary>
    public int NewAlertCount { get; }

    /// <summary>Cagiranin bir sonraki tick'e tasimasi gereken durum.</summary>
    public AlertNotificationState NextState { get; }
}

/// <summary>
/// "NAS'ta yeni bir kritik alarm var" kararini veren saf mantik.
/// </summary>
/// <remarks>
/// Faz 4 oncesi tetik (<c>TrayApplicationContext</c>, alarm sayisi karsilastirmasi)
/// uc yerden bozuktu ve bu sinif ucunu de kapatir:
/// <list type="number">
/// <item>SAYI tabanliydi: bir alarm kapanip ayni tick'te baskasi acilirsa toplam
/// degismez, yeni alarm sessizce kacardi. Burada karar KIMLIK kumesi farkiyla verilir.</item>
/// <item><c>level</c> hic okunmuyordu, "kritik alarm" gercekte "herhangi bir alarm"
/// demekti. Burada <see cref="AlertNotificationOptions.MinimumLevel"/> esigi var.</item>
/// <item>Sayac her acilista 0'dan basliyordu, mevcut alarmlar "yeni" sayiliyordu.
/// Burada ilk tick yalnizca TABAN alir (<see cref="AlertNotificationState.Primed"/>).</item>
/// </list>
/// Ayrica kisma (throttle) var: daha once SKT bildiriminde damga gonderimden SONRA
/// basildigi icin "dakikada bir bildirim" hatasi yasanmisti. Buradaki kisma alarmlari
/// DUSURMEZ — sadece geciktirir; pencere dolunca hepsi tek balonda cikar, cunku
/// kaybolan bir kritik alarm gurultuden daha kotudur (spec bolum 9).
/// </remarks>
public static class AlertNotificationPolicy
{
    /// <summary>Baslik metni okunamayan alarm icin kullanilan metin.</summary>
    public const string UnnamedAlert = "(başlıksız uyarı)";

    /// <summary>Balon metinlerinin sert ust siniri — Shell balonu uzun metni keser.</summary>
    public const int MaxBodyLength = 240;

    /// <summary>
    /// Ham <c>level</c> metnini enum'a cevirir. Tanimadigi her sey
    /// <see cref="NasAlertLevel.Unknown"/> olur (istisna atmaz).
    /// </summary>
    public static NasAlertLevel ParseLevel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return NasAlertLevel.Unknown;

        return raw.Trim().ToUpperInvariant() switch
        {
            "INFO" => NasAlertLevel.Info,
            "NOTICE" => NasAlertLevel.Notice,
            "WARNING" => NasAlertLevel.Warning,
            "ERROR" => NasAlertLevel.Error,
            "CRITICAL" => NasAlertLevel.Critical,
            "ALERT" => NasAlertLevel.Alert,
            "EMERGENCY" => NasAlertLevel.Emergency,
            _ => NasAlertLevel.Unknown,
        };
    }

    /// <summary>
    /// Karsilastirilabilir onem sirasi. <see cref="NasAlertLevel.Unknown"/> bilerek
    /// WARNING ile ayni siraya konur: seviyesi okunamayan bir alarmi INFO sayip
    /// dusurmek, TrueNAS bir gun yeni bir seviye adi eklediginde kritik bir alarmi
    /// SESSIZCE yutar. Fazladan bir balon, kacirilan bir alarmdan ucuzdur.
    /// </summary>
    public static int SeverityRank(NasAlertLevel level) => level switch
    {
        NasAlertLevel.Info => 0,
        NasAlertLevel.Notice => 1,
        NasAlertLevel.Warning => 2,
        NasAlertLevel.Unknown => 2,
        NasAlertLevel.Error => 3,
        NasAlertLevel.Critical => 4,
        NasAlertLevel.Alert => 5,
        NasAlertLevel.Emergency => 6,
        _ => 2,
    };

    /// <summary>
    /// Bir tick'in kararini verir. <paramref name="current"/> o an ACIK (dismissed
    /// olmayan) alarmlarin tamamidir; null ya da bos gecilebilir.
    /// </summary>
    public static AlertNotificationDecision Decide(
        AlertNotificationState? state,
        IReadOnlyList<NasAlert?>? current,
        AlertNotificationOptions? options,
        DateTimeOffset now)
    {
        state ??= AlertNotificationState.Initial;
        options ??= new AlertNotificationOptions();

        int threshold = SeverityRank(options.MinimumLevel);

        // Esigi gecen, kimligi cikarilabilen alarmlar. Kimlik SIRASI korunur ki
        // balon govdesi TrueNAS'in verdigi sirayla okunsun.
        var eligible = new List<(string Id, string Text)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alert in current ?? Array.Empty<NasAlert?>())
        {
            if (alert is null) continue;
            if (SeverityRank(ParseLevel(alert.Level)) < threshold) continue;

            string? id = TrackingId(alert);
            if (id is null) continue;              // ne kimlik ne baslik — takip edilemez
            if (!seen.Add(id)) continue;           // ayni alarm listede iki kez

            eligible.Add((id, DisplayText(alert)));
        }

        var eligibleIds = eligible.Select(e => e.Id).ToArray();

        // 1) Ilk okuma: yalnizca taban alinir, hicbir sey bildirilmez.
        if (!state.Primed)
        {
            return Silent(new AlertNotificationState(
                eligibleIds, primed: true, lastNotifiedAt: state.LastNotifiedAt));
        }

        // Kapanan alarmlar kumeden DUSER: ayni alarm yeniden acilirsa yeniden bildirilir.
        var stillOpen = state.NotifiedIds.Where(id => seen.Contains(id));
        var carried = new HashSet<string>(stillOpen, StringComparer.Ordinal);

        var fresh = eligible.Where(e => !carried.Contains(e.Id)).ToList();

        // 2) Yeni bir sey yok.
        if (fresh.Count == 0)
        {
            return Silent(new AlertNotificationState(carried, primed: true, state.LastNotifiedAt));
        }

        // 3) Kisma penceresi: bildirme, ama BIRIKTIR — kumeye eklemiyoruz, bu yuzden
        //    pencere dolunca ayni alarmlar tek balonda cikacak.
        if (state.LastNotifiedAt is DateTimeOffset last
            && options.Throttle > TimeSpan.Zero
            && now - last < options.Throttle)
        {
            return Silent(new AlertNotificationState(carried, primed: true, state.LastNotifiedAt));
        }

        foreach (var f in fresh) carried.Add(f.Id);

        return new AlertNotificationDecision(
            shouldNotify: true,
            title: BuildTitle(fresh.Count),
            body: BuildBody(fresh.Select(f => f.Text).ToList(), options.MaxBodyLines),
            newAlertCount: fresh.Count,
            nextState: new AlertNotificationState(carried, primed: true, lastNotifiedAt: now));
    }

    private static AlertNotificationDecision Silent(AlertNotificationState next) =>
        new AlertNotificationDecision(false, string.Empty, string.Empty, 0, next);

    /// <summary>
    /// Takip anahtari. Kimlik yoksa basliktan turetilir — kimliksiz bir alarmi tumden
    /// atlamak onu gorunmez yapardi, her tick bildirmek ise spam olurdu.
    /// </summary>
    private static string? TrackingId(NasAlert alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.Id)) return alert.Id!.Trim();
        if (!string.IsNullOrWhiteSpace(alert.Title)) return "text:" + alert.Title!.Trim();
        return null;
    }

    private static string DisplayText(NasAlert alert) =>
        string.IsNullOrWhiteSpace(alert.Title) ? UnnamedAlert : alert.Title!.Trim();

    private static string BuildTitle(int count) =>
        count == 1 ? "NAS: yeni uyarı" : $"NAS: {count} yeni uyarı";

    private static string BuildBody(IReadOnlyList<string> texts, int maxLines)
    {
        int lines = Math.Max(1, maxLines);
        var shown = texts.Take(lines).ToList();
        var body = string.Join(Environment.NewLine, shown);

        if (texts.Count > shown.Count)
            body += Environment.NewLine + $"… ve {texts.Count - shown.Count} tane daha";

        return body.Length > MaxBodyLength
            ? body.Substring(0, MaxBodyLength - 1).TrimEnd() + "…"
            : body;
    }
}
