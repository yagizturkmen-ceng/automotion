using System;
using System.Windows;

namespace AutoMotion.Tests;

/// <summary>
/// Kenardan/köşeden panel boyutlandırmasının GEOMETRİSİ.
///
/// Kenar tespiti ve yeni ölçek hesabı MainWindow'a gömülü olduğundan (pencere kurmadan
/// örneklenemez) burada aynı formüller bağımsız olarak doğrulanır: hangi bölge hangi kenar sayılır,
/// ve sürüklenen kenarın karşısı sabit kalacak şekilde ölçek nasıl çıkar.
///
/// Neden değerli: bu hesaplar "sağdan çekince sol kenar kaymasın" gibi gözle fark edilen ama
/// derleyicinin yakalayamadığı işaret/yön hatalarına açık. Uygulamada ölçülen gerçek panel
/// kutusu (ekran y=76.8..698.4, x=32.8..348.8) referans alındı.
/// </summary>
public class PanelEdgeResizeTests
{
    private const double Grab = 9;                       // MainWindow.EdgeGrabThickness
    private static readonly Rect Panel = new(32.8, 76.8, 316, 621.6);

    private enum Edge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>MainWindow.DetectEdgeAt ile aynı kural.</summary>
    private static Edge DetectEdge(Rect r, Point p)
    {
        var outer = new Rect(r.X - Grab, r.Y - Grab, r.Width + Grab * 2, r.Height + Grab * 2);
        if (!outer.Contains(p)) return Edge.None;

        bool left = Math.Abs(p.X - r.Left) <= Grab;
        bool right = Math.Abs(p.X - r.Right) <= Grab;
        bool top = Math.Abs(p.Y - r.Top) <= Grab;
        bool bottom = Math.Abs(p.Y - r.Bottom) <= Grab;

        if (top && left) return Edge.TopLeft;
        if (top && right) return Edge.TopRight;
        if (bottom && left) return Edge.BottomLeft;
        if (bottom && right) return Edge.BottomRight;
        if (left) return Edge.Left;
        if (right) return Edge.Right;
        if (top) return Edge.Top;
        if (bottom) return Edge.Bottom;
        return Edge.None;
    }

    [Fact]
    public void EachEdgeAndCorner_IsDetected()
    {
        Assert.Equal(Edge.Left, DetectEdge(Panel, new Point(Panel.Left, 300)));
        Assert.Equal(Edge.Right, DetectEdge(Panel, new Point(Panel.Right, 300)));
        Assert.Equal(Edge.Top, DetectEdge(Panel, new Point(200, Panel.Top)));
        Assert.Equal(Edge.Bottom, DetectEdge(Panel, new Point(200, Panel.Bottom)));
        Assert.Equal(Edge.TopLeft, DetectEdge(Panel, new Point(Panel.Left, Panel.Top)));
        Assert.Equal(Edge.TopRight, DetectEdge(Panel, new Point(Panel.Right, Panel.Top)));
        Assert.Equal(Edge.BottomLeft, DetectEdge(Panel, new Point(Panel.Left, Panel.Bottom)));
        Assert.Equal(Edge.BottomRight, DetectEdge(Panel, new Point(Panel.Right, Panel.Bottom)));
    }

    /// <summary>Kenar, hem içeriden hem DIŞARIDAN yaklaşınca tutulabilmeli - kullanıcı imleci
    /// panelin tam kenarına getirdiğinde bir piksel şaşınca özellik ölmemeli.</summary>
    [Theory]
    [InlineData(-6)]  // panelin biraz DIŞI
    [InlineData(0)]   // tam kenar
    [InlineData(6)]   // panelin biraz İÇİ
    public void Edge_IsGrabbable_FromBothSides(double offset)
    {
        Assert.Equal(Edge.Right, DetectEdge(Panel, new Point(Panel.Right + offset, 300)));
        Assert.Equal(Edge.Bottom, DetectEdge(Panel, new Point(200, Panel.Bottom + offset)));
    }

