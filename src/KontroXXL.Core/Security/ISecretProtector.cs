using System.Text;

namespace KontroXXL.Core.Security;

/// <summary>
/// A5: TrueNAS API anahtari diske duz metin yazilmaz.
/// Gercek implementasyon Windows DPAPI kullanir; Core platform-bagimsiz kalmak
/// zorunda oldugu icin burada yalnizca sozlesme ve test implementasyonu durur.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Bos/null girdi icin bos dizge dondurur — asla null.</summary>
    string Protect(string? plain);

    /// <summary>Cozulemezse null dondurur, ISTISNA FIRLATMAZ.</summary>
    string? Unprotect(string? cipher);
}

/// <summary>Sifrelemeyen implementasyon: testler ve Windows disi ortamlar icin.</summary>
public sealed class PlaintextSecretProtector : ISecretProtector
{
    public string Protect(string? plain) =>
        string.IsNullOrEmpty(plain) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
        catch { return null; }
    }
}
