using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
namespace Kachalka;

public static class FeaturePoster
{
    public static string? FromSuggestion(byte[] bytes,string imdbId)
    {
        using var json=JsonDocument.Parse(bytes);
        if(!json.RootElement.TryGetProperty("d",out var rows)||rows.ValueKind!=JsonValueKind.Array)return null;
        foreach(var row in rows.EnumerateArray())
        {
            if(!row.TryGetProperty("id",out var id)||id.GetString()!=imdbId||!row.TryGetProperty("i",out var image)||
               !image.TryGetProperty("imageUrl",out var url)||!Uri.TryCreate(url.GetString(),UriKind.Absolute,out var uri)||
               uri.Scheme!="https"||uri.Host!="m.media-amazon.com"||uri.UserInfo.Length>0||!uri.AbsolutePath.StartsWith("/images/M/",StringComparison.Ordinal))continue;
            return Regex.Replace(uri.GetLeftPart(UriPartial.Path),@"\._V1_.*\.jpg$","._V1_QL85_UX1000_.jpg");
        }
        return null;
    }
}

public partial class MainWindow
{
    readonly Dictionary<string,Task<string?>> featurePosters=[];
    sealed class FeaturePosterLookup
    {
        public readonly CancellationTokenSource Request=new(TimeSpan.FromSeconds(6));
        public Task<string?> Task=null!;
        public int Subscribers;
    }
    readonly Dictionary<string,FeaturePosterLookup> featurePosterLookups=[];
    async Task<string?> FeaturePosterUrl(string id,string query,CancellationToken ct,SourceClient? client=null)
    {
        ct.ThrowIfCancellationRequested();
        if(featurePosters.TryGetValue(id,out var cached)&&cached.IsCompletedSuccessfully)return await cached.WaitAsync(ct);
        if(!featurePosterLookups.TryGetValue(id,out var lookup)||lookup.Request.IsCancellationRequested)
        {
            lookup=new();featurePosterLookups[id]=lookup;var token=lookup.Request.Token;
            async Task<string?> Find()
            {
                try
                {
                    var prefix=query.Length>0&&char.IsAsciiLetter(query[0])?char.ToLowerInvariant(query[0]):'x';
                    var bytes=await (client??sourceClient).Read(new Uri("https://v3.sg.media-imdb.com/suggestion/"+prefix+"/"+Uri.EscapeDataString(query)+".json"),256*1024,token);
                    return await Task.Run(()=>FeaturePoster.FromSuggestion(bytes,id),token);
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch{return null;}
            }
            lookup.Task=Find();
        }
        lookup.Subscribers++;
        try
        {
            var url=await lookup.Task.WaitAsync(ct);ct.ThrowIfCancellationRequested();
            if(featurePosters.Count>=16&&!featurePosters.ContainsKey(id))featurePosters.Remove(featurePosters.Keys.First());
            featurePosters[id]=Task.FromResult(url);return url;
        }
        finally
        {
            if(--lookup.Subscribers==0)
            {
                if(featurePosterLookups.TryGetValue(id,out var current)&&ReferenceEquals(current,lookup))featurePosterLookups.Remove(id);
                // A shared lookup survives one banner leaving the viewport;
                // cancel it only once every interested banner has left.
                if(!lookup.Task.IsCompleted)lookup.Request.Cancel();lookup.Request.Dispose();
            }
        }
    }
    async Task ImproveFeaturePoster(Image image,MediaItem item,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var saved=BundledCatalog.Search(item.Section,item.Title).FirstOrDefault(x=>x.Id==item.Id);
        var id=item.ImdbId??saved?.ImdbId;
        if(id==null||!Regex.IsMatch(id,@"^tt[0-9]{7,10}$"))return;
        var url=await FeaturePosterUrl(id,item.OriginalTitle??saved?.OriginalTitle??id,ct);
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
                    (bitmap,_)=await CoverCache.Load(path,1024*1024,ct=>sourceClient.Read(new Uri(url),1024*1024,ct),bytes=>
                    {
                        using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=900;result.StreamSource=stream;result.EndInit();result.Freeze();return result;
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
        catch{ /* The already displayed cached thumbnail remains available. */ }
    }
}
