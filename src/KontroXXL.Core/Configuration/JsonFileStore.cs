using System.Text;

namespace KontroXXL.Core.Configuration;

/// <summary>
/// Yapılandırma dosyası yazımı. Doğrudan üzerine yazmak, yazma sırasındaki bir
/// çökmede config.json'ı yarım bırakır ve uygulama bir daha açılmaz.
/// Önce .tmp'ye yazılır, sonra tek adımda yerine taşınır.
/// </summary>
public static class JsonFileStore
{
    public static void WriteAtomic(string path, string content)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(dir);

        string tmp = full + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(full)) File.Replace(tmp, full, destinationBackupFileName: null);
        else File.Move(tmp, full);
    }

    public static string? ReadOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}
