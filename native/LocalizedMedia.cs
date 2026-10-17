using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Kachalka;

public sealed record EnglishMedia(string Language,string Title,string Description,string? Poster,string? Backdrop,string[] Genres,string[] Countries,string[] Actors,string[] Directors)
{
    public CinemaPerson[] People=>Directors.Select(name=>new CinemaPerson(name,"Режиссёры","")).Concat(Actors.Select(name=>new CinemaPerson(name,"Актёры",""))).ToArray();
    public static string? PosterUrl(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="image.tmdb.org"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,@"^/t/p/w500/[a-zA-Z0-9_-]+\.jpg$")?uri.AbsoluteUri:null;
    public static EnglishMedia? Parse(byte[] bytes,string identity,string section)
    {
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.GetProperty("id").GetString()!=identity||root.GetProperty("section").GetString()!=section)throw new InvalidDataException("Localization belongs to another title.");
        var row=root.GetProperty("item");if(row.ValueKind==JsonValueKind.Null)return null;
        var data=row.Deserialize<EnglishMedia>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(data==null||data.Language!="en"||string.IsNullOrWhiteSpace(data.Title)||data.Title.Length>300||data.Description==null||data.Description.Length>4000||data.Genres==null||data.Countries==null||data.Actors==null||data.Directors==null)throw new InvalidDataException("Invalid localization.");
        return data with{Poster=PosterUrl(data.Poster),Backdrop=FeatureBackdrop.ValidUrl(data.Backdrop),Genres=data.Genres.Take(6).ToArray(),Countries=data.Countries.Take(4).ToArray(),Actors=data.Actors.Take(12).ToArray(),Directors=data.Directors.Take(3).ToArray()};
    }
}

public partial class MainWindow
{
    Task<MediaItem> EnglishMetadata(MediaItem item,bool priority,CancellationToken ct,Action<MediaItem>? updated,bool force)
    {
        if(force)cardMetadata.Remove(item.Id);
        if(cardMetadata.TryGetValue(item.Id,out var cached)&&!cached.IsCanceled&&!cached.IsFaulted)return cached;
        if(!ct.CanBeCanceled)ct=metadataLifetime.Token;
        async Task<MediaItem> Load()
        {
            var result=await metadataScheduler.Run(priority,async()=>
            {
                var id=OnlineIndexClient.IdFor(item)??item.ImdbId;
                if(id==null)return item;
                var section=item.Section=="Сериалы"?"series":"movies";
                var path=Path.Combine(Preferences.DataDir,"localization","en",section+"-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))+".json");
                EnglishMedia? data=null;
                try{if(!force&&File.Exists(path)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromDays(7))data=EnglishMedia.Parse(await CacheFiles.ReadAllBytesAsync(path,ct),id,section);}catch(Exception)when(!ct.IsCancellationRequested){}
                if(data==null)
                {
                    var query=OnlineIndexClient.IdFor(item)!=null?"id="+Uri.EscapeDataString(id):"imdb="+Uri.EscapeDataString(id)+"&section="+section;
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(12));
                    var bytes=await onlineIndex.ReadSite("api/localized-media?language=en&"+query,128*1024,deadline.Token,bytes=>EnglishMedia.Parse(bytes,id,section),restSites:false);
                    data=EnglishMedia.Parse(bytes,id,section);
                    if(data!=null)try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);await CacheFiles.WriteAllBytesAsync(path,bytes,ct);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
                return item with{English=data};
            },ct);
            ct.ThrowIfCancellationRequested();updated?.Invoke(result);return result;
        }
        var task=Load();if(cardMetadata.Count>=100)cardMetadata.Remove(cardMetadata.Keys.First());cardMetadata[item.Id]=task;return task;
    }
}
