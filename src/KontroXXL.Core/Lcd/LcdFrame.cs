namespace KontroXXL.Core.Lcd;

/// <summary>
/// Ekrana gidecek tek kare. <see cref="BarValue"/> doluysa çağıran L1 yerine B1 gönderir.
/// Her iki satır da her zaman tam 16 karakterdir.
/// </summary>
public sealed record LcdFrame(string Line0, string Line1, int? BarValue);
