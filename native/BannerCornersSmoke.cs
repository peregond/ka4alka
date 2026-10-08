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
            try
            {
                // A nested Grid's visual offset remains part of a direct or brush
                // capture. Detach this isolated fixture and actually arrange it
                // at the origin, retaining its production clip and children.
                if(detached)
                {
                    parent!.Child=null;element.Measure(size);element.Arrange(new Rect(size));element.UpdateLayout();
                    if(VisualTreeHelper.GetOffset(element).Length>0.001)throw new Exception("Banner artwork fixture could not be arranged at the origin.");
                }
                bitmap.Render(element);return bitmap;
            }
            finally
            {
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
            var reference=new Border{Width=element.ActualWidth,Height=element.ActualHeight,CornerRadius=new(radius),BorderThickness=new(thickness),Background=Brushes.White,BorderBrush=Brushes.White,UseLayoutRounding=true,SnapsToDevicePixels=true};
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
                    Message=message,Scale=scale,ElementType=element.GetType().Name,ElementSize=element.RenderSize.ToString(),RestoredOffset=VisualTreeHelper.GetOffset(element).ToString(),
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
        foreach(var primary in new[]{true,false})
        {
            // The fixture stays outside the window so Loaded cannot request artwork
            // or change the catalog while its sizes and interaction rings are checked.
            var item=new MediaItem(-987654300,"Проверка округления","Фильмы","Приключения",2026,"8,1","8,0","#26344E");
            var banner=FeatureBanner(item,primary);banner.UseLayoutRounding=true;banner.SnapsToDevicePixels=true;
            foreach(var size in new[]{new Size(760,260),new Size(392,235),new Size(316,205)})
            {
                banner.Width=size.Width;banner.Height=size.Height;banner.Measure(size);banner.Arrange(new Rect(size));banner.UpdateLayout();
                var image=VisualElements<Image>(banner).Single(x=>x.Tag?.ToString()=="FeaturePoster");image.Source=poster;
                var frame=(Border)banner.Content;var artwork=(Grid)frame.Child;
                var hover=(Border)(banner.Template.FindName("HoverRing",banner)??throw new Exception("Banner hover ring is missing."));
                var focus=(Border)(banner.Template.FindName("FocusRing",banner)??throw new Exception("Banner keyboard focus ring is missing."));
                if(!banner.Focusable||!banner.IsTabStop||AutomationProperties.GetName(banner)!="Открыть "+item.Title||!ReferenceEquals(banner.Tag,item))
                    throw new Exception("The whole banner must retain its accessible film action.");
                foreach(var state in new[]{"normal","hover","focus"})
                {
                    hover.Visibility=state=="hover"?Visibility.Visible:Visibility.Collapsed;
                    focus.Visibility=state=="focus"?Visibility.Visible:Visibility.Collapsed;
                    banner.UpdateLayout();
                    foreach(var scale in new[]{1d,1.25,1.5})
                    {
                        var stage=$"banner-{(primary?"primary":"secondary")}-{size.Width}-{state}-{scale}";
                        CheckContour(banner,16,1,scale,stage);
                        CheckContour(artwork,15,0,scale,stage+"-artwork");
                        checks.Add(new{Primary=primary,Width=size.Width,Height=size.Height,State=state,Scale=scale,AllFourCorners=true});
                        if(scale==1.25&&size.Width==(primary?760:392))Save(Raster(banner,scale),$"banner-{(primary?"primary":"secondary")}-{state}-125");
                    }
                }
            }
        }
        var result=new{AllFourCorners=true,ArtworkContained=true,OutlineIntact=true,HoverAndKeyboardFocus=true,Resizing=true,FractionalScales=new[]{1d,1.25,1.5},Checks=checks};
        File.WriteAllText(Path.Combine(output,"banner-corners.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        return result;
    }
}
