using System.Text;

namespace KontroXXL.Core.Serial;

/// <summary>
/// Bayt akışını satırlara böler. SerialPort.ReadLine() bloklar ve kopmada
/// beklenmedik istisnalar fırlatır; okuma döngüsü ham bayt okuyup bunu kullanır.
/// </summary>
public sealed class SerialLineBuffer
{
    readonly StringBuilder _sb = new();
    readonly int _maxLineLength;
    bool _overflowed;

    public SerialLineBuffer(int maxLineLength = 256) => _maxLineLength = maxLineLength;

    public IEnumerable<string> Feed(ReadOnlySpan<byte> chunk)
    {
        var lines = new List<string>();
        foreach (byte b in chunk)
        {
            char c = (char)b;
            if (c == '\n')
            {
                if (!_overflowed && _sb.Length > 0) lines.Add(_sb.ToString());
                _sb.Clear();
                _overflowed = false;
                continue;
            }
            if (c == '\r') continue;

            if (_sb.Length >= _maxLineLength) { _overflowed = true; _sb.Clear(); continue; }
            _sb.Append(c);
        }
        return lines;
    }

    public void Reset() { _sb.Clear(); _overflowed = false; }
}