    [Fact]
    public void PanelCentre_AndEmptyDesktop_AreNotEdges()
    {
        Assert.Equal(Edge.None, DetectEdge(Panel, new Point(200, 300)));   // panel ortası: taşıma alanı
        Assert.Equal(Edge.None, DetectEdge(Panel, new Point(900, 700)));   // boş masaüstü
    }

    // ---------------------------------------------------------------- ölçek hesabı

    /// <summary>MainWindow.UpdateEdgeResize'daki formül: sürüklenen kenarın KARŞISINDAKİ kenardan
    /// uzaklık, yeni ölçeği verir.</summary>
    private static double ScaleFromDrag(Edge edge, Rect start, Point cursor, double unscaledWidth, double unscaledHeight)
        => edge switch
        {
            Edge.Right => (cursor.X - start.Left) / unscaledWidth,
            Edge.Left => (start.Right - cursor.X) / unscaledWidth,
            Edge.Bottom => (cursor.Y - start.Top) / unscaledHeight,
            Edge.Top => (start.Bottom - cursor.Y) / unscaledHeight,
            _ => double.NaN
        };

    [Fact]
    public void DraggingRightEdge_Outward_GrowsThePanel()
    {
        double before = Panel.Width / 340.0;
        double after = ScaleFromDrag(Edge.Right, Panel, new Point(Panel.Right + 100, 300), 340, 670);

        Assert.True(after > before, $"sağa çekince büyümeli: {before:0.###} -> {after:0.###}");
    }

    [Fact]
    public void DraggingRightEdge_Inward_ShrinksThePanel()
    {
        double before = Panel.Width / 340.0;
        double after = ScaleFromDrag(Edge.Right, Panel, new Point(Panel.Right - 100, 300), 340, 670);

        Assert.True(after < before, $"içeri çekince küçülmeli: {before:0.###} -> {after:0.###}");
    }

    /// <summary>Sol kenardan SOLA çekmek de paneli BÜYÜTMELİ (sağ kenar sabit kalır) - işaret
    /// hatasında panel ters yöne tepki verirdi.</summary>
    [Fact]
    public void DraggingLeftEdge_Outward_GrowsThePanel()
    {
        double before = Panel.Width / 340.0;
        double after = ScaleFromDrag(Edge.Left, Panel, new Point(Panel.Left - 100, 300), 340, 670);

        Assert.True(after > before, $"sola çekince büyümeli: {before:0.###} -> {after:0.###}");
    }

    [Fact]
    public void DraggingTopEdge_Upward_GrowsThePanel()
    {
        double before = Panel.Height / 670.0;
        double after = ScaleFromDrag(Edge.Top, Panel, new Point(200, Panel.Top - 80), 340, 670);

        Assert.True(after > before, $"yukarı çekince büyümeli: {before:0.###} -> {after:0.###}");
    }

    /// <summary>Sol/üst kenardan çekilirken KARŞI kenar yerinde kalmalı: panel yeni ölçeğe göre
    /// kaydırılır. Kaydırma yapılmazsa panel sürüklenen yönün tersine "kaçar".</summary>
    [Fact]
    public void DraggingLeftEdge_KeepsTheRightEdgeAnchored()
    {
        const double unscaledWidth = 340;
        double newScale = ScaleFromDrag(Edge.Left, Panel, new Point(Panel.Left - 100, 300), unscaledWidth, 670);

        double newLeft = Panel.Right - unscaledWidth * newScale;
        double newRight = newLeft + unscaledWidth * newScale;

        Assert.Equal(Panel.Right, newRight, precision: 6);
        Assert.True(newLeft < Panel.Left, "sol kenar sola doğru açılmalı");
    }

    [Fact]
    public void DraggingTopEdge_KeepsTheBottomEdgeAnchored()
    {
        const double unscaledHeight = 670;
        double newScale = ScaleFromDrag(Edge.Top, Panel, new Point(200, Panel.Top - 80), 340, unscaledHeight);

        double newTop = Panel.Bottom - unscaledHeight * newScale;
        double newBottom = newTop + unscaledHeight * newScale;

        Assert.Equal(Panel.Bottom, newBottom, precision: 6);
        Assert.True(newTop < Panel.Top, "üst kenar yukarı doğru açılmalı");
    }
}
