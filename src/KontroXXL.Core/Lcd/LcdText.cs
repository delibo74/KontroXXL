using System.Text;

namespace KontroXXL.Core.Lcd;

/// <summary>
/// HD44780 LCD 16x2 ekranına giden her metin buradan geçer.
/// Ekran yalnızca ASCII 0x20-0x7E ile iki özel karakteri (0x01 RX oku, 0x02 TX oku) çizebilir.
/// </summary>
public static class LcdText
{
    public const int Width = 16;
    public const char RxArrow = '\x01';
    public const char TxArrow = '\x02';

    // Türkçe harfler için birebir karşılıklar. Sıra önemli değil, uzunluk 1:1 korunur.
    static readonly Dictionary<char, char> Translit = new()
    {
        ['ı'] = 'i', ['İ'] = 'I',
        ['ğ'] = 'g', ['Ğ'] = 'G',
        ['ü'] = 'u', ['Ü'] = 'U',
        ['ş'] = 's', ['Ş'] = 'S',
        ['ö'] = 'o', ['Ö'] = 'O',
        ['ç'] = 'c', ['Ç'] = 'C',
    };

    /// <summary>Uzunluğu değiştirmeden ekranın çizebileceği karakterlere indirger.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == RxArrow || c == TxArrow) { sb.Append(c); continue; }
            if (Translit.TryGetValue(c, out char mapped)) { sb.Append(mapped); continue; }
            sb.Append(c >= 0x20 && c <= 0x7E ? c : '?');
        }
        return sb.ToString();
    }

    /// <summary>Sanitize eder ve tam olarak <paramref name="width"/> karakter döndürür.</summary>
    public static string Fit(string? text, int width = Width)
    {
        string s = Sanitize(text);
        return s.Length >= width ? s[..width] : s.PadRight(width);
    }

    /// <summary>
    /// Ekrana sığmayan metni kaydırarak gösterir. <paramref name="offset"/> çağıran tarafından
    /// artırılır — böylece fonksiyon saf kalır ve testte zamana bağımlı olmaz.
    /// </summary>
    public static string Scroll(string? text, int offset, int width = Width)
    {
        string s = Sanitize(text);
        if (s.Length <= width) return s.PadRight(width);

        string extended = s + "  " + s;
        int period = s.Length + 2;
        int start = ((offset % period) + period) % period;
        return extended.Substring(start, width);
    }
}
