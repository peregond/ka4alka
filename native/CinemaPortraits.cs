using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

public enum PortraitStatus { Available, Missing, Unavailable }
public record PortraitResult(string? Url,PortraitStatus Status,string? SourceUrl=null,bool FromCache=false);

public sealed partial class CinemaPeople
{
    public record Identity(string Title,string Description,string SourceUrl,string? PhotoUrl,string? WikidataId);
    sealed record IdentityCache(Identity Profile,bool PortraitChecked,int? OriginId=null);
    sealed record IdentityResult(Identity? Profile,bool Unavailable=false,bool FromCache=false,bool PortraitChecked=false);
    static readonly SemaphoreSlim identitySlots=new(3);
    static readonly ConcurrentDictionary<string,Lazy<Task<IdentityResult>>> identityRequests=new();
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static string IdentityKey(CinemaPerson person,MediaItem? origin)=>Hash(Normalize(person.Name)+"|"+person.Role+"|"+person.PageUrl+"|"+origin?.Id);

    // Wiki's canonical Russian titles usually put the surname first. Only exact
    // name variants are queried; a first search hit is never used as identity.
    public static string[] IdentityTitles(CinemaPerson person)
    {
        var name=Regex.Replace(person.Name,@"\s+"," ").Trim();
        if(name.Length is <2 or >150||name.Contains('|'))return [];
        var names=new List<string>{name};var words=name.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(words.Length>1&&!name.Contains(','))names.Add(words[^1]+", "+string.Join(" ",words[..^1]));
        var roles=person.Role switch
        {
            "Актёры"=>new[]{"актёр","актриса"},
            "Режиссёры"=>new[]{"режиссёр","кинорежиссёр"},
            "Операторы"=>new[]{"кинооператор","оператор"},
            _=>Array.Empty<string>()
        };
        return names.Concat(names.SelectMany(title=>roles.Select(role=>title+" ("+role+")"))).Distinct().ToArray();
    }
    static string NameKey(string name)=>string.Join(" ",Normalize(Regex.Replace(name,@"\s*\([^)]*\)\s*$","")).Split(' ',StringSplitOptions.RemoveEmptyEntries).OrderBy(x=>x,StringComparer.Ordinal));
    static bool Profession(string description,string role)=>Regex.IsMatch(description,role switch
    {
        "Актёры"=>@"(?i)акт[её]р|актрис",
        "Режиссёры"=>@"(?i)режисс[её]р",
        "Операторы"=>@"(?i)кинооператор|оператор.постановщик|кинематографист",
        _=>@"(?i)акт[её]р|актрис|режисс[её]р|кинооператор"
    });
    public static Identity[] ParseIdentities(byte[] bytes,CinemaPerson person)
    {
        using var document=JsonDocument.Parse(bytes);var root=document.RootElement;
        if(root.TryGetProperty("error",out _))throw new InvalidDataException("Википедия временно не вернула сведения об участнике.");
        if(!root.TryGetProperty("query",out var query)||!query.TryGetProperty("pages",out var pages))throw new InvalidDataException("Источник не вернул страницы участников.");
        var result=new List<Identity>();
        foreach(var page in pages.EnumerateObject().Select(x=>x.Value))
        {
            if(page.TryGetProperty("missing",out _)||page.TryGetProperty("invalid",out _))continue;
            if(page.TryGetProperty("pageprops",out var properties)&&properties.TryGetProperty("disambiguation",out _))continue;
            var title=page.TryGetProperty("title",out var titleValue)?titleValue.GetString()??"":"";
            var description=page.TryGetProperty("extract",out var extract)?extract.GetString()??"":"";
            if(NameKey(title)!=NameKey(person.Name)||!Profession(description,person.Role))continue;
            var photo=page.TryGetProperty("thumbnail",out var thumbnail)&&thumbnail.ValueKind==JsonValueKind.Object&&thumbnail.TryGetProperty("source",out var source)?PhotoUrl(source.GetString()):null;
            var item=page.TryGetProperty("pageprops",out properties)&&properties.TryGetProperty("wikibase_item",out var wikibase)?wikibase.GetString():null;
            if(item==null||!Regex.IsMatch(item,@"^Q[1-9]\d*$"))item=null;
            result.Add(new(title,description,"https://ru.wikipedia.org/wiki/"+Uri.EscapeDataString(title.Replace(' ','_')),photo,item));
        }
        return result.DistinctBy(x=>x.SourceUrl).ToArray();
    }
    async Task<string?> WikidataPortrait(string item,CancellationToken ct)
    {
        var url="https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&ids="+item;
        using var document=JsonDocument.Parse(await client.Read(new Uri(url),1024*1024,ct));
        if(document.RootElement.TryGetProperty("error",out _))throw new InvalidDataException("Wikidata временно не вернула фотографию.");
        var entity=document.RootElement.GetProperty("entities").GetProperty(item);
        if(!entity.TryGetProperty("claims",out var claims)||!claims.TryGetProperty("P18",out var images))return null;
        var image=images.EnumerateArray().Where(x=>!x.TryGetProperty("rank",out var rank)||rank.GetString()!="deprecated")
            .OrderByDescending(x=>x.TryGetProperty("rank",out var rank)&&rank.GetString()=="preferred")
            .Select(x=>x.TryGetProperty("mainsnak",out var snak)&&snak.TryGetProperty("datavalue",out var data)&&data.TryGetProperty("value",out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null)
            .FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
        if(image==null)return null;
        var commons="https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url&iiurlwidth=400&titles="+Uri.EscapeDataString("File:"+image);
        using var media=JsonDocument.Parse(await client.Read(new Uri(commons),1024*1024,ct));
        if(media.RootElement.TryGetProperty("error",out _))throw new InvalidDataException("Wikimedia временно не вернула фотографию.");
        foreach(var page in media.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject().Select(x=>x.Value))
        {
            if(!page.TryGetProperty("imageinfo",out var information))continue;
            foreach(var info in information.EnumerateArray())
                if(info.TryGetProperty("thumburl",out var thumbnail)&&PhotoUrl(thumbnail.GetString()) is {} photo)return photo;
        }
        return null;
    }
    async Task<IdentityResult> FetchIdentity(CinemaPerson person,MediaItem? origin,string path)
    {
        IdentityCache? saved=null;DateTime savedAt=default;
        try
        {
            savedAt=File.GetLastWriteTimeUtc(path);
            if(File.Exists(path))saved=JsonSerializer.Deserialize<IdentityCache>(await CacheFiles.ReadAllTextAsync(path));
            if(saved?.Profile is {} profile&&(NameKey(profile.Title)!=NameKey(person.Name)||!Profession(profile.Description,person.Role)||saved.OriginId!=null&&saved.OriginId!=origin?.Id))saved=null;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){}
        if(saved!=null&&DateTime.UtcNow-savedAt<TimeSpan.FromDays(7)&&saved.PortraitChecked)
            return new(saved.Profile,FromCache:true,PortraitChecked:true);
        if(!await identitySlots.WaitAsync(TimeSpan.FromSeconds(30)))
            return new(saved?.Profile,Unavailable:true,FromCache:saved!=null,PortraitChecked:saved?.PortraitChecked??false);
        try
        {
            // A cancelled visual only stops waiting; repeated renders share this
            // bounded request and can consume its successful result afterwards.
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(18));var ct=deadline.Token;
            Identity? identity=saved?.Profile;int? resolvedOrigin=saved?.OriginId;
            if(identity==null||DateTime.UtcNow-savedAt>=TimeSpan.FromDays(7))
            {
                var titles=IdentityTitles(person);if(titles.Length==0)return new(null);
                var url="https://ru.wikipedia.org/w/api.php?action=query&format=json&redirects=1&prop=extracts%7Cpageprops%7Cpageimages&exintro=1&explaintext=1&piprop=thumbnail&pithumbsize=400&titles="+Uri.EscapeDataString(string.Join('|',titles));
                var candidates=ParseIdentities(await client.Read(new Uri(url),2*1024*1024,ct),person);
                if(candidates.Length==1){identity=candidates[0];resolvedOrigin=null;}
                else
                {
                    identity=null;
                    if(origin!=null&&candidates.Length>1)
                    {
                        var matches=new List<Identity>();
                        foreach(var candidate in candidates)
                        {
                            var parse="https://ru.wikipedia.org/w/api.php?action=parse&format=json&prop=text&redirects=1&page="+Uri.EscapeDataString(candidate.Title);
                            using var article=JsonDocument.Parse(await client.Read(new Uri(parse),4*1024*1024,ct));
                            var works=Works(article.RootElement.GetProperty("parse").GetProperty("text").GetProperty("*").GetString()??"");
                            if(works.Any(work=>work.Year==origin.Year&&Normalize(work.Title)==Normalize(origin.Title)))matches.Add(candidate);
                        }
                        if(matches.Count==1){identity=matches[0];resolvedOrigin=origin.Id;}
                    }
                }
                if(identity==null)return new(null);
            }
            bool checkedPortrait=identity.PhotoUrl!=null;
            bool portraitUnavailable=false;
            if(identity.PhotoUrl==null)
            {
                try
                {
                    var photo=identity.WikidataId==null?null:await WikidataPortrait(identity.WikidataId,ct);
                    identity=identity with{PhotoUrl=photo};checkedPortrait=true;
                }
                catch(Exception error)when(error is not OutOfMemoryException){portraitUnavailable=true;}
            }
            try{await CacheFiles.WriteAllTextAsync(path,JsonSerializer.Serialize(new IdentityCache(identity,checkedPortrait,resolvedOrigin)));}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
            return new(identity,portraitUnavailable,PortraitChecked:checkedPortrait);
        }
        catch(Exception error)when(error is not OutOfMemoryException)
        {
            // Read stale identity/photo during outages without making its
            // timestamp fresh. Transient failures never create negative caches.
            return new(saved?.Profile,Unavailable:true,FromCache:saved!=null,PortraitChecked:saved?.PortraitChecked??false);
        }
        finally{identitySlots.Release();}
    }
    async Task<IdentityResult> ResolveIdentity(CinemaPerson person,MediaItem? origin,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var key=Path.GetFullPath(Preferences.DataDir)+"|"+IdentityKey(person,origin);
        // Unambiguous identities are reusable across films. Only a namesake
        // chosen by matching film/year carries an origin constraint in the cache.
        var path=Path.Combine(Preferences.DataDir,"people",IdentityKey(person,null)+".identity.json");
        var pending=identityRequests.GetOrAdd(key,_=>new Lazy<Task<IdentityResult>>(()=>FetchIdentity(person,origin,path),LazyThreadSafetyMode.ExecutionAndPublication));
        var task=pending.Value;
        _=task.ContinueWith(_=>identityRequests.TryRemove(new KeyValuePair<string,Lazy<Task<IdentityResult>>>(key,pending)),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        return await task.WaitAsync(ct);
    }
    public async Task<PortraitResult> ResolvePortrait(CinemaPerson person,CancellationToken ct,MediaItem? origin=null)
    {
        ct.ThrowIfCancellationRequested();string? legacy=null;DateTime legacyAt=default;
        var path=Path.Combine(Preferences.DataDir,"people",Hash(person.Name)+".portrait.json");
        try
        {
            if(File.Exists(path))
            {
                legacyAt=File.GetLastWriteTimeUtc(path);legacy=PhotoUrl(JsonSerializer.Deserialize<string>(await CacheFiles.ReadAllTextAsync(path,ct)));
                if(legacy!=null&&DateTime.UtcNow-legacyAt<TimeSpan.FromDays(7))return new(legacy,PortraitStatus.Available,FromCache:true);
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){}
        var resolved=await ResolveIdentity(person,origin,ct);
        if(PhotoUrl(resolved.Profile?.PhotoUrl) is {} photo)return new(photo,PortraitStatus.Available,resolved.Profile?.SourceUrl,resolved.FromCache);
        if(resolved.Unavailable&&legacy!=null)return new(legacy,PortraitStatus.Available,resolved.Profile?.SourceUrl,true);
        return new(null,resolved.Unavailable?PortraitStatus.Unavailable:PortraitStatus.Missing,resolved.Profile?.SourceUrl,resolved.FromCache);
    }
}
