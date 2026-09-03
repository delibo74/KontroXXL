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

        string dir = Path.GetDirectoryName(Path.GetFullPath(targetFile)) ?? ".";
        string tmp = targetFile + ".migrating";

        try
        {
            Directory.CreateDirectory(dir);

            // Yarim kopya birakmamak icin once .tmp'ye, sonra tek adimda yerine.
            // JsonFileStore.WriteAtomic ile ayni konvansiyon.
            File.Copy(legacyFile, tmp, overwrite: true);
            File.Move(tmp, targetFile, overwrite: false);
            return true;
        }
        catch
        {
            // Goc edilemezse uygulama varsayilanlarla acilir; cokmez.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }
}
