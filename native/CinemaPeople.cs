using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using HtmlAgilityPack;
namespace Kachalka;

// Professional person profiles supply biography and film identities, with
// Wikipedia as a fallback. Catalog work credits must confirm the same person.
public sealed partial class CinemaPeople(SourceClient client)
{
    static string Normalize(string text)=>Regex.Replace(text.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static string Text(HtmlNode? node)=>Regex.Replace(HtmlEntity.DeEntitize(node?.InnerText??""),@"\s+"," ").Trim();
    public record Work(string Title,int Year,string? OriginalTitle=null);
    public static bool MatchesWork(MediaItem film,Work work)
    {
        if(work.Year is <1900 or >2100||film.Year!=work.Year)return false;
        var names=new[]{work.Title,work.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>Normalize(x!)).ToHashSet(StringComparer.Ordinal);
        return names.Contains(Normalize(film.Title))||!string.IsNullOrWhiteSpace(film.OriginalTitle)&&names.Contains(Normalize(film.OriginalTitle));
    }
    public static Work[] Works(string html)
    {
        var doc=new HtmlDocument();doc.LoadHtml(html);var works=new List<Work>();
        foreach(var table in doc.DocumentNode.SelectNodes("//table[contains(@class,'wikitable')]")??Enumerable.Empty<HtmlNode>())
        {
            var heading=Text(table.SelectSingleNode(".//tr"));
            if(!heading.Contains("Год",StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(heading,@"(?i)название|фильм"))continue;
            int? year=null;
            foreach(var row in table.SelectNodes(".//tr[td]")??Enumerable.Empty<HtmlNode>())
            {
                var cells=row.SelectNodes("./td");if(cells==null)continue;
                var titles=new List<string>();
                foreach(var cell in cells)
                {
                    var value=Text(cell);var match=Regex.Match(value,@"^(?:19|20)\d{2}$");
                    if(match.Success){year=int.Parse(match.Value);continue;}
                    var link=cell.SelectSingleNode(".//a[starts-with(@href,'/wiki/') and not(contains(@href,':'))]");
                    if(link!=null)titles.Add(Text(link));
                }
                if(year.HasValue&&titles.Count>0&&titles[0].Length>1)works.Add(new(titles[0],year.Value));
            }
        }
        return works.DistinctBy(x=>(x.Title,x.Year)).ToArray();
    }
    public static string? PhotoUrl(string? value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0)return null;
        var wiki=uri.Host is "upload.wikimedia.org" or "thumb.wikimedia.org";
        var professional=uri.Host=="kino-teatr.ua"&&uri.Query.Length==0&&uri.Fragment.Length==0&&
            Regex.IsMatch(uri.AbsolutePath,@"^/public/main/persons/[A-Za-z0-9_-][A-Za-z0-9_.-]*\.(?:jpe?g|png|webp)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return wiki||professional?uri.AbsoluteUri:ZonaPhotoUrl(value);
    }
    public static string? ZonaPhotoUrl(string? value)
    {
        if(value?.Contains('%')==true||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return null;
        var zonaHost=uri.Host is "img1.zonapic.com" or "img2.zonapic.com" or "img3.zonapic.com" or "img4.zonapic.com";
        return zonaHost&&
            Regex.IsMatch(uri.AbsolutePath,@"^/images/actor/[0-9]+/[0-9]+\.(?:jpe?g|png|webp)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)?uri.AbsoluteUri:null;
    }
    public async Task<string?> Portrait(CinemaPerson person,CancellationToken ct)=>
        (await ResolvePortrait(person,ct)).Url;
    static string? ProfileIdentity(CinemaPerson person)=>string.IsNullOrWhiteSpace(person.ProfileUrl)?null:ProfessionalCinemaPeople.ProfileUrl(person.ProfileUrl)??person.ProfileUrl;
    static string? SourceIdentityId(CinemaPerson person)=>person.SourcePersonId!=null&&Regex.IsMatch(person.SourcePersonId,@"^[1-9]\d{0,11}$")?person.SourcePersonId:null;
    public static string ProfileCachePath(CinemaPerson person)
    {
        var explicitProfile=ProfileIdentity(person);var sourceId=SourceIdentityId(person);
        var identity=person.Name+"|"+person.Role+(explicitProfile==null?"":"|"+explicitProfile)+(sourceId==null?"":"|zona-person:"+sourceId);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Preferences.DataDir,"people",key+".json");
    }
    public async Task<PersonProfile> Load(CinemaPerson person,MediaItem? origin,IEnumerable<MediaItem> known,CancellationToken ct,Action<PersonProfile>? progress=null)
    {
        var context=SynchronizationContext.Current;var knownFilms=known.ToArray();
        var explicitProfile=ProfileIdentity(person);var sourceId=SourceIdentityId(person);
        var cache=ProfileCachePath(person);
        var aliases=new HashSet<string>(StringComparer.Ordinal){Normalize(person.Name)};
        PersonProfile? saved=null;var savedComplete=true;
        try
        {
            if(new FileInfo(cache).Length<=4*1024*1024)
            {
                var json=await CacheFiles.ReadAllTextAsync(cache,ct);saved=JsonSerializer.Deserialize<PersonProfile>(json);
                using var document=JsonDocument.Parse(json);
                savedComplete=!document.RootElement.TryGetProperty("FilmographyComplete",out var completed)||completed.ValueKind==JsonValueKind.True;
                if(saved?.Person is {} cachedPerson&&Normalize(cachedPerson.Name)==Normalize(person.Name)&&cachedPerson.Role==person.Role)
                {
                    var cachedExplicit=ProfileIdentity(cachedPerson);
                    var cachedSource=ProfessionalCinemaPeople.ProfileUrl(saved.SourceUrl);
                    if(sourceId!=null&&SourceIdentityId(cachedPerson)!=sourceId||explicitProfile!=null&&
                        ((cachedExplicit!=explicitProfile&&cachedSource!=explicitProfile)||
                         cachedExplicit!=null&&cachedExplicit!=explicitProfile||cachedSource!=null&&cachedSource!=explicitProfile))
                    {saved=null;}
                    else if(document.RootElement.TryGetProperty("Aliases",out var names)&&names.ValueKind==JsonValueKind.Array)
                        foreach(var name in names.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Take(20))
                            if(name.GetString() is {Length:>0 and <=150} alias)aliases.Add(Normalize(alias));
                }
                else saved=null;
            }
        }
        catch(IOException){}catch(JsonException){}catch(UnauthorizedAccessException){}
        bool MatchesCredit(CinemaPerson credit)=>aliases.Contains(Normalize(credit.Name))&&credit.Role==person.Role&&
            (explicitProfile==null||ProfileIdentity(credit)==null||ProfileIdentity(credit)==explicitProfile)&&
            (sourceId==null||SourceIdentityId(credit)==null||SourceIdentityId(credit)==sourceId);
        bool Matches(MediaItem film)=>film.People.Any(MatchesCredit);
        bool KnownMatches(MediaItem film)=>film.People.Any(credit=>MatchesCredit(credit)&&(explicitProfile==null||ProfileIdentity(credit)==explicitProfile));
        var otherKnown=knownFilms.Concat(origin==null?[]:[origin]).ToArray();
        var films=(saved?.Filmography??[]).Where(Matches).Concat(otherKnown.Where(KnownMatches)).DistinctBy(x=>x.Id).ToList();
        string biography=saved?.Description??"",source=saved?.SourceUrl??"";var resolvedPerson=person;
        var confirmed=new System.Collections.Concurrent.ConcurrentBag<MediaItem>();
        var progressGate=new object();long lastPublished=0,revision=0,delivered=0;bool pendingPublish=false,progressFinished=false;
        PersonProfile Snapshot()=>new(resolvedPerson,biography,films.Concat(confirmed).DistinctBy(x=>x.Id).ToArray(),source.Length>0?source:null);
        void Publish(bool immediately=false)
        {
            if(progress==null||ct.IsCancellationRequested)return;
            lock(progressGate)
            {
                var now=System.Diagnostics.Stopwatch.GetTimestamp();
                if(progressFinished)return;
                if(!immediately&&now-lastPublished<System.Diagnostics.Stopwatch.Frequency/5)
                {
                    if(!pendingPublish){pendingPublish=true;_=PublishLater(TimeSpan.FromSeconds((System.Diagnostics.Stopwatch.Frequency/5d-(now-lastPublished))/System.Diagnostics.Stopwatch.Frequency));}
                    return;
                }
                lastPublished=now;var profile=Snapshot();var update=++revision;
                void Deliver()
                {
                    if(ct.IsCancellationRequested)return;
                    lock(progressGate){if(update<=delivered)return;delivered=update;progress(profile);}
                }
                if(context!=null&&!ReferenceEquals(context,SynchronizationContext.Current))context.Post(_=>Deliver(),null);
                else Deliver();
            }
        }
        async Task PublishLater(TimeSpan delay)
        {
            try{await Task.Delay(delay,ct);lock(progressGate){pendingPublish=false;Publish(immediately:true);}}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        }
        async Task Save(PersonProfile profile,bool complete=false)
        {
            if(profile.Description.Length==0&&profile.Filmography.Length==0)return;
            try{Directory.CreateDirectory(Path.GetDirectoryName(cache)!);await CacheFiles.WriteAllTextAsync(cache,JsonSerializer.Serialize(new{profile.Person,profile.Description,profile.Filmography,profile.SourceUrl,Aliases=aliases.ToArray(),FilmographyComplete=complete}),ct);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        // Biography and remembered, verified films are useful immediately. They
        // must not wait for every remote filmography search to finish.
        if(biography.Length>0||films.Count>0)Publish(immediately:true);
        if(savedComplete&&saved is {Description.Length:>0,Filmography.Length:>1}&&DateTime.UtcNow-File.GetLastWriteTimeUtc(cache)<TimeSpan.FromDays(7)){progressFinished=true;return Snapshot();}
        var complete=false;var failedWorks=0;
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(75));
            var token=deadline.Token;
            ProfessionalPerson? professional=null;
            try{professional=await new ProfessionalCinemaPeople(client).Load(person,origin,token);}
            catch(Exception error)when(!token.IsCancellationRequested){ErrorLog.Write(error);}
            Work[] works;
            if(professional is {Biography.Length:>0})
            {
                resolvedPerson=professional.Person;biography=professional.Biography;source=professional.SourceUrl;works=professional.Works;
                foreach(var name in professional.Aliases.Append(professional.Person.Name))aliases.Add(Normalize(name));
                films.AddRange((saved?.Filmography??[]).Where(Matches).Concat(otherKnown.Where(KnownMatches)));
                Publish(immediately:true);await Save(Snapshot());
                try{works=works.Concat(await new ProfessionalCinemaPeople(client).Filmography(professional,token)).DistinctBy(x=>(Normalize(x.Title),x.Year,Normalize(x.OriginalTitle??""))).ToArray();}
                catch(Exception error)when(!token.IsCancellationRequested){Interlocked.Increment(ref failedWorks);ErrorLog.Write(error);}
                // A known film with only a shared name cannot identify an
                // explicit professional. Their own work list must also agree.
                films.AddRange(otherKnown.Where(film=>Matches(film)&&works.Any(work=>MatchesWork(film,work))));
                Publish();
            }
            else
            {
                var resolved=await ResolveIdentity(person,origin,token);
                var identity=resolved.Profile??throw new InvalidDataException("Нет однозначной биографии участника.");
                // A temporary primary-provider outage must not replace its
                // useful cached biography with a shorter fallback extract.
                if(biography.Length==0||!source.StartsWith("https://kino-teatr.ua/",StringComparison.Ordinal))
                {biography=identity.Description;source=identity.SourceUrl;}
                Publish(immediately:true);await Save(Snapshot());
                var parse="https://ru.wikipedia.org/w/api.php?action=parse&format=json&prop=text&redirects=1&page="+Uri.EscapeDataString(identity.Title);
                using var article=JsonDocument.Parse(await WikiRead(new Uri(parse),4*1024*1024,token));
                works=Works(article.RootElement.GetProperty("parse").GetProperty("text").GetProperty("*").GetString()??"");
            }
            var candidates=works
                .Where(work=>!films.Any(film=>MatchesWork(film,work)))
                .OrderByDescending(x=>x.Year).Take(16).ToArray();
            var catalog=new LiveCatalog(client);var index=new OnlineIndexClient(client);using var slots=new SemaphoreSlim(3);
            async Task<T> Lookup<T>(Func<CancellationToken,Task<T>> read)
            {
                using var request=CancellationTokenSource.CreateLinkedTokenSource(token);request.CancelAfter(TimeSpan.FromSeconds(8));
                return await read(request.Token).WaitAsync(request.Token);
            }
            await Task.WhenAll(candidates.Select(async work=>
            {
                await slots.WaitAsync(token);
                try
                {
                    var tried=new HashSet<int>();
                    async Task<bool> Confirm(MediaItem film)
                    {
                        if(!MatchesWork(film,work)||!tried.Add(film.Id))return false;
                        try
                        {
                            var detail=await catalog.Detail(film,token);
                            if(!MatchesWork(detail,work)||!Matches(detail))return false;
                            confirmed.Add(detail);Publish();return true;
                        }
                        catch(Exception)when(!token.IsCancellationRequested){Interlocked.Increment(ref failedWorks);return false;}
                    }
                    foreach(var film in knownFilms.Concat(saved?.Filmography??[]).Where(x=>MatchesWork(x,work)).DistinctBy(x=>x.Id))
                        if(await Confirm(film))return;
                    foreach(var section in new[]{"Фильмы","Сериалы"})
                    {
                        foreach(var film in BundledCatalog.FindWork(section,work.Title,work.Year,work.OriginalTitle))
                            if(await Confirm(film))return;
                        foreach(var query in new[]{work.Title,work.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!).DistinctBy(Normalize))
                        {
                            try
                            {
                                var indexed=await Lookup(ct=>index.Browse(section,query,1,ct));
                                foreach(var film in indexed.Where(x=>MatchesWork(x,work)))if(await Confirm(film))return;
                            }
                            catch(Exception)when(!token.IsCancellationRequested){ /* The direct catalog remains an independent discovery route. */ }
                            try
                            {
                                var direct=await Lookup(ct=>catalog.Browse(section,query,1,ct));
                                foreach(var film in direct.Where(x=>MatchesWork(x,work)))if(await Confirm(film))return;
                            }
                            catch(Exception)when(!token.IsCancellationRequested){Interlocked.Increment(ref failedWorks);}
                        }
                    }
                }
                catch(Exception)when(!ct.IsCancellationRequested){Interlocked.Increment(ref failedWorks); /* A single unavailable work does not invalidate confirmed films. */ }
                finally{slots.Release();}
                return;
            }));
            complete=!token.IsCancellationRequested&&failedWorks==0;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception error){ErrorLog.Write(error); /* Keep confirmed works and cached biography when the source is unavailable. */ }
        var result=Snapshot();Publish(immediately:true);lock(progressGate)progressFinished=true;await Save(result,complete);
        return result;
    }
}
