using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Kachalka;

public partial class MainWindow
{
    async Task<object> CheckBannerPlaceholder(string output)
    {
        var bytes=new byte[64*96*4];
        for(var y=0;y<96;y++)for(var x=0;x<64;x++)
        {
            var at=(y*64+x)*4;bytes[at]=(byte)(x%2*200);bytes[at+1]=(byte)(y%2*220);bytes[at+2]=120;bytes[at+3]=255;
        }
        var poster=BitmapSource.Create(64,96,96,96,PixelFormats.Pbgra32,null,bytes,64*4);poster.Freeze();
        var wide=BitmapSource.Create(2,2,96,96,PixelFormats.Pbgra32,null,Enumerable.Repeat(new byte[]{60,130,170,255},4).SelectMany(x=>x).ToArray(),8);wide.Freeze();
        var source=new Image{Source=poster};var landscape=new Image();
        var artwork=new Grid{Width=480,Height=240};AddBannerArtwork(artwork,landscape,source);
        var host=new Window{Owner=this,Content=artwork,Width=500,Height=290,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000};
        var placeholder=VisualElements<BannerPlaceholder>(artwork).Single();
        var layer=VisualElements<Grid>(artwork).Single(x=>x.Name=="BannerPlaceholder");
        async Task Ready(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await placeholder.Ready;artwork.UpdateLayout();}
        void Save(string name)
        {
            var bitmap=new RenderTargetBitmap(480,240,96,96,PixelFormats.Pbgra32);bitmap.Render(artwork);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        try
        {
            host.Show();await Ready();
            if(placeholder.Source is not BitmapSource {IsFrozen:true} small||small.PixelWidth>96||small.PixelHeight>96||layer.Visibility!=Visibility.Visible)throw new Exception("The banner does not show its bounded preblurred poster.");
            if(!ReferenceEquals(source.Source,poster))throw new Exception("Preparing the banner changed its foreground poster.");
            var first=BannerPlaceholder.Prepare(poster);var second=BannerPlaceholder.Prepare(poster);
            if(!ReferenceEquals(first,second)||!ReferenceEquals(await first,small))throw new Exception("Banner preparation is not cached and coalesced.");
            if(VisualElements<UIElement>(artwork).Any(x=>x.Effect!=null))throw new Exception("Banner placeholder uses a live rendering effect.");
            Save("banner-placeholder-blurred");
            landscape.Source=wide;await Ready();
            if(layer.Visibility!=Visibility.Collapsed||placeholder.Source!=null)throw new Exception("The placeholder remains on top of the loaded landscape.");
            Save("banner-placeholder-loaded");
            landscape.Source=null;source.Source=wide;landscape.Source=poster;await Ready();
            if(placeholder.Source!=null)throw new Exception("Late placeholder preparation replaced the landscape.");
            landscape.Source=null;await Ready();
            if(!ReferenceEquals(placeholder.Source,await BannerPlaceholder.Prepare(wide)))throw new Exception("Switching a slide leaves the previous poster behind.");
            source.Source=null;await Ready();
            if(placeholder.Source!=null||layer.Visibility!=Visibility.Visible||layer.Children.OfType<Border>().First().Background is not GradientBrush {IsFrozen:true})throw new Exception("Missing posters do not keep the built-in gradient.");
            Save("banner-placeholder-gradient");
            source.Source=poster;host.Content=null;await Ready();
            if(placeholder.Source!=null)throw new Exception("Unloading the banner leaves stale asynchronous artwork.");
        }
        finally{host.Close();}
        var result=new{PreblurredPoster=true,FrozenThumbnail=true,MaximumSide=96,CacheCoalesced=true,NoLiveEffects=true,ForegroundPosterUnchanged=true,LandscapeReplacesPlaceholder=true,LateResultsRejected=true,SlideSwitchResets=true,MissingPosterGradient=true,UnloadRejectsArtwork=true};
        File.WriteAllText(Path.Combine(output,"banner-placeholder-checks.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result;
    }
}
