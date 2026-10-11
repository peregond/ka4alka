using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Kachalka;

// Poster rows draw hover and focus rings 4 px outside each card. At fractional Windows scales (125 %,
// 150 %) the rounded width of a full row of cards can come out a fraction wider than the row itself,
// and WPF then clips the panel to its own bounds, whose left edge is the first card's edge: the left
// side of that card's ring disappeared. The grid asks for no more width than it is given and, like the
// row around it, never clips its cards; the page's scroll viewer still clips at the window edge.
public sealed class PosterGrid:UniformGrid
{
    protected override Size MeasureOverride(Size constraint)
    {
        var size=base.MeasureOverride(constraint);
        return new(double.IsInfinity(constraint.Width)?size.Width:Math.Min(size.Width,constraint.Width),size.Height);
    }
    protected override Geometry? GetLayoutClip(Size layoutSlotSize)=>null;
}

public sealed class PosterRow:ItemsControl
{
    protected override Geometry? GetLayoutClip(Size layoutSlotSize)=>null;
}
