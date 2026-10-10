using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
namespace Kachalka;

public partial class MainWindow
{
    readonly Dictionary<string,(string? Url,DateTime Expires)> featurePosters=[];
    sealed class FeaturePosterLookup
    {
        public readonly CancellationTokenSource Request=new(TimeSpan.FromSeconds(15));
        public Task<string?> Task=null!;
        public int Subscribers;
        public DateTime Expires;
    }
    readonly Dictionary<string,FeaturePosterLookup> featurePosterLookups=[];
    DateTime backdropRetryAfterUtc;
    async Task<string?> FeaturePosterUrl(string id,CancellationToken ct,SourceClient? client=null)
    {
        ct.ThrowIfCancellationRequested();
        if(featurePosters.TryGetValue(id,out var cached)&&cached.Expires>DateTime.UtcNow)return cached.Url;
        if(!featurePosterLookups.TryGetValue(id,out var lookup)||lookup.Request.IsCancellationRequested)
        {
            lookup=new();featurePosterLookups[id]=lookup;var token=lookup.Request.Token;
            async Task<string?> Find()
            {
                var path=Path.Combine(Preferences.DataDir,"covers","backdrop-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))+".json");
                byte[]? saved=null;
                try
                {
                    saved=await Task.Run(()=>File.Exists(path)&&new FileInfo(path).Length<=32768?File.ReadAllBytes(path):null,token);
                    if(saved!=null)
                    {
                        var value=FeatureBackdrop.FromResponse(saved,id);
                        var age=DateTime.UtcNow-File.GetLastWriteTimeUtc(path);
                        if(age<(value!=null?TimeSpan.FromDays(7):TimeSpan.FromHours(1))){lookup.Expires=File.GetLastWriteTimeUtc(path).Add(value!=null?TimeSpan.FromDays(7):TimeSpan.FromHours(1));return value;}
                    }
                }
                catch(Exception)when(!token.IsCancellationRequested){saved=null;}
                string? Retained()
                {
                    if(saved==null||DateTime.UtcNow-File.GetLastWriteTimeUtc(path)>=TimeSpan.FromDays(30))return null;
                    var value=FeatureBackdrop.FromResponse(saved,id);if(value!=null)lookup.Expires=File.GetLastWriteTimeUtc(path).AddDays(30);return value;
                }
                if(client==null&&DateTime.UtcNow<backdropRetryAfterUtc)
                {
                    if(Retained() is {} retained)return retained;
                    throw new IOException("Источник фонов временно недоступен.");
                }
                try
                {
                    var request="api/backdrop?id="+Uri.EscapeDataString(id);
                    var bytes=client!=null?await client.Read(new Uri(OnlineIndexClient.PublishedSite,request),32768,token):await onlineIndex.ReadSite(request,32768,token);
                    var value=await Task.Run(()=>FeatureBackdrop.FromResponse(bytes,id),token);
                    lookup.Expires=DateTime.UtcNow.Add(value!=null?TimeSpan.FromDays(7):TimeSpan.FromHours(1));
                    try{await Task.Run(()=>Directory.CreateDirectory(Path.GetDirectoryName(path)!),token);await CacheFiles.WriteAllBytesAsync(path,bytes,token);}catch(IOException){}catch(UnauthorizedAccessException){}
                    return value;
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch
                {
                    if(client==null)backdropRetryAfterUtc=DateTime.UtcNow.AddSeconds(30);
                    if(Retained() is {} retained)return retained;
                    throw;
                }
            }
            lookup.Task=Find();
        }
        lookup.Subscribers++;
        try
        {
            var url=await lookup.Task.WaitAsync(ct);ct.ThrowIfCancellationRequested();
            if(featurePosters.Count>=16&&!featurePosters.ContainsKey(id))featurePosters.Remove(featurePosters.Keys.First());
            featurePosters[id]=(url,lookup.Expires);return url;
        }
        finally
        {
            if(--lookup.Subscribers==0)
            {
                if(featurePosterLookups.TryGetValue(id,out var current)&&ReferenceEquals(current,lookup))featurePosterLookups.Remove(id);
                if(!lookup.Task.IsCompleted)lookup.Request.Cancel();lookup.Request.Dispose();
            }
        }
    }
    async Task ImproveFeaturePoster(Image image,MediaItem item,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(prefs.LiteMode)return;
        var id=OnlineIndexClient.IdFor(item);
        if(id==null)return;
        var url=await FeaturePosterUrl(id,ct);
        ct.ThrowIfCancellationRequested();
        if(url==null||closing||closed||!image.IsLoaded||!ReferenceEquals(image.DataContext,item)||CoverViewportFor(image)?.Measure(image).Near!=true)return;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(6));
            await coverSlots.WaitAsync(timeout.Token);
            try
            {
                if(closing||closed||!image.IsLoaded||!ReferenceEquals(image.DataContext,item)||CoverViewportFor(image)?.Measure(image).Near!=true)return;
                var path=Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".img");
                if(!coverCache.TryGetValue(url,out var bitmap))
                {
                    var proxy="api/backdrop-image?file="+Uri.EscapeDataString(new Uri(url).Segments.Last().Insert(0,"/"));
                    (bitmap,_)=await CoverCache.Load(path,2*1024*1024,ct=>onlineIndex.ReadSite(proxy,2*1024*1024,ct),bytes=>
                    {
                        using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=1280;result.StreamSource=stream;result.EndInit();if(!FeatureBackdrop.Landscape(result.PixelWidth,result.PixelHeight))throw new InvalidDataException("Источник вернул постер вместо широкого фона.");result.Freeze();return result;
                    },timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                    if(coverCache.Count>=48)coverCache.Remove(coverCache.Keys.First());coverCache[url]=bitmap;
                }
                else _=Task.Run(()=>CacheFiles.Touch(path),timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                if(!closing&&!closed&&image.IsLoaded&&ReferenceEquals(image.DataContext,item)&&CoverViewportFor(image)?.Measure(image).Near==true){CancelCover(ObserveDownloadCover(image));image.Source=bitmap;}
            }
            finally{coverSlots.Release();}
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch{ /* Keep the preblurred poster or built-in gradient if the backdrop is unavailable. */ }
    }
}
