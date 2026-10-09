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
    static readonly ImageBrush darkGrain=CreateGrainBrush(.22),lightGrain=CreateGrainBrush(.18);

    internal static DrawingBrush Ambient(bool light)=>light?lightAmbient:darkAmbient;
    internal static ImageBrush Grain(bool light)=>light?lightGrain:darkGrain;

    static Color Color(string value)=>(Color)ColorConverter.ConvertFromString(value);
    static DrawingBrush CreateAmbient(bool light)
    {
        var diagonal=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1)};
        diagonal.GradientStops.Add(new(Color(light?"#104C786A":"#204F6660"),0));
        diagonal.GradientStops.Add(new(Color(light?"#064F6962":"#0B354B45"),.52));
        diagonal.GradientStops.Add(new(Color(light?"#043D6860":"#06213935"),1));
        var softLight=new RadialGradientBrush{Center=new(.23,.08),GradientOrigin=new(.23,.08),RadiusX=.78,RadiusY=.86};
        softLight.GradientStops.Add(new(Color(light?"#06465C56":"#0A415F59"),0));
        softLight.GradientStops.Add(new(Color(light?"#00465C56":"#00415F59"),1));
        var teal=new RadialGradientBrush{Center=new(.95,.90),GradientOrigin=new(.95,.90),RadiusX=.60,RadiusY=.75};
        teal.GradientStops.Add(new(Color(light?"#03476F63":"#04325247"),0));
        teal.GradientStops.Add(new(Color(light?"#00476F63":"#00325247"),1));
        var bounds=new RectangleGeometry(new Rect(0,0,1,1));
        var layers=new DrawingGroup();
        foreach(var brush in new Brush[]{diagonal,softLight,teal})layers.Children.Add(new GeometryDrawing(brush,null,bounds));
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
