using KontroXXL.Core.Layout;
using Xunit;

namespace KontroXXL.Core.Tests.Layout;

public class DockTopStackTests
{
    // WinForms Dock.Top kontrolleri ekleme sirasinin TERSINE yigar: en son eklenen
    // en ustte durur. Gorsel sirayi ekleme sirasina cevirmek bu yuzden ters cevirmektir.
    [Fact]
    public void Insertion_order_is_the_reverse_of_the_visual_order()
    {
        var visual = new[] { "ust", "orta", "alt" };

        var insertion = DockTopStack.ToInsertionOrder(visual);

        Assert.Equal(new[] { "alt", "orta", "ust" }, insertion);
    }

    [Fact]
    public void The_topmost_section_is_added_last()
    {
        var visual = new[] { "NAS ozeti", "havuzlar", "uyarilar", "servisler" };

        var insertion = DockTopStack.ToInsertionOrder(visual);

        Assert.Equal("NAS ozeti", insertion[insertion.Count - 1]);
        Assert.Equal("servisler", insertion[0]);
    }

    [Fact]
    public void A_single_section_is_unchanged()
    {
        Assert.Equal(new[] { "tek" }, DockTopStack.ToInsertionOrder(new[] { "tek" }));
    }

    [Fact]
    public void An_empty_list_stays_empty()
    {
        Assert.Empty(DockTopStack.ToInsertionOrder(new string[0]));
    }

    [Fact]
    public void Null_is_treated_as_empty_rather_than_throwing()
    {
        Assert.Empty(DockTopStack.ToInsertionOrder<string>(null));
    }

    [Fact]
    public void Applying_it_twice_returns_the_original_order()
    {
        var visual = new[] { "a", "b", "c", "d" };

        var twice = DockTopStack.ToInsertionOrder(DockTopStack.ToInsertionOrder(visual));

        Assert.Equal(visual, twice);
    }
}
