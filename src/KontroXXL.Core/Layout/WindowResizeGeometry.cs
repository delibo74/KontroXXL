using System;

namespace KontroXXL.Core.Layout;

/// <summary>Bir pencerenin hangi kenarindan tutuldugu.</summary>
public enum WindowEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Kenarliksiz (<c>FormBorderStyle.None</c>) bir pencerenin yeniden boyutlandirma
/// geometrisi — saf tamsayi matematigi, hicbir Windows tipi yok.
/// </summary>
/// <remarks>
/// F4-2: pencere kilidi acilirken tutamak hesabinin WinForms icine gomulmesi onu
/// test edilemez yapardi. Kenar/kose kararlari burada, <c>MainForm</c> yalnizca
/// sonucu WM_NCHITTEST kodlarina cevirir.
/// </remarks>
public static class WindowResizeGeometry
{
    /// <summary>Kenar tutamaginin kalinligi (piksel).</summary>
    public const int DefaultBorder = 4;

    /// <summary>Kose tutamaginin kenar boyunca uzunlugu (piksel).</summary>
    public const int DefaultCorner = 14;

    /// <summary>
    /// <paramref name="x"/>/<paramref name="y"/> pencerenin ISTEMCI koordinatlaridir.
    /// Pencere disinda kalan ya da tutamak bandina girmeyen her nokta
    /// <see cref="WindowEdge.None"/> doner.
    /// </summary>
    public static WindowEdge HitTest(
        int width, int height, int x, int y,
        int border = DefaultBorder, int corner = DefaultCorner)
    {
        if (width <= 0 || height <= 0) return WindowEdge.None;
        if (x < 0 || y < 0 || x >= width || y >= height) return WindowEdge.None;
        if (border <= 0) return WindowEdge.None;

        // Cok kucuk pencerede sol ve sag bantlar cakisir; bant yariya kadar daralir ki
        // "sol" ile "sag" ayni pikseli sahiplenmesin.
        int half = Math.Min(width, height) / 2;
        int b = Math.Min(border, Math.Max(1, half));
        int c = Math.Max(b, Math.Min(corner, Math.Max(1, half)));

        bool left = x < b;
        bool right = x >= width - b;
        bool top = y < b;
        bool bottom = y >= height - b;

        if (!left && !right && !top && !bottom) return WindowEdge.None;

        // Kose: kenar bandindayken diger eksende kose uzunlugu icindeysek kosedeyiz.
        bool nearLeft = x < c;
        bool nearRight = x >= width - c;
        bool nearTop = y < c;
        bool nearBottom = y >= height - c;

        if ((top && nearLeft) || (left && nearTop)) return WindowEdge.TopLeft;
        if ((top && nearRight) || (right && nearTop)) return WindowEdge.TopRight;
        if ((bottom && nearLeft) || (left && nearBottom)) return WindowEdge.BottomLeft;
        if ((bottom && nearRight) || (right && nearBottom)) return WindowEdge.BottomRight;

        if (left) return WindowEdge.Left;
        if (right) return WindowEdge.Right;
        if (top) return WindowEdge.Top;
        return WindowEdge.Bottom;
    }

    /// <summary>
    /// Esneyen bir bolumun olcusu (genislik ya da yukseklik): kapsayicidan sabit paylar
    /// dusulur, ama asla <paramref name="natural"/> altina inmez — kucuk pencerede duzen
    /// bozulmasin, kaydirma devralsin.
    /// </summary>
    /// <remarks>
    /// F4-2'de <paramref name="reserved"/> "taban olcu eksi dogal olcu" olarak veriliyor:
    /// boylece pencere ILK boyutundayken sonuc tam olarak <paramref name="natural"/>
    /// cikar (gorsel kimlik degismez) ve yalnizca FAZLALIK bolume dagitilir.
    /// </remarks>
    public static int Stretch(int available, int reserved, int natural)
    {
        int n = Math.Max(0, natural);
        if (available <= 0) return n;
        return Math.Max(n, available - Math.Max(0, reserved));
    }

    /// <summary>
    /// Yuvarlak kose yaricapi. Pencere ekrani doldurdugunda kose yuvarlatilmaz
    /// (buyutulmus pencerede yuvarlak kose Windows'ta yanlis gorunur) — 0 doner.
    /// </summary>
    public static int CornerRadius(bool fillsWorkArea, int radius = 12) =>
        fillsWorkArea ? 0 : Math.Max(0, radius);
}

/// <summary>
/// <c>DonutProgress</c> cizim geometrisi. Faz 4 oncesi sabit kodluydu
/// (<c>Rectangle(20, 15, 100, 100)</c>, baslik <c>y=130</c>) ve kontrol buyutulse
/// bile olceklenmiyordu.
/// </summary>
/// <remarks>
/// Buranin varlik sebebi TEK BASINA olcekleme degil, "gorsel kimlik degismedi"
/// iddiasini test edilebilir kilmak: varsayilan 140x170'te sonuc eski sabitlerle
/// BIREBIR ayni cikmali, testler bunu kilitler.
/// </remarks>
public static class DonutGeometry
{
    /// <summary>Kontrolun genisliginden halkanin iki yanina ayrilan toplam pay.</summary>
    public const int HorizontalPadding = 40;

    /// <summary>Halkanin ustunde/altinda kalan pay (baslik seridi dahil).</summary>
    public const int VerticalPadding = 70;

    /// <summary>Halka ile baslik arasindaki bosluk.</summary>
    public const int TitleGap = 15;

    /// <summary>En kucuk anlamli cap; bunun altinda cizim okunmaz olur.</summary>
    public const int MinDiameter = 24;

    /// <summary>Halkanin sinirlayici karesi (sol, ust, cap).</summary>
    public static (int X, int Y, int Diameter) Ring(int width, int height)
    {
        int d = Math.Max(
            MinDiameter,
            Math.Min(width - HorizontalPadding, height - VerticalPadding));

        int x = (width - d) / 2;
        int y = Math.Max(0, (height - d - 40) / 2);
        return (x, y, d);
    }

    /// <summary>Ortadaki deger yazisinin ust kenari.</summary>
    public static float ValueTextTop(int ringY, int diameter, float textHeight) =>
        ringY + diameter / 2 + 5 - textHeight / 2;

    /// <summary>Halkanin altindaki baslik yazisinin ust kenari.</summary>
    public static int TitleTextTop(int ringY, int diameter) => ringY + diameter + TitleGap;
}
