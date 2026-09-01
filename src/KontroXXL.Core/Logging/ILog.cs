namespace KontroXXL.Core.Logging;

public enum LogLevel { Debug = 0, Info = 1, Error = 2 }

public interface ILog
{
    void Debug(string msg);
    void Info(string msg);
    void Error(string msg, Exception? ex = null);
}

/// <summary>Log yazmayan uygulama — testlerde ve log açılamadığında kullanılır.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Debug(string msg) { }
    public void Info(string msg) { }
    public void Error(string msg, Exception? ex = null) { }
}
