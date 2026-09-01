using System.Text;
using KontroXXL.Core.Serial;
using Xunit;

namespace KontroXXL.Core.Tests.Serial;

public class SerialLineBufferTests
{
    static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Emits_a_complete_line()
        => Assert.Equal(new[] { "EV:UP" }, new SerialLineBuffer().Feed(B("EV:UP\n")).ToArray());

    [Fact]
    public void Joins_a_line_split_across_two_chunks()
    {
        var buf = new SerialLineBuffer();
        Assert.Empty(buf.Feed(B("EV:")).ToArray());
        Assert.Equal(new[] { "EV:UP" }, buf.Feed(B("UP\n")).ToArray());
    }

    [Fact]
    public void Emits_two_lines_from_one_chunk()
        => Assert.Equal(new[] { "EV:UP", "EV:DN" },
                        new SerialLineBuffer().Feed(B("EV:UP\nEV:DN\n")).ToArray());

    [Fact]
    public void Strips_carriage_returns()
        => Assert.Equal(new[] { "CMD:READY" }, new SerialLineBuffer().Feed(B("CMD:READY\r\n")).ToArray());

    [Fact]
    public void Skips_empty_lines()
        => Assert.Equal(new[] { "EV:UP" }, new SerialLineBuffer().Feed(B("\n\nEV:UP\n\n")).ToArray());

    [Fact]
    public void Drops_a_runaway_line_instead_of_growing_without_bound()
    {
        var buf = new SerialLineBuffer(maxLineLength: 32);
        Assert.Empty(buf.Feed(B(new string('x', 500))).ToArray());
        // Taşma sonrası kendini toparlar:
        Assert.Equal(new[] { "EV:UP" }, buf.Feed(B("\nEV:UP\n")).ToArray());
    }

    [Fact]
    public void Reset_discards_a_partial_line()
    {
        var buf = new SerialLineBuffer();
        buf.Feed(B("EV:"));
        buf.Reset();
        Assert.Equal(new[] { "UP" }, buf.Feed(B("UP\n")).ToArray());
    }
}
