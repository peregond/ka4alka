using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kachalka;

public static class CinemaSourceTests
{
    static readonly string[] KnownPages=[LiveCatalog.Base+"/movies/terminator",LiveCatalog.Base+"/movies/terminator-2-sudnyi-den"];
    static string Normalize(string value)=>Regex.Replace(value.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static async Task<T> Bounded<T>(Func<CancellationToken,Task<T>> read,CancellationToken ct,TimeSpan? limit=null)
    {
        using var request=CancellationTokenSource.CreateLinkedTokenSource(ct);request.CancelAfter(limit??TimeSpan.FromSeconds(8));
        return await read(request.Token).WaitAsync(request.Token);
    }
    static MediaItem KnownFilm(string url,int position)=>new(-(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(url)),0)&int.MaxValue),position==0?"Терминатор":"Терминатор 2: Судный день","Фильмы","",position==0?1984:1991,"—","—","#526B69"){PageUrl=url};

    public static async Task Diagnostics()
    {
        using var client=new SourceClient();var output=Path.GetFullPath("test-output/cinema-source-diagnostics");Directory.CreateDirectory(output);
        using var raw=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});raw.DefaultRequestHeaders.UserAgent.ParseAdd("Kachalka/0.11");
        var urls=new[]{LiveCatalog.Base+"/search-form?query="+Uri.EscapeDataString("Терминатор")}.Concat(KnownPages).Append(new Uri(OnlineIndexClient.PublishedSite,"api/catalog?section=movies&q="+Uri.EscapeDataString("Терминатор")+"&page=1").AbsoluteUri).ToArray();
        var evidence=await Task.WhenAll(urls.Select(async url=>
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            int? rawStatus=null;var finalUri="";var location="";var rawError="";
            try
            {
                using var headers=await Bounded(ct=>raw.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct),CancellationToken.None);
                rawStatus=(int)headers.StatusCode;finalUri=headers.RequestMessage?.RequestUri?.AbsoluteUri??url;location=headers.Headers.Location?.ToString()??"";
                Console.WriteLine($"DIAGNOSTIC noRedirect status={rawStatus}; request={url}; finalUri={finalUri}; location={location}; seconds={watch.Elapsed.TotalSeconds:0.0}");
            }
            catch(Exception error){rawError=error.GetType().Name+": "+error.Message;Console.WriteLine($"DIAGNOSTIC noRedirect unavailable request={url}; error={rawError}");}
            try
            {
                var bytes=await Bounded(ct=>client.Read(new Uri(url),4*1024*1024,ct),CancellationToken.None);
                var people=url.StartsWith(LiveCatalog.Base,StringComparison.Ordinal)?CinemaMetadata.People(bytes).Length:0;
                var rows=url.StartsWith(OnlineIndexClient.PublishedSite.AbsoluteUri,StringComparison.Ordinal)?JsonDocument.Parse(bytes).RootElement.GetProperty("items").GetArrayLength():0;
                Console.WriteLine($"DIAGNOSTIC redirectedRead succeeded url={url}; bytes={bytes.Length}; people={people}; catalogRows={rows}; seconds={watch.Elapsed.TotalSeconds:0.0}");
                await File.WriteAllBytesAsync(Path.Combine(output,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".response"),bytes);
                return new{Url=url,Available=true,RawStatus=rawStatus,FinalUri=finalUri,Location=location,RawError=rawError,Bytes=bytes.Length,People=people,CatalogRows=rows,Seconds=watch.Elapsed.TotalSeconds,Error=""};
            }
            catch(Exception error)
            {
                Console.WriteLine($"DIAGNOSTIC unavailable url={url}; seconds={watch.Elapsed.TotalSeconds:0.0}; error={error.GetType().Name}: {error.Message}");
                return new{Url=url,Available=false,RawStatus=rawStatus,FinalUri=finalUri,Location=location,RawError=rawError,Bytes=0,People=0,CatalogRows=0,Seconds=watch.Elapsed.TotalSeconds,Error=error.ToString()};
            }
        }));
        await File.WriteAllTextAsync(Path.Combine(output,"responses.json"),JsonSerializer.Serialize(evidence));
        Console.WriteLine("DIAGNOSTIC ONLY: HTTP reachability does not verify live cinema navigation.");
    }

    public static async Task Run()
    {
        using var client=new SourceClient();using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var catalog=new LiveCatalog(client);var online=new OnlineIndexClient(client);var output=Path.GetFullPath("test-output/cinema-source");Directory.CreateDirectory(output);
        var candidates=new List<MediaItem>();
        try
        {
            var indexed=await Bounded(ct=>online.Browse("Фильмы","Терминатор",1,ct),deadline.Token);
            candidates.AddRange(indexed.Take(3));Console.WriteLine($"DISCOVERY: published online catalog returned {indexed.Count} film identities; live credits still required.");
        }
        catch(Exception error)when(!deadline.IsCancellationRequested){Console.WriteLine("DISCOVERY: online catalog unavailable: "+error.Message);}
        if(candidates.Count==0)
            try
            {
                var found=await Bounded(ct=>catalog.Browse("Фильмы","Терминатор",1,ct),deadline.Token);
                candidates.AddRange(found.Take(3));Console.WriteLine($"DISCOVERY: direct catalog search returned {found.Count} film identities.");
            }
            catch(Exception error)when(!deadline.IsCancellationRequested){Console.WriteLine("DISCOVERY: direct search unavailable: "+error.Message);}
        // These identities were verified in earlier live releases. They identify
        // direct navigation targets only; all film credits must be fetched now.
        candidates.AddRange(KnownPages.Select(KnownFilm));
        var freshFilms=new List<MediaItem>();
        bool SharedParticipant(CinemaPerson person)=>freshFilms.Count(x=>x.People.Any(p=>Normalize(p.Name)==Normalize(person.Name)&&p.Role==person.Role))>1;
        foreach(var film in candidates.DistinctBy(x=>x.PageUrl).OrderBy(x=>KnownPages.Contains(x.PageUrl)?0:1))
        {
            try
            {
                var bytes=await Bounded(ct=>client.Read(new Uri(film.PageUrl!),4*1024*1024,ct),deadline.Token);
                await File.WriteAllBytesAsync(Path.Combine(output,"film-"+film.Id+".html"),bytes,deadline.Token);
                var people=CinemaMetadata.People(bytes);Console.WriteLine($"LIVE: {film.Title}: {people.Length} participants from {film.PageUrl}");
                if(people.Length==0)continue;
                var metadata=await MediaMetadata.Load(film,ct=>online.Detail(film,ct),ct=>catalog.Detail(film,ct),ct:deadline.Token);
                freshFilms.Add(MediaMetadata.Merge(metadata.Item,film with{People=people}));
                if(freshFilms.SelectMany(x=>x.People).Any(SharedParticipant))break;
            }
            catch(Exception error)when(!deadline.IsCancellationRequested){Console.WriteLine($"LIVE: film unavailable {film.PageUrl}: {error.Message}");}
        }
        foreach(var film in freshFilms)
        foreach(var person in film.People.OrderByDescending(SharedParticipant).ThenByDescending(x=>x.Role=="Режиссёры").Take(2))
        {
            try
            {
                // The application uses already known film cards too. Here they
                // qualify only after fresh HTTP responses confirmed their credits.
                var profile=await catalog.Person(person,deadline.Token,film,freshFilms);
                var works=profile.Filmography.Where(x=>x.PageUrl!=null).DistinctBy(x=>x.PageUrl).ToArray();
                Console.WriteLine($"LIVE: {person.Name}: {works.Length} distinct works, biography {profile.Description.Length} characters");
                if(works.Length<2||profile.Description.Length<50)continue;
                var verified=new List<MediaItem>();
                foreach(var work in works.Take(6))
                {
                    try
                    {
                        var bytes=await Bounded(ct=>client.Read(new Uri(work.PageUrl!),4*1024*1024,ct),deadline.Token);
                        var credits=CinemaMetadata.People(bytes);
                        if(!credits.Any(x=>Normalize(x.Name)==Normalize(person.Name)&&x.Role==person.Role))continue;
                        verified.Add(work);await File.WriteAllBytesAsync(Path.Combine(output,"verified-work-"+work.Id+".html"),bytes,deadline.Token);
                        if(verified.Count==2)break;
                    }
                    catch(Exception error)when(!deadline.IsCancellationRequested){Console.WriteLine($"LIVE: work verification unavailable {work.PageUrl}: {error.Message}");}
                }
                if(verified.Count<2)continue;
                await File.WriteAllTextAsync(Path.Combine(output,"verified.json"),JsonSerializer.Serialize(new{Film=film.Title,People=film.People,Person=profile.Person,Works=verified.Select(x=>new{x.Title,x.PageUrl}),Top=CinemaMetadata.Top(profile.Filmography).Length,FreshCredits=true,FreshWorkVerification=true}),deadline.Token);
                Console.WriteLine("PASS: live film credits, participant biography and two independently verified filmography cards");return;
            }
            catch(Exception error)when(!deadline.IsCancellationRequested){Console.WriteLine($"LIVE: person unavailable {person.Name}: {error.Message}");}
        }
        throw new InvalidOperationException("Live source did not provide working film → person → film navigation with fresh credits for two distinct works; see source HTTP diagnostics and captured HTML.");
    }
}
