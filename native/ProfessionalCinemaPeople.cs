using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Kachalka;

public record ProfessionalPerson(CinemaPerson Person,string Biography,string? PhotoUrl,string SourceUrl,string SourceName,CinemaPeople.Work[] Works,string[] Aliases,bool FromCache=false,string? FilmographyPageUrl=null);

// Kino-Teatr's public Russian surname search does not need an API key. A search
// hit is only a candidate: its own name, profession and identity are checked.
public sealed class ProfessionalCinemaPeople(SourceClient client)
{
    const string Host="kino-teatr.ua";
    const int MaximumBytes=2*1024*1024;
    static readonly SemaphoreSlim slots=new(3,3);
    static readonly ConcurrentDictionary<string,Lazy<Task<ProfessionalPerson?>>> requests=new();
    static readonly ConcurrentDictionary<string,DateTime> missing=new();
    static readonly object formSync=new();
    static Task<Uri>? formRequest;
    static DateTime formExpires;
    static string Normalize(string value)=>Regex.Replace(value.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static string NameKey(string value)=>string.Join(' ',Normalize(value).Split(' ',StringSplitOptions.RemoveEmptyEntries).OrderBy(x=>x,StringComparer.Ordinal));
    static string IdentitySuffix(CinemaPerson person)=>string.IsNullOrWhiteSpace(person.SourcePersonId)&&string.IsNullOrWhiteSpace(person.OriginalName)?"":"|source:"+person.SourcePersonId+"|original:"+NameKey(person.OriginalName??"");
    static string Text(HtmlNode? node)=>Regex.Replace(HtmlEntity.DeEntitize(node?.InnerText??""),@"\s+"," ").Trim();
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static HtmlDocument Html(byte[] bytes){var document=new HtmlDocument();document.LoadHtml(Encoding.UTF8.GetString(bytes));return document;}
    static Uri? Trusted(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)||!Uri.TryCreate(new Uri("https://"+Host),HtmlEntity.DeEntitize(value),out var uri)||uri.Scheme!="https"||uri.Host!=Host||!uri.IsDefaultPort||uri.UserInfo.Length>0)return null;
        return uri;
    }
    public static string? ProfileUrl(string? value)
    {
        var uri=Trusted(value);
        return uri!=null&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,@"^/(?:ru/|uk/)?person/[a-z0-9-]+-\d+\.phtml$",RegexOptions.IgnoreCase)?uri.AbsoluteUri:null;
    }
    public static string? PhotoUrl(string? value)
    {
        var uri=Trusted(value);
        return uri!=null&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,@"^/public/main/persons/[a-z0-9_.-]+\.(?:jpe?g|png|webp)$",RegexOptions.IgnoreCase)?uri.AbsoluteUri:null;
    }
    static string? FilmographyUrl(HtmlDocument document,string profileUrl)
    {
        var id=Regex.Match(new Uri(profileUrl).AbsolutePath,@"-(\d+)\.phtml$").Groups[1].Value;
        foreach(var anchor in document.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            var uri=Trusted(anchor.GetAttributeValue("href",""));
            if(uri!=null&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,@"^/(?:ru/|uk/)?person_films/[a-z0-9-]+-"+Regex.Escape(id)+@"\.phtml$",RegexOptions.IgnoreCase))return uri.AbsoluteUri;
        }
        return null;
    }
    static bool Role(string text,string role)=>Regex.IsMatch(text,role switch
    {
        "Актёры"=>@"(?i)\bакт[её]р|\bактрис|\bactor\b|\bactress\b",
        "Режиссёры"=>@"(?i)режисс[её]р|\bdirector\b(?!\s+of\s+photography)",
        "Операторы"=>@"(?i)кинооператор|\bоператор\b|\bcinematographer\b|director of photography|\bcamera operator\b|\bcameraman\b",
        _=>@"(?!)"
    });
    static string Biography(HtmlDocument document)
    {
        var structured=document.DocumentNode.SelectSingleNode("//*[@itemprop='description' and not(self::meta)]");
        if(structured!=null&&Text(structured).Length>30)return Text(structured);
        var heading=(document.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4")??Enumerable.Empty<HtmlNode>()).FirstOrDefault(x=>Normalize(Text(x)) is "биография" or "біографія");
        if(heading?.ParentNode==null)return "";
        var paragraphs=new List<string>();
        foreach(var node in heading.ParentNode.ChildNodes.SkipWhile(x=>x!=heading).Skip(1))
        {
            if(node.Name is "h1" or "h2" or "h3" or "h4")break;
            if(node.Name is "script" or "style" or "form")continue;
            var value=Text(node);if(value.Length==0)continue;
            if(value.Contains("Фильмография",StringComparison.OrdinalIgnoreCase)||value.Contains("Комментарии",StringComparison.OrdinalIgnoreCase))break;
            paragraphs.Add(value);if(paragraphs.Count>=3||string.Join(' ',paragraphs).Length>=1400)break;
        }
        return string.Join(' ',paragraphs);
    }
    static string Excerpt(string text)
    {
        if(text.Length<=320)return text;
        var end=text.LastIndexOfAny(['.','!','?'],Math.Min(319,text.Length-1));
        if(end<80)end=text.LastIndexOf(' ',319);
        return text[..Math.Max(1,end+1)].Trim()+"…";
    }
    static string[] Names(HtmlDocument document)
    {
        var heading=document.DocumentNode.SelectSingleNode("//h1");if(heading?.ParentNode==null)return [];
        var names=new List<string>{Text(heading)};
        foreach(var node in heading.ParentNode.Descendants().Take(35))
        {
            var value=Text(node);
            if(value.Length is >2 and <100&&Regex.IsMatch(value,@"^[A-ZÀ-Ž][\p{L}'’.-]+(?:\s+[A-ZÀ-Ž][\p{L}'’.-]+){1,5}$"))names.Add(value);
        }
        foreach(var node in document.DocumentNode.SelectNodes("//*[@itemprop='alternateName']")??Enumerable.Empty<HtmlNode>())
        {
            var value=Text(node);if(value.Length is >2 and <100)names.Add(value);
        }
        return names.Where(x=>x.Length is >1 and <150).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static CinemaPeople.Work[] Works(byte[] bytes,string? role=null)
    {
        var document=Html(bytes);var works=new List<CinemaPeople.Work>();
        if(!Normalize(Text(document.DocumentNode.SelectSingleNode("//h1"))).StartsWith("фильмография ",StringComparison.Ordinal))return [];
        foreach(var anchor in document.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            var uri=Trusted(anchor.GetAttributeValue("href",""));
            if(uri==null||!Regex.IsMatch(uri.AbsolutePath,@"^/(?:ru/|uk/)?film/[a-z0-9-]+-\d+\.phtml$",RegexOptions.IgnoreCase))continue;
            var heading=Text(anchor.SelectSingleNode("preceding::*[self::h1 or self::h2 or self::h3 or self::h4][1]"));
            if(role==null?!new[]{"Актёры","Режиссёры","Операторы"}.Any(x=>Role(heading,x)):!Role(heading,role))continue;
            var title=Text(anchor);if(title.Length is <2 or >200)continue;
            foreach(var parent in anchor.Ancestors().Take(4))
            {
                var value=Text(parent);if(value.Length>900)break;
                var years=Regex.Matches(value,@"(?<!\d)(?:19|20)\d{2}(?!\d)").Select(x=>int.Parse(x.Value)).Distinct().ToArray();
                if(years.Length!=1)continue;
                var original=Text(parent.SelectSingleNode(".//span[contains(@class,'uk-text-muted')]"));
                var originalMatch=Regex.Match(original,@"^(.+?)\s*\((?:19|20)\d{2}\)$");
                works.Add(new(title,years[0],originalMatch.Success?originalMatch.Groups[1].Value.Trim():null));break;
            }
        }
        return works.DistinctBy(x=>(Normalize(x.Title),x.Year)).Take(100).ToArray();
    }
    static JsonElement? StructuredPerson(HtmlDocument document,string sourceUrl)
    {
        var id=Regex.Match(new Uri(sourceUrl).AbsolutePath,@"-(\d+)\.phtml$").Groups[1].Value;
        foreach(var script in document.DocumentNode.SelectNodes("//script[@type='application/ld+json']")??Enumerable.Empty<HtmlNode>())
        {
            try
            {
                using var json=JsonDocument.Parse(script.InnerText);
                var root=json.RootElement;
                var nodes=root.ValueKind==JsonValueKind.Array?root.EnumerateArray().ToArray():root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("@graph",out var graph)&&graph.ValueKind==JsonValueKind.Array?graph.EnumerateArray().ToArray():[root];
                foreach(var node in nodes)
                {
                    if(node.ValueKind!=JsonValueKind.Object||!node.TryGetProperty("@type",out var type)||!(type.ValueKind==JsonValueKind.String?type.GetString()=="Person":type.ValueKind==JsonValueKind.Array&&type.EnumerateArray().Any(x=>x.ValueKind==JsonValueKind.String&&x.GetString()=="Person")))continue;
                    var profile=node.TryGetProperty("url",out var url)&&url.ValueKind==JsonValueKind.String?ProfileUrl(url.GetString()):null;
                    if(profile!=null&&Regex.Match(new Uri(profile).AbsolutePath,@"-(\d+)\.phtml$").Groups[1].Value==id)return node.Clone();
                }
            }
            catch(JsonException){}
        }
        return null;
    }
    static string[] Strings(JsonElement? node,string field)
    {
        if(node is not JsonElement value||!value.TryGetProperty(field,out var property))return [];
        return property.ValueKind==JsonValueKind.String?[property.GetString()??""]:property.ValueKind==JsonValueKind.Array?property.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()??"").ToArray():[];
    }
    static string[] Aliases(HtmlDocument document,JsonElement? structured)=>(structured==null?Names(document):Strings(structured,"name").Concat(Strings(structured,"alternateName"))).Where(x=>x.Length is >1 and <150).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static ProfessionalPerson? Parse(byte[] bytes,CinemaPerson person,string sourceUrl)
    {
        sourceUrl=ProfileUrl(sourceUrl)??throw new InvalidDataException("Неверный адрес кинопрофиля.");
        var document=Html(bytes);var structured=StructuredPerson(document,sourceUrl);
        var aliases=Aliases(document,structured);
        var biography=Strings(structured,"description").FirstOrDefault()??Biography(document);
        var introduction=Regex.Split(biography,@"(?<=[.!?])\s+").FirstOrDefault()??"";
        var jobs=Strings(structured,"jobTitle").Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        if(!aliases.Any(x=>NameKey(x)==NameKey(person.Name))||!Role(jobs.Length>0?string.Join(' ',jobs):introduction,person.Role))return null;
        var originals=aliases.Where(x=>Regex.IsMatch(x,@"[A-Za-z]")).ToArray();
        if(!string.IsNullOrWhiteSpace(person.OriginalName)&&originals.Length>0&&!originals.Any(x=>NameKey(x)==NameKey(person.OriginalName)))return null;
        var photo=Strings(structured,"image").Select(PhotoUrl).FirstOrDefault(x=>x!=null)??
            (document.DocumentNode.SelectNodes("//img[@alt]")??Enumerable.Empty<HtmlNode>())
                .Where(x=>aliases.Any(name=>Normalize(x.GetAttributeValue("alt",""))=="персона "+Normalize(name)))
                .Select(x=>PhotoUrl(x.GetAttributeValue("src",x.GetAttributeValue("data-src","")))).FirstOrDefault(x=>x!=null);
        var profilePerson=person with{ProfileUrl=sourceUrl};
        return new(profilePerson,Excerpt(biography),photo,sourceUrl,"Kino-Teatr.ua",Works(bytes,person.Role),aliases,FilmographyPageUrl:FilmographyUrl(document,sourceUrl));
    }
    async Task<Uri> SearchForm(CancellationToken ct)
    {
        Task<Uri> task;
        lock(formSync)
        {
            if(formRequest==null||formExpires<DateTime.UtcNow||formRequest.IsFaulted||formRequest.IsCanceled)
            {
                formExpires=DateTime.UtcNow.AddMinutes(10);formRequest=LoadForm();
            }
            task=formRequest;
        }
        return await task.WaitAsync(ct);
        async Task<Uri> LoadForm()
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var document=Html(await client.Read(new Uri("https://"+Host+"/persons.phtml"),MaximumBytes,deadline.Token));
            var input=document.DocumentNode.SelectSingleNode("//form[translate(@method,'get','GET')='GET']//input[@name='lastname']");
            var form=input?.Ancestors("form").FirstOrDefault();var target=Trusted(form?.GetAttributeValue("action",""));
            if(target==null||target.Query.Length>0||target.Fragment.Length>0||!target.AbsolutePath.Contains("/persons/",StringComparison.Ordinal))throw new InvalidDataException("Источник не предоставил поиск персон.");
            return target;
        }
    }
    static string Surname(string query)
    {
        query=Regex.Replace(query,@"\s+"," ").Trim();
        return query.Contains(',')?query.Split(',')[0].Trim():query.Split(' ',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"";
    }
    async Task<string[]> Candidates(string query,CancellationToken ct)
    {
        if(query.Length is <2 or >150)return [];
        var target=await SearchForm(ct);var surname=Surname(query);
        var tokens=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var pages=new SortedDictionary<int,Uri>();var visited=new HashSet<int>{1};
        var document=await ReadPage(new Uri(target.AbsoluteUri+"?lastname="+Uri.EscapeDataString(surname)));
        for(var count=0;count<3;count++)
        {
            var matches=(document.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
                .Where(x=>tokens.All(token=>Normalize(Text(x)).Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains(token)))
                .Select(x=>ProfileUrl(x.GetAttributeValue("href",""))).OfType<string>().Distinct().Take(13).ToArray();
            if(matches.Length>0)return matches;
            DiscoverPages(document);
            var next=pages.FirstOrDefault(x=>!visited.Contains(x.Key));
            if(next.Value==null)return [];
            if(count==2)throw new InvalidDataException("Источник не подтвердил отсутствие персоны в ограниченной выборке.");
            visited.Add(next.Key);document=await ReadPage(next.Value);
        }
        return [];
        async Task<HtmlDocument> ReadPage(Uri uri)
        {
            var result=Html(await client.Read(uri,MaximumBytes,ct));
            if(Normalize(Text(result.DocumentNode.SelectSingleNode("//h1")))!="персоны в кино")throw new InvalidDataException("Источник временно не предоставил список персон.");
            return result;
        }
        void DiscoverPages(HtmlDocument page)
        {
            var prefix=target.AbsolutePath[..target.AbsolutePath.IndexOf("/persons/",StringComparison.Ordinal)]+"/persons/lastname/";
            foreach(var anchor in page.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
            {
                var uri=Trusted(anchor.GetAttributeValue("href",""));if(uri==null||uri.Query.Length>0||uri.Fragment.Length>0)continue;
                var match=Regex.Match(uri.AbsolutePath,"^"+Regex.Escape(prefix)+@"([^/]+)/page/(\d+)\.phtml$",RegexOptions.IgnoreCase);
                if(!match.Success||!string.Equals(Uri.UnescapeDataString(match.Groups[1].Value),surname,StringComparison.OrdinalIgnoreCase)||!int.TryParse(match.Groups[2].Value,out var number)||number is <2 or >100)continue;
                pages.TryAdd(number,uri);
            }
        }
    }
    public async Task<CinemaPerson[]> SearchPeople(string query,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(14));
        var urls=await Candidates(query,deadline.Token);var result=new ConcurrentBag<CinemaPerson>();var unavailable=0;
        await Task.WhenAll(urls.Take(12).Select(async url=>
        {
            var entered=false;
            try
            {
                await slots.WaitAsync(deadline.Token);entered=true;
                var bytes=await client.Read(new Uri(url),MaximumBytes,deadline.Token);var document=Html(bytes);var structured=StructuredPerson(document,url);
                if(structured==null&&document.DocumentNode.SelectSingleNode("//h1")==null)throw new InvalidDataException("Источник временно не предоставил кинопрофиль.");
                var aliases=Aliases(document,structured);
                var tokens=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
                var name=aliases.FirstOrDefault(x=>tokens.All(token=>Normalize(x).Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains(token)));
                if(name==null)return;
                foreach(var role in new[]{"Актёры","Режиссёры","Операторы"})
                {
                    var person=new CinemaPerson(name,role,"",url);if(Parse(bytes,person,url)!=null)result.Add(person);
                }
            }
            catch(OperationCanceledException)when(!ct.IsCancellationRequested){Interlocked.Increment(ref unavailable);}
            catch(HttpRequestException){Interlocked.Increment(ref unavailable);}
            catch(InvalidDataException){Interlocked.Increment(ref unavailable);}
            finally{if(entered)slots.Release();}
        }));
        ct.ThrowIfCancellationRequested();
        if(result.IsEmpty&&unavailable>0)throw new HttpRequestException("Источник временно не предоставил сведения о найденных персонах.");
        return result.DistinctBy(x=>(x.ProfileUrl,x.Role)).OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Role,StringComparer.Ordinal).Take(20).ToArray();
    }
    public async Task<ProfessionalPerson?> Load(CinemaPerson person,MediaItem? origin,CancellationToken ct,bool forceRefresh=false)
    {
        var identityKey=Preferences.DataDir+"|"+NameKey(person.Name)+"|"+person.Role+"|"+person.ProfileUrl+"|"+origin?.Id+IdentitySuffix(person);
        var key=Hash(identityKey+"|force:"+forceRefresh);
        if(!forceRefresh&&missing.TryGetValue(key,out var absent)&&absent>DateTime.UtcNow)return null;
        var pending=requests.GetOrAdd(key,_=>new(()=>Lookup(person,origin,forceRefresh),LazyThreadSafetyMode.ExecutionAndPublication));var task=pending.Value;
        _=task.ContinueWith(_=>requests.TryRemove(new KeyValuePair<string,Lazy<Task<ProfessionalPerson?>>>(key,pending)),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        var result=await task.WaitAsync(ct);
        if(result==null){if(missing.Count>=500)missing.Clear();missing[key]=DateTime.UtcNow.AddHours(1);}
        else{missing.TryRemove(Hash(identityKey+"|force:False"),out _);missing.TryRemove(Hash(identityKey+"|force:True"),out _);}
        return result;
    }
    public static string CachePath(CinemaPerson person,MediaItem? origin)=>Path.Combine(Preferences.DataDir,"people",Hash(NameKey(person.Name)+"|"+person.Role+"|"+person.ProfileUrl+"|"+origin?.Id+IdentitySuffix(person))+".professional.json");
    async Task<ProfessionalPerson?> Lookup(CinemaPerson person,MediaItem? origin,bool forceRefresh)
    {
        var path=CachePath(person,origin);
        ProfessionalPerson? saved=null;
        try
        {
            if(File.Exists(path))saved=JsonSerializer.Deserialize<ProfessionalPerson>(await CacheFiles.ReadAllTextAsync(path));
            if(saved!=null&&(ProfileUrl(saved.SourceUrl)==null||saved.Person==null||saved.Person.Role!=person.Role||saved.Biography==null||saved.Aliases==null||saved.Works==null||!saved.Aliases.Any(x=>x!=null&&NameKey(x)==NameKey(person.Name))))saved=null;
            if(saved!=null)saved=saved with{PhotoUrl=PhotoUrl(saved.PhotoUrl)};
            if(!forceRefresh&&saved!=null&&File.GetLastWriteTimeUtc(path)>DateTime.UtcNow-(saved.PhotoUrl==null?TimeSpan.FromHours(1):TimeSpan.FromDays(7)))return saved with{Person=person with{ProfileUrl=saved.SourceUrl},FromCache=true};
        }
        catch(IOException){}catch(JsonException){}catch(UnauthorizedAccessException){}
        using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(24));await slots.WaitAsync(wait.Token);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(18));var ct=deadline.Token;
            var direct=ProfileUrl(person.ProfileUrl);
            var urls=direct!=null?[direct]:await Candidates(person.Name,ct);
            if(urls.Length>12)return null;
            var profiles=new List<ProfessionalPerson>();
            foreach(var url in urls)
            {
                var bytes=await client.Read(new Uri(url),MaximumBytes,ct);var document=Html(bytes);
                if(StructuredPerson(document,url)==null&&document.DocumentNode.SelectSingleNode("//h1")==null)throw new InvalidDataException("Источник временно не предоставил кинопрофиль.");
                var result=Parse(bytes,person,url);if(result==null)continue;
                profiles.Add(result);
            }
            if(profiles.Count>1&&origin!=null)
                for(var index=0;index<profiles.Count;index++)profiles[index]=profiles[index] with{Works=await Filmography(profiles[index],ct)};
            var selected=profiles.Count==1?profiles[0]:origin==null?null:profiles.Where(x=>x.Works.Any(work=>work.Year==origin.Year&&(Normalize(work.Title)==Normalize(origin.Title)||!string.IsNullOrWhiteSpace(origin.OriginalTitle)&&Normalize(work.Title)==Normalize(origin.OriginalTitle)||!string.IsNullOrWhiteSpace(work.OriginalTitle)&&(Normalize(work.OriginalTitle)==Normalize(origin.OriginalTitle??origin.Title)||Normalize(work.OriginalTitle)==Normalize(origin.Title))))).Take(2).ToArray() is [var match]?match:null;
            if(selected==null)return null;
            try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);await CacheFiles.WriteAllTextAsync(path,JsonSerializer.Serialize(selected),ct);}catch(IOException){}catch(UnauthorizedAccessException){}
            return selected;
        }
        catch when(saved!=null){return saved with{Person=person with{ProfileUrl=saved.SourceUrl},FromCache=true};}
        finally{slots.Release();}
    }
    public async Task<CinemaPeople.Work[]> Filmography(ProfessionalPerson profile,CancellationToken ct)
    {
        var source=ProfileUrl(profile.SourceUrl);var uri=Trusted(profile.FilmographyPageUrl);
        if(source==null||uri==null||uri.Query.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("Источник не предоставил фильмографию.");
        var id=Regex.Match(new Uri(source).AbsolutePath,@"-(\d+)\.phtml$").Groups[1].Value;
        if(!Regex.IsMatch(uri.AbsolutePath,@"^/(?:ru/|uk/)?person_films/[a-z0-9-]+-"+Regex.Escape(id)+@"\.phtml$",RegexOptions.IgnoreCase))throw new InvalidDataException("Источник не подтвердил фильмографию этой персоны.");
        var path=Path.Combine(Preferences.DataDir,"people",Hash(source+"|"+profile.Person.Role)+".professional-works.json");
        CinemaPeople.Work[]? saved=null;
        try
        {
            if(File.Exists(path))saved=JsonSerializer.Deserialize<CinemaPeople.Work[]>(await CacheFiles.ReadAllTextAsync(path,ct));
            if(saved!=null&&File.GetLastWriteTimeUtc(path)>DateTime.UtcNow.AddDays(-7))return saved;
        }
        catch(IOException){}catch(JsonException){}catch(UnauthorizedAccessException){}
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var bytes=await client.Read(uri,MaximumBytes,deadline.Token).WaitAsync(deadline.Token);
            if(!Normalize(Text(Html(bytes).DocumentNode.SelectSingleNode("//h1"))).StartsWith("фильмография ",StringComparison.Ordinal))throw new InvalidDataException("Источник временно не предоставил фильмографию.");
            var works=profile.Works.Concat(Works(bytes,profile.Person.Role)).DistinctBy(x=>(Normalize(x.Title),x.Year)).ToArray();
            try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);await CacheFiles.WriteAllTextAsync(path,JsonSerializer.Serialize(works),ct);}catch(IOException){}catch(UnauthorizedAccessException){}
            return works;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch when(saved!=null){return saved;}
    }

}
