using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kachalka;

// The backdrop is composed from shared frozen brushes. Its single small grain
// tile is created once; changing theme never regenerates pixels or animates it.
static class ThemeBackground
{
    internal const int GrainSize=128;
    static readonly BitmapSource grainTile=CreateGrain();
    static readonly DrawingBrush darkAmbient=CreateAmbient(false),lightAmbient=CreateAmbient(true);
    static readonly ImageBrush darkGrain=CreateGrainBrush(.38),lightGrain=CreateGrainBrush(.28);

    internal static DrawingBrush Ambient(bool light)=>light?lightAmbient:darkAmbient;
    internal static ImageBrush Grain(bool light)=>light?lightGrain:darkGrain;

    static Color Color(string value)=>(Color)ColorConverter.ConvertFromString(value);
    static DrawingBrush CreateAmbient(bool light)
    {
        var diagonal=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1)};
        diagonal.GradientStops.Add(new(Color(light?"#165D56B9":"#2236276A"),0));
        diagonal.GradientStops.Add(new(Color(light?"#08497FC4":"#10425482"),.52));
        diagonal.GradientStops.Add(new(Color(light?"#1461A9AD":"#1C235E60"),1));
        var violet=new RadialGradientBrush{Center=new(.23,.08),GradientOrigin=new(.23,.08),RadiusX=.78,RadiusY=.86};
        violet.GradientStops.Add(new(Color(light?"#127966D8":"#224C318C"),0));
        violet.GradientStops.Add(new(Color(light?"#007966D8":"#004C318C"),1));
        var teal=new RadialGradientBrush{Center=new(.95,.90),GradientOrigin=new(.95,.90),RadiusX=.60,RadiusY=.75};
        teal.GradientStops.Add(new(Color(light?"#0A54B1AA":"#122F7372"),0));
        teal.GradientStops.Add(new(Color(light?"#0054B1AA":"#002F7372"),1));
        var bounds=new RectangleGeometry(new Rect(0,0,1,1));
        var layers=new DrawingGroup();
        foreach(var brush in new Brush[]{diagonal,violet,teal})layers.Children.Add(new GeometryDrawing(brush,null,bounds));
        var backdrop=new DrawingBrush(layers){Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new(0,0,1,1)};
        backdrop.Freeze();return backdrop;
    }
    static BitmapSource CreateGrain()
    {
        var pixels=new byte[GrainSize*GrainSize*4];uint state=0x8AC53B17;
        for(var offset=0;offset<pixels.Length;offset+=4)
        {
            state^=state<<13;state^=state>>17;state^=state<<5;
            var alpha=(byte)(4+state%11);var gray=(byte)((state&0x80000000)!=0?alpha:0);
            pixels[offset]=pixels[offset+1]=pixels[offset+2]=gray;pixels[offset+3]=alpha;
        }
        var tile=BitmapSource.Create(GrainSize,GrainSize,96,96,PixelFormats.Pbgra32,null,pixels,GrainSize*4);
        tile.Freeze();return tile;
    }
    static ImageBrush CreateGrainBrush(double opacity)
    {
        var brush=new ImageBrush(grainTile){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new(0,0,GrainSize,GrainSize),
            ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new(0,0,GrainSize,GrainSize),Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top,Opacity=opacity};
        RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.NearestNeighbor);brush.Freeze();return brush;
    }
}

public partial class MainWindow
{
    void ApplyBackgroundTheme(bool light)
    {
        Application.Current.Resources["AmbientGlow"]=ThemeBackground.Ambient(light);
        Application.Current.Resources["BackgroundGrain"]=ThemeBackground.Grain(light);
    }
}
