namespace KontroXXL.Core.Configuration;

/// <summary>
/// v2 yapilandirmasini exe'nin yanindan %APPDATA%'ya tasir. Her acilista calisir
/// ve idempotenttir: hedef zaten varsa hicbir sey yapmaz, boylece kullanicinin
/// yeni ayarlari eski dosyayla ezilmez.
/// </summary>
public static class ConfigMigrator
{
    /// <returns>Goc GERCEKTEN yapildiysa true.</returns>
    public static bool MigrateIfNeeded(string legacyFile, string targetFile)
    {
        if (File.Exists(targetFile)) return false;
        if (!File.Exists(legacyFile)) return false;

        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(targetFile)) ?? ".";
            Directory.CreateDirectory(dir);

            // Kopyala, TASIMA — eski dosya geri donus yolu olarak kalir.
            File.Copy(legacyFile, targetFile, overwrite: false);
            return true;
        }
        catch
        {
            // Goc edilemezse uygulama varsayilanlarla acilir; cokmez.
            return false;
        }
    }
}
