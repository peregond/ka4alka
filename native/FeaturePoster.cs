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
    async Task ImproveFeaturePoster(Image image,MediaItem item)
    {
        var saved=BundledCatalog.Search(item.Section,item.Title).FirstOrDefault(x=>x.Id==item.Id);
        var id=item.ImdbId??saved?.ImdbId;
        if(id==null||!Regex.IsMatch(id,@"^tt[0-9]{7,10}$"))return;
        if(!featurePosters.TryGetValue(id,out var task))
        {
            async Task<string?> Find()
            {
                try
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var query=item.OriginalTitle??saved?.OriginalTitle??id;
                    var prefix=query.Length>0&&char.IsAsciiLetter(query[0])?char.ToLowerInvariant(query[0]):'x';
                    var bytes=await sourceClient.Read(new Uri("https://v3.sg.media-imdb.com/suggestion/"+prefix+"/"+Uri.EscapeDataString(query)+".json"),256*1024,timeout.Token);
                    return FeaturePoster.FromSuggestion(bytes,id);
                }
                catch{return null;}
            }
            if(featurePosters.Count>=16)featurePosters.Remove(featurePosters.Keys.First());
            featurePosters[id]=task=Find();
        }
        var url=await task;
        if(url==null||closed||!image.IsLoaded||!ReferenceEquals(image.DataContext,item))return;
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await coverSlots.WaitAsync(timeout.Token);
            try
            {
                if(!coverCache.TryGetValue(url,out var bitmap))
                {
                    var path=Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".img");
                    (bitmap,_)=await CoverCache.Load(path,1024*1024,ct=>sourceClient.Read(new Uri(url),1024*1024,ct),bytes=>
                    {
                        using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=900;result.StreamSource=stream;result.EndInit();result.Freeze();return result;
                    },timeout.Token);
                    if(coverCache.Count>=48)coverCache.Remove(coverCache.Keys.First());coverCache[url]=bitmap;
                }
                if(!closed&&image.IsLoaded&&ReferenceEquals(image.DataContext,item)){CancelCover(ObserveDownloadCover(image));image.Source=bitmap;}
            }
            finally{coverSlots.Release();}
        }
        catch{ /* The already displayed cached thumbnail remains available. */ }
    }
}
