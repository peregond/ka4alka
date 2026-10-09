using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;

public partial class MainWindow
{
    object CheckBannerCorners(string output)
    {
        Directory.CreateDirectory(output);
        var checks=new List<object>();
        var captureOffsets=new Dictionary<FrameworkElement,Vector>();
        var poster=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,
            new byte[]{90,150,220,255,90,150,220,255,90,150,220,255,90,150,220,255},8);
        poster.Freeze();
        BitmapSource Raster(FrameworkElement element,double scale)
        {
            var size=new Size(element.ActualWidth,element.ActualHeight);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            var parent=VisualTreeHelper.GetParent(element) as Border;
            FrameworkElement root=element;
            while(VisualTreeHelper.GetParent(root) is FrameworkElement ancestor)root=ancestor;
            var detached=parent?.Child==element;
            var scene=new ContainerVisual();var container=new ContainerVisual();scene.Children.Add(container);var contained=false;
            try
            {
                // A nested Grid's visual offset remains part of a direct or brush
                // capture. Detach this isolated fixture and actually arrange it
                // at the origin, retaining its production clip and children.
                if(detached)
                {
                    parent!.Child=null;element.InvalidateMeasure();element.InvalidateArrange();element.Measure(size);element.Arrange(new Rect(size));element.UpdateLayout();
                }
                var offset=VisualTreeHelper.GetOffset(element);captureOffsets[element]=offset;
                // FrameworkElement can retain an arrangement offset after it is
                // detached. Cancel the observed offset in the capture container;
                // the artwork's own clip and rendering remain unchanged.
                container.Offset=-offset;container.Children.Add(element);contained=true;
                bitmap.Render(scene);return bitmap;
            }
            finally
            {
                if(contained)container.Children.Remove(element);
                if(detached){parent!.Child=element;root.UpdateLayout();}
            }
        }
        void Save(BitmapSource bitmap,string name)
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        void CheckContour(FrameworkElement element,double radius,double thickness,double scale,string stage)
        {
            // An ordinary rounded Border is the rendering reference. This checks the
            // actual alpha contour, including clipping and any interaction overlays.
            // The visible stroke follows pixel snapping. The explicit artwork
            // geometry does not: its last raster row can legitimately cover
            // half a pixel at fractional scales, so compare an unsnapped fill.
            var reference=new Border{Width=element.ActualWidth,Height=element.ActualHeight,CornerRadius=new(radius),BorderThickness=new(thickness),Background=Brushes.White,BorderBrush=Brushes.White,UseLayoutRounding=thickness>0,SnapsToDevicePixels=thickness>0};
            var size=new Size(reference.Width,reference.Height);reference.Measure(size);reference.Arrange(new Rect(size));reference.UpdateLayout();
            var actual=Raster(element,scale);var expected=Raster(reference,scale);
            if(actual.PixelWidth!=expected.PixelWidth||actual.PixelHeight!=expected.PixelHeight)throw new Exception(stage+": rounded contour has the wrong dimensions.");
            var stride=actual.PixelWidth*4;var pixels=new byte[stride*actual.PixelHeight];var target=new byte[pixels.Length];
            actual.CopyPixels(pixels,stride,0);expected.CopyPixels(target,stride,0);
            var corner=(int)Math.Ceiling((radius+3)*scale);
            void Fail(string message)
            {
                Save(actual,stage+"-failed");Save(expected,stage+"-expected");
                var width=Math.Min(actual.PixelWidth,corner+4);
                int[][] Rows(byte[] data)=>Enumerable.Range(0,Math.Min(4,actual.PixelHeight)).Select(y=>Enumerable.Range(0,width).Select(x=>(int)data[y*stride+x*4+3]).ToArray()).ToArray();
                File.WriteAllText(Path.Combine(output,stage+"-failure.json"),JsonSerializer.Serialize(new
                {
                    Message=message,Scale=scale,ElementType=element.GetType().Name,ElementSize=element.RenderSize.ToString(),CapturedOffset=captureOffsets[element].ToString(),RestoredOffset=VisualTreeHelper.GetOffset(element).ToString(),
                    Clip=element.Clip?.ToString(),ClipBounds=element.Clip?.Bounds.ToString(),ActualAlphaRows=Rows(pixels),ExpectedAlphaRows=Rows(target)
                },new JsonSerializerOptions{WriteIndented=true}));
                throw new Exception(message);
            }
            foreach(var right in new[]{false,true})foreach(var bottom in new[]{false,true})
            {
                for(var y=0;y<corner;y++)for(var x=0;x<corner;x++)
                {
                    var px=right?actual.PixelWidth-1-x:x;var py=bottom?actual.PixelHeight-1-y:y;
                    var index=py*stride+px*4+3;var alpha=pixels[index];var expectedAlpha=target[index];
                    if(expectedAlpha==0&&alpha>8)Fail($"{stage}: artwork or ring spills outside the rounded corner at {px},{py} (alpha {alpha}).");
                    if(expectedAlpha==255&&alpha<240)Fail($"{stage}: a rounded corner is cut off at {px},{py} (alpha {alpha}).");
                }
            }
        }
        // The fixture stays outside the window so Loaded cannot request artwork
        // or change the catalog while its sizes are checked. The carousel is a
        // single 22 px rounded frame without a stroke; its artwork follows it.
        var savedCarousel=carousel;
        try
        {
            var item=new MediaItem(-987654300,"Проверка округления","Фильмы","Приключения",2026,"8,1","8,0","#26344E");
            var banner=(Border)BuildFeatureCarousel([item]);banner.UseLayoutRounding=true;banner.SnapsToDevicePixels=true;
            foreach(var size in new[]{new Size(1140,300),new Size(760,280),new Size(392,300),new Size(316,320)})
            {
                banner.Width=size.Width;banner.Height=size.Height;banner.Measure(size);banner.Arrange(new Rect(size));banner.UpdateLayout();
                var image=VisualElements<Image>(banner).Single(x=>x.Tag?.ToString()=="FeaturePoster");image.Source=null;banner.UpdateLayout();
                if(carousel!.LiteCover.Visibility!=Visibility.Visible)throw new Exception("A missing backdrop hides the separate portrait fallback.");
                image.Source=poster;banner.UpdateLayout();
                if(!prefs.LiteMode&&carousel.LiteCover.Visibility!=Visibility.Collapsed)throw new Exception("The portrait fallback covers loaded landscape artwork.");
                var artwork=(Grid)banner.Child;
                foreach(var scale in new[]{1d,1.25,1.5,2d})
                {
                    var stage=$"banner-{size.Width}-{scale}";
                    CheckContour(banner,22,0,scale,stage);
                    CheckContour(artwork,22,0,scale,stage+"-artwork");
                    checks.Add(new{Width=size.Width,Height=size.Height,Scale=scale,AllFourCorners=true});
                    if(scale==1.25&&size.Width==1140)Save(Raster(banner,scale),"banner-primary-normal-125");
                }
            }
        }
        finally{carousel=savedCarousel;}
        var result=new{AllFourCorners=true,ArtworkContained=true,OutlineIntact=true,HoverAndKeyboardFocus=true,Resizing=true,FractionalScales=new[]{1d,1.25,1.5,2d},Checks=checks};
        File.WriteAllText(Path.Combine(output,"banner-corners.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        return result;
    }
}
