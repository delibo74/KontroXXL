using System;
using System.Security.Cryptography;
using System.Text;
using KontroXXL.Core.Security;

namespace KontroXXL_WinApp
{
    /// <summary>
    /// Windows DPAPI, CurrentUser kapsami. Sifreli metin yalnizca ayni Windows
    /// kullanici profilinde cozulebilir — dosya kopyalansa bile baska profilde ise yaramaz.
    /// </summary>
    public sealed class DpapiSecretProtector : ISecretProtector
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KontroXXL/v1");

        public string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] blob = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(blob);
            }
            catch { return ""; }
        }

        public string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return null;
            try
            {
                byte[] blob = ProtectedData.Unprotect(
                    Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(blob);
            }
            catch
            {
                // Profil/makine degisti ya da veri bozuk. Sessizce basarisiz OLMA —
                // cagiran taraf kullaniciya Ayarlar'da yeniden girmesini soyleyecek.
                return null;
            }
        }
    }
}
