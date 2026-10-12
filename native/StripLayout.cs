namespace Kachalka;

// Arithmetic of the sideways strips on a series card (seasons, episodes). It stays free of WPF so the
// decisions can be checked anywhere: when a strip has to scroll, where it must stop to show a segment,
// and how far one arrow press travels.
public static class StripLayout
{
    // A strip scrolls when its segments are wider than the room the page leaves for them. The room does not
    // depend on the scroll controls, so showing them can never flip the decision back.
    public static bool Overflows(double extent,double room)=>room>0&&extent>room+.5;

    // The offset that shows the segment [left,left+width] in a viewport of the given size. A segment that is
    // already fully visible leaves the strip where it is; otherwise the segment is centred.
    public static double Reveal(double left,double width,double offset,double viewport,double scrollable)
    {
        var last=Math.Max(0,scrollable);
        if(viewport<=0)return Math.Clamp(offset,0,last);
        if(left>=offset&&left+width<=offset+viewport)return Math.Clamp(offset,0,last);
        return Math.Clamp(left-(viewport-width)/2,0,last);
    }

    // One arrow press moves by most of the viewport, so the last segment of the old page is still in sight.
    public static double Page(double offset,double viewport,double scrollable,int direction)=>
        Math.Clamp(offset+Math.Sign(direction)*Math.Max(0,viewport)*.8,0,Math.Max(0,scrollable));
}
