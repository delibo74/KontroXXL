namespace KontroXXL.Core.Diagnostics;

/// <summary>
/// Bir güncelleme denemesi başarısız olduğunda kullanıcıya ne söyleneceği ve
/// uygulamanın yaşamaya devam edip edemeyeceği.
/// </summary>
public sealed class UpdateFailureResponse
{
    public UpdateFailureResponse(string message, bool mustExit)
    {
        Message = message;
        MustExit = mustExit;
    }

    /// <summary>Kullanıcıya gösterilecek tam metin — hiçbir zaman boş değildir.</summary>
    public string Message { get; }

    /// <summary>
    /// true ise süreç YAŞAYAMAZ: çıkış yolu zaten işletilmiştir (seri port kapalı,
    /// LCD veda yazdı, tepsi ikonu gizlendi) ve geri alınamaz.
    /// </summary>
    public bool MustExit { get; }
}

/// <summary>
/// "Güncellemeleri denetle" akışının başarısızlık davranışı.
/// </summary>
/// <remarks>
/// Spec §9 sessiz başarısızlığı yasaklıyor, ama sessiz-BOZUK durum daha kötüsü:
/// <c>ApplyUpdatesAndRestart</c> çağrısı (kilitli dosya, virüs tarayıcı, izin) fırlarsa
/// yıkım ADIMLARI ZATEN YAPILMIŞ olur. O noktada bir uyarı kutusu gösterip devam etmek
/// tepsisi görünmeyen, seri portu kapalı, LCD'sinde "BYE BYE" yazan, ama timer'ları
/// tikleyen bir hayalet süreç bırakır — kullanıcının menüye ulaşıp kapatması bile
/// mümkün değildir. Bu yüzden yıkım sonrası tek doğru davranış: söyle ve kapan.
/// </remarks>
public static class UpdateFailurePolicy
{
    /// <summary>Özel durum mesajı okunamadığında kullanılacak metin.</summary>
    public const string UnknownError = "Bilinmeyen hata";

    /// <summary>Yıkımdan ÖNCEKİ hatalar: uygulama sağlam, yalnızca haber verilir.</summary>
    public const string RecoverableHeader = "Güncelleme denetlenemedi:";

    /// <summary>Yıkımdan SONRAKİ hatalar: uygulama onarılamaz, kapanacağı söylenir.</summary>
    public const string FatalHeader =
        "Güncelleme uygulanamadı ve uygulama çalışır durumda bırakılamadı; kapatılıyor.";

    /// <summary>
    /// <paramref name="tornDown"/> çıkış yolunun (config yazımı, LCD vedası, seri port
    /// bırakma, tepsi ikonunun gizlenmesi) işletilmiş olup olmadığını söyler.
    /// </summary>
    public static UpdateFailureResponse Describe(bool tornDown, string? error)
    {
        var detail = string.IsNullOrWhiteSpace(error) ? UnknownError : error!.Trim();
        return tornDown
            ? new UpdateFailureResponse(FatalHeader + "\n\n" + detail, mustExit: true)
            : new UpdateFailureResponse(RecoverableHeader + "\n\n" + detail, mustExit: false);
    }
}
