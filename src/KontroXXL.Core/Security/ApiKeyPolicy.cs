namespace KontroXXL.Core.Security;

/// <summary>TrueNAS API anahtarinin HTTP basligina konulabilecek durumda olup olmadigi.</summary>
public enum ApiKeyStatus
{
    /// <summary>Anahtar hic girilmemis (ya da yalnizca bosluktan ibaret).</summary>
    Missing,

    /// <summary>Anahtar oldugu gibi kullanilabilir.</summary>
    Ready,

    /// <summary>Anahtar kullanilabilir ama once temizlendi (yapistirmadan gelen satir sonu/bosluk).</summary>
    Repaired,

    /// <summary>Temizlikten sonra bile baslik degeri olamayacak karakterler kaldi.</summary>
    Unusable
}

/// <summary>Bir anahtar adayinin degerlendirilmis hali.</summary>
public sealed class ApiKeyEvaluation
{
    public ApiKeyEvaluation(string key, ApiKeyStatus status, string message)
    {
        Key = key;
        Status = status;
        Message = message;
    }

    /// <summary>Normalize edilmis anahtar. <see cref="IsUsable"/> false ise BOSTUR.</summary>
    public string Key { get; }

    public ApiKeyStatus Status { get; }

    /// <summary>true ise <see cref="Key"/> dogrudan Authorization basligina konabilir.</summary>
    public bool IsUsable => Status == ApiKeyStatus.Ready || Status == ApiKeyStatus.Repaired;

    /// <summary>Kullaniciya/loga gosterilecek metin. <see cref="ApiKeyStatus.Ready"/> icin bostur.</summary>
    public string Message { get; }
}

/// <summary>
/// TrueNAS API anahtarini HTTP <c>Authorization</c> basligina koymadan once normalize eder
/// ve kullanilabilirligini soyler.
/// </summary>
/// <remarks>
/// 2026-09-04 CANLI HATA: kullanicinin yapistirdigi anahtarin icinde satir sonu vardi.
/// <c>AuthenticationHeaderValue</c> baslik degerinde CR/LF kabul etmez ve
/// "New-line characters are not allowed in header values" firlatir; bu istisna
/// <c>TrayApplicationContext</c> kurucusunun icinde atildigi icin uygulama HIC ayaga
/// kalkmiyordu — tepsi ikonu yok, LCD yok, Ayarlar'a ulasip anahtari duzeltmek bile
/// mumkun degildi. Bu yuzden karar burada, test edilebilir ve UI'dan bagimsiz verilir:
/// <b>hicbir anahtar degeri uygulamayi dusurmemeli.</b>
///
/// Normalizasyon TUM bosluk karakterlerini atar (yalnizca bastan/sondan degil): TrueNAS
/// anahtari <c>1-&lt;base64&gt;</c> bicimindedir ve icinde bosluk BULUNMAZ, dolayisiyla
/// gorulen her bosluk yapistirma artigidir — satira bolunmus bir anahtari birlestirmek
/// dogru onarimdir. Geriye baslik degeri olamayacak bir karakter kalirsa anahtar
/// kullanilamaz sayilir; sessizce yarim bir deger gondermeyiz.
/// </remarks>
public static class ApiKeyPolicy
{
    public const string MissingMessage =
        "TrueNAS API anahtari girilmemis — NAS modulu devre disi. Ayarlar'dan anahtari girin.";

    public const string RepairedMessage =
        "API anahtarindaki satir sonu/bosluk temizlendi.";

    public const string UnusableMessage =
        "TrueNAS API anahtari gecersiz karakter iceriyor — NAS modulu devre disi. " +
        "Ayarlar'dan anahtari yeniden girin.";

    /// <summary>
    /// Anahtar adayindan tum bosluk karakterlerini atar. null girdi bos dize dondurur.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        var buffer = new System.Text.StringBuilder(raw!.Length);
        foreach (char c in raw)
        {
            if (char.IsWhiteSpace(c)) continue;
            buffer.Append(c);
        }
        return buffer.ToString();
    }

    /// <summary>
    /// Verilen dizenin HTTP baslik degeri olarak gonderilebilecegini soyler:
    /// yalnizca yazdirilabilir ASCII (0x21-0x7E). Bos dize gecerli SAYILMAZ.
    /// </summary>
    public static bool IsValidHeaderValue(string? normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return false;
        foreach (char c in normalized!)
        {
            if (c < '\u0021' || c > '\u007E') return false;
        }
        return true;
    }

    /// <summary>Ham (kullanicidan ya da config'ten gelen) anahtari degerlendirir.</summary>
    public static ApiKeyEvaluation Evaluate(string? raw)
    {
        string normalized = Normalize(raw);

        if (normalized.Length == 0)
            return new ApiKeyEvaluation("", ApiKeyStatus.Missing, MissingMessage);

        if (!IsValidHeaderValue(normalized))
            return new ApiKeyEvaluation("", ApiKeyStatus.Unusable, UnusableMessage);

        return normalized == raw
            ? new ApiKeyEvaluation(normalized, ApiKeyStatus.Ready, "")
            : new ApiKeyEvaluation(normalized, ApiKeyStatus.Repaired, RepairedMessage);
    }
}
