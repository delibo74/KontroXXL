using KontroXXL.Core.Layout;
using Xunit;

namespace KontroXXL.Core.Tests.Layout;

public class WindowResizeGeometryTests
{
    const int W = 1000;
    const int H = 680;

    static WindowEdge Hit(int x, int y) => WindowResizeGeometry.HitTest(W, H, x, y);

    // ---- Kenarlar ---------------------------------------------------------

    [Fact]
    public void Middle_of_the_window_is_not_a_grip()
    {
        Assert.Equal(WindowEdge.None, Hit(W / 2, H / 2));
    }

    [Theory]
    [InlineData(0, 340, WindowEdge.Left)]
    [InlineData(3, 340, WindowEdge.Left)]
    [InlineData(999, 340, WindowEdge.Right)]
    [InlineData(996, 340, WindowEdge.Right)]
    [InlineData(500, 0, WindowEdge.Top)]
    [InlineData(500, 3, WindowEdge.Top)]
    [InlineData(500, 679, WindowEdge.Bottom)]
    [InlineData(500, 676, WindowEdge.Bottom)]
    public void Edge_band_is_four_pixels_wide(int x, int y, WindowEdge expected)
    {
        Assert.Equal(expected, Hit(x, y));
    }

    [Theory]
    [InlineData(4, 340)]     // sol bandin hemen ici
    [InlineData(995, 340)]   // sag bandin hemen ici
    [InlineData(500, 4)]
    [InlineData(500, 675)]
    public void One_pixel_inside_the_band_is_already_content(int x, int y)
    {
        Assert.Equal(WindowEdge.None, Hit(x, y));
    }

    // ---- Koseler ----------------------------------------------------------

    [Theory]
    [InlineData(0, 0, WindowEdge.TopLeft)]
    [InlineData(13, 1, WindowEdge.TopLeft)]
    [InlineData(1, 13, WindowEdge.TopLeft)]
    [InlineData(999, 0, WindowEdge.TopRight)]
    [InlineData(0, 679, WindowEdge.BottomLeft)]
    [InlineData(999, 679, WindowEdge.BottomRight)]
    [InlineData(997, 670, WindowEdge.BottomRight)]
    public void Corners_win_over_plain_edges(int x, int y, WindowEdge expected)
    {
        Assert.Equal(expected, Hit(x, y));
    }

    [Fact]
    public void Just_past_the_corner_length_is_a_plain_edge()
    {
        Assert.Equal(WindowEdge.Top, Hit(14, 1));
        Assert.Equal(WindowEdge.Left, Hit(1, 14));
    }

    // ---- Sinir / bozuk girdi ----------------------------------------------

