using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;

// The existing decoded poster is reused: this control makes no network requests.
// Frozen thumbnail results are shared by the homepage and detail page, with a
// small, bounded cache. It does not attach a live blur effect or animate frames.
sealed class BannerPlaceholder : Image
{
    static readonly Dictionary<BitmapSource,Task<BitmapSource>> Cache=[];
    static readonly object Gate=new();
    public static readonly DependencyProperty PosterSourceProperty=DependencyProperty.Register(nameof(PosterSource),typeof(BitmapSource),typeof(BannerPlaceholder),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty BackdropSourceProperty=DependencyProperty.Register(nameof(BackdropSource),typeof(ImageSource),typeof(BannerPlaceholder),new PropertyMetadata(null,Changed));
    public BitmapSource? PosterSource{get=>(BitmapSource?)GetValue(PosterSourceProperty);set=>SetValue(PosterSourceProperty,value);}
    public ImageSource? BackdropSource{get=>(ImageSource?)GetValue(BackdropSourceProperty);set=>SetValue(BackdropSourceProperty,value);}
    int generation;
    internal Task Ready {get;private set;}=Task.CompletedTask;
    public BannerPlaceholder()
    {
        Name="BlurredBannerPoster";Stretch=Stretch.Fill;IsHitTestVisible=false;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        Loaded+=(_,_)=>Refresh();Unloaded+=(_,_)=>{generation++;Source=null;};
    }
    static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs args)=>((BannerPlaceholder)sender).Refresh();
    void Refresh()
    {
        var version=++generation;Source=null;
        if(!IsLoaded||BackdropSource!=null||PosterSource is not {IsFrozen:true} poster){Ready=Task.CompletedTask;return;}
        Ready=Load(poster,version);
    }
    async Task Load(BitmapSource poster,int version)
    {
        try
        {
            var bitmap=await Prepare(poster);
            if(IsLoaded&&generation==version&&BackdropSource==null&&ReferenceEquals(PosterSource,poster))Source=bitmap;
        }
        catch{ /* The built-in gradient remains usable if a poster cannot be decoded. */ }
    }
    internal static Task<BitmapSource> Prepare(BitmapSource poster)
    {
        if(!poster.IsFrozen)throw new ArgumentException("Poster work requires an immutable bitmap.");
        lock(Gate)
        {
            if(Cache.TryGetValue(poster,out var existing))return existing;
            // Every entry holds at most a regular decoded poster and a <37 KiB
            // thumbnail. In-flight preparation is also coalesced for the title.
            if(Cache.Count>=16)Cache.Remove(Cache.Keys.First());
            var task=Task.Run<BitmapSource>(()=>
            {
                var scale=Math.Min(1,(double)PosterBlur.MaximumSide/Math.Max(poster.PixelWidth,poster.PixelHeight));
                var thumbnail=new TransformedBitmap(poster,new ScaleTransform(scale,scale));
                var converted=new FormatConvertedBitmap(thumbnail,PixelFormats.Pbgra32,null,0);
                var width=converted.PixelWidth;var height=converted.PixelHeight;var stride=width*4;
                var pixels=new byte[stride*height];converted.CopyPixels(pixels,stride,0);
                var blurred=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,PosterBlur.Apply(pixels,width,height),stride);
                blurred.Freeze();return blurred;
            });
            Cache[poster]=task;return task;
        }
    }
}
