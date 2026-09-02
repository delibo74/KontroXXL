namespace KontroXXL.Core.Configuration;

/// <summary>
/// Yazilabilir her seyin yolu buradan gecer.
/// A6: v2 bunlari exe'nin yanina yaziyordu. Velopack kurulum klasorunu (`current/`)
/// her guncellemede DEGISTIRDIGI icin oraya yazilan her sey guncellemede kaybolur;
/// Program Files'a kurulunca da yazma izni yoktur.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "KontroXXL";

    public string Root { get; }

    public AppPaths(string root) => Root = root;

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string LogDir => Path.Combine(Root, "logs");
    public string LogFile => Path.Combine(LogDir, "app.log");
    public string CrashLog => Path.Combine(Root, "crash.log");

    /// <summary>
    /// Roaming AppData altinda. Velopack kurulumu %LOCALAPPDATA%\KontroXXL kullaniyor —
    /// kasten farkli dal, kaldirma sirasinda kullanici verisi silinmesin diye.
    /// </summary>
    public static AppPaths ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));
}
