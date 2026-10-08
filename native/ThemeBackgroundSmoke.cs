using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kachalka;

public partial class MainWindow
{
    object CheckBackgroundTheme()
    {
        var background=(SolidColorBrush)FindResource("Bg");var ambient=(DrawingBrush)FindResource("AmbientGlow");var grain=(ImageBrush)FindResource("BackgroundGrain");
        if(!ambient.IsFrozen||!grain.IsFrozen||grain.ImageSource is not BitmapSource tile||!tile.IsFrozen||
           tile.PixelWidth!=ThemeBackground.GrainSize||tile.PixelHeight!=ThemeBackground.GrainSize||tile.Format!=PixelFormats.Pbgra32||
           grain.TileMode!=TileMode.Tile||grain.ViewportUnits!=BrushMappingMode.Absolute||grain.Viewport.Width!=tile.PixelWidth||grain.Viewport.Height!=tile.PixelHeight)
            throw new Exception("The backdrop grain must be one small frozen bitmap tiled without resizing or animation.");
        if(!ReferenceEquals(grain,ThemeBackground.Grain(prefs.Light))||!ReferenceEquals(ambient,ThemeBackground.Ambient(prefs.Light))||
           !ReferenceEquals(ThemeBackground.Grain(false).ImageSource,ThemeBackground.Grain(true).ImageSource))
            throw new Exception("Theme changes must reuse the shared backdrop and grain tile.");
        for(var change=0;change<64;change++)
            if(!ReferenceEquals(ThemeBackground.Grain(change%2==0).ImageSource,tile))throw new Exception("Switching theme regenerated the grain bitmap.");
        var layer=FindVisual<Border>(this,element=>element.Name=="BackgroundGrainLayer")??throw new Exception("The backdrop grain layer is missing.");
        if(Content is not Grid root||!ReferenceEquals(layer.Parent,root)||layer.IsHitTestVisible||layer.Focusable||layer.Child!=null||layer.Effect!=null||
           !ReferenceEquals(layer.Background,grain)||Math.Abs(layer.ActualWidth-root.ActualWidth)>.5||Math.Abs(layer.ActualHeight-root.ActualHeight)>.5||
           root.Children.IndexOf(layer)>=root.Children.IndexOf(RootGrid)||VisualElements<Border>(this).Count(element=>ReferenceEquals(element.Background,grain))!=1)
            throw new Exception("Grain must fill only the root backdrop, behind panels, text and posters, without receiving input.");
        var glow=root.Children.OfType<Border>().SingleOrDefault(element=>ReferenceEquals(element.Background,ambient));
        if(glow==null||root.Children.IndexOf(glow)>=root.Children.IndexOf(layer)||glow.IsHitTestVisible)
            throw new Exception("The root gradient and grain layers are out of order.");

        const int width=800,height=500;
        byte[] RenderBackdrop(bool noise)
        {
            var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())
            {
                var bounds=new Rect(0,0,width,height);drawing.DrawRectangle(background,null,bounds);drawing.DrawRectangle(ambient,null,bounds);
                if(noise)drawing.DrawRectangle(grain,null,bounds);
            }
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);return pixels;
        }
        var smooth=RenderBackdrop(false);var textured=RenderBackdrop(true);
        var changed=0;var maximumNoise=0;var maximumTint=0;double textContrast=double.MaxValue,mutedContrast=double.MaxValue;
        var text=((SolidColorBrush)FindResource("Text")).Color;var muted=((SolidColorBrush)FindResource("Muted")).Color;
        double Channel(byte value){var v=value/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
        double Luminance(Color color)=>.2126*Channel(color.R)+.7152*Channel(color.G)+.0722*Channel(color.B);
        double Contrast(Color ink,Color ground){var a=Luminance(ink);var b=Luminance(ground);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);}
        for(var offset=0;offset<textured.Length;offset+=4)
        {
            var delta=0;
            for(var channel=0;channel<3;channel++)delta=Math.Max(delta,Math.Abs(textured[offset+channel]-smooth[offset+channel]));
            if(delta>0)changed++;maximumNoise=Math.Max(maximumNoise,delta);
            maximumTint=Math.Max(maximumTint,Math.Max(Math.Abs(smooth[offset]-background.Color.B),Math.Max(Math.Abs(smooth[offset+1]-background.Color.G),Math.Abs(smooth[offset+2]-background.Color.R))));
            var ground=Color.FromRgb(textured[offset+2],textured[offset+1],textured[offset]);
            textContrast=Math.Min(textContrast,Contrast(text,ground));mutedContrast=Math.Min(mutedContrast,Contrast(muted,ground));
            if(textured[offset+3]!=255)throw new Exception("The backdrop rendering lost its solid theme base.");
        }
        if(changed<width*height/10||maximumNoise is <1 or >8)throw new Exception("Backdrop grain is absent or overwhelms the gradient.");
        if(maximumTint>36)throw new Exception("The backdrop gradient overwhelms the selected theme base.");
        if(textContrast<7||mutedContrast<4.5)throw new Exception($"Backdrop text contrast is insufficient ({textContrast:F2} / {mutedContrast:F2}).");
        double[] Mean(int x,int y)
        {
            var result=new double[3];const int patch=24;
            for(var py=y;py<y+patch;py++)for(var px=x;px<x+patch;px++)for(var c=0;c<3;c++)result[c]+=smooth[(py*width+px)*4+c]/(double)(patch*patch);
            return result;
        }
        var top=Mean(20,20);var bottom=Mean(width-44,height-44);
        var variation=top.Zip(bottom,(a,b)=>Math.Abs(a-b)).Max();
        if(variation<4)throw new Exception("The rendered backdrop has no visible gradual change in color.");
        return new{Light=prefs.Light,RootOnly=true,ReceivesInput=false,Frozen=true,TileSize=tile.PixelWidth,TileBytes=tile.PixelWidth*tile.PixelHeight*4,
            SharedAcrossThemes=true,MaximumGrainDelta=maximumNoise,MaximumTintDelta=maximumTint,GradientVariation=Math.Round(variation,2),
            TextContrast=Math.Round(textContrast,2),MutedContrast=Math.Round(mutedContrast,2)};
    }
}