    [Theory]
    [InlineData(-1, 340)]
    [InlineData(1000, 340)]
    [InlineData(500, -1)]
    [InlineData(500, 680)]
    public void Points_outside_the_window_are_not_grips(int x, int y)
    {
        Assert.Equal(WindowEdge.None, Hit(x, y));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, -5)]
    public void Degenerate_window_sizes_do_not_throw(int w, int h)
    {
        Assert.Equal(WindowEdge.None, WindowResizeGeometry.HitTest(w, h, 0, 0));
    }

    [Fact]
    public void Zero_border_disables_resizing_entirely()
    {
        Assert.Equal(WindowEdge.None, WindowResizeGeometry.HitTest(W, H, 0, 0, border: 0));
    }

    [Fact]
    public void Left_and_right_never_claim_the_same_pixel_in_a_tiny_window()
    {
        // 6 piksel genis bir pencerede 4 pikselik bant iki yandan cakisirdi.
        for (int x = 0; x < 6; x++)
        {
            var edge = WindowResizeGeometry.HitTest(6, 6, x, 3);
            Assert.NotEqual(WindowEdge.None, edge);   // her piksel bir tutamak
        }

        Assert.Contains(
            WindowResizeGeometry.HitTest(6, 6, 0, 3),
            new[] { WindowEdge.Left, WindowEdge.TopLeft, WindowEdge.BottomLeft });
        Assert.Contains(
            WindowResizeGeometry.HitTest(6, 6, 5, 3),
            new[] { WindowEdge.Right, WindowEdge.TopRight, WindowEdge.BottomRight });
    }

    // ---- Stretch ----------------------------------------------------------

    [Fact]
    public void Stretch_grows_with_the_container()
    {
        Assert.Equal(1140, WindowResizeGeometry.Stretch(1200, 60, 740));
    }

    [Fact]
    public void Stretch_never_shrinks_below_the_natural_size()
    {
        // Kucuk pencerede duzen bozulmaz; kaydirma devralir.
        Assert.Equal(740, WindowResizeGeometry.Stretch(600, 60, 740));
        Assert.Equal(740, WindowResizeGeometry.Stretch(0, 60, 740));
        Assert.Equal(740, WindowResizeGeometry.Stretch(-100, 60, 740));
    }

    [Fact]
    public void Stretch_tolerates_negative_reserved_and_natural()
    {
        Assert.Equal(1200, WindowResizeGeometry.Stretch(1200, -10, -10));
    }

    [Fact]
    public void At_the_baseline_size_the_result_is_exactly_the_natural_size()
    {
        // F4-2'nin gorsel kimlik sarti: reserved = taban - dogal verildiginde
        // pencere ilk boyutundayken hicbir sey degismemeli.
        const int baseline = 760, natural = 740;
        Assert.Equal(natural, WindowResizeGeometry.Stretch(baseline, baseline - natural, natural));

        // 200 piksel buyuyen pencere fazlaligi birebir bolume aktarir.
        Assert.Equal(natural + 200,
            WindowResizeGeometry.Stretch(baseline + 200, baseline - natural, natural));
    }

    // ---- CornerRadius -----------------------------------------------------

    [Fact]
    public void Corners_are_square_when_the_window_fills_the_work_area()
    {
        Assert.Equal(0, WindowResizeGeometry.CornerRadius(fillsWorkArea: true));
        Assert.Equal(12, WindowResizeGeometry.CornerRadius(fillsWorkArea: false));
        Assert.Equal(0, WindowResizeGeometry.CornerRadius(false, radius: -5));
    }
}

public class DonutGeometryTests
{
    // DonutProgress varsayilan boyutu.
    const int W = 140;
    const int H = 170;

    [Fact]
    public void Default_size_reproduces_the_old_hardcoded_rectangle_exactly()
    {
        // F4-2'nin sarti: gorsel kimlik DEGISMEYECEK. Faz 4 oncesi kod
        // Rectangle(20, 15, 100, 100) ciziyordu — burasi onu kilitler.
        Assert.Equal((20, 15, 100), DonutGeometry.Ring(W, H));
    }

    [Fact]
    public void Default_size_reproduces_the_old_text_positions_exactly()
    {
        var (_, y, d) = DonutGeometry.Ring(W, H);

        // Eskisi: (110 - h) / 2 + 15  ==  70 - h/2
        Assert.Equal(70f - 21f / 2, DonutGeometry.ValueTextTop(y, d, 21f));
        // Eskisi: sabit 130
        Assert.Equal(130, DonutGeometry.TitleTextTop(y, d));
    }

    [Fact]
    public void Ring_grows_with_the_control()
    {
        var (x, y, d) = DonutGeometry.Ring(240, 270);

        Assert.Equal(200, d);
        Assert.Equal(20, x);       // (240 - 200) / 2
        Assert.Equal(15, y);       // (270 - 200 - 40) / 2
    }

    [Fact]
    public void Ring_stays_centred_when_only_the_width_grows()
    {
        var (x, _, d) = DonutGeometry.Ring(300, H);

        Assert.Equal(100, d);      // yukseklik sinirliyor
        Assert.Equal(100, x);      // (300 - 100) / 2
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(-50, -50)]
    public void Degenerate_control_sizes_fall_back_to_the_minimum_diameter(int w, int h)
    {
        var (_, y, d) = DonutGeometry.Ring(w, h);

        Assert.Equal(DonutGeometry.MinDiameter, d);
        Assert.True(y >= 0);
    }
}
