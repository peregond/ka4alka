namespace Kachalka;

public static class NativeReleaseSources
{
    public static ReleaseSource[] Create(SourceClient client,MediaItem item)=>Create(client,_=>Task.FromResult(item),item.Section=="Сериалы");
    public static ReleaseSource[] Create(SourceClient client,Func<CancellationToken,Task<MediaItem>> resolve,bool series)
    {
        async Task<IReadOnlyList<SourceEntry>> Resolved(Func<MediaItem,CancellationToken,Task<IReadOnlyList<SourceEntry>>> search,CancellationToken ct)=>await search(await resolve(ct),ct);
        async Task<IReadOnlyList<SourceEntry>> Aliases(MediaItem item,Func<string,CancellationToken,Task<IReadOnlyList<SourceEntry>>> search,CancellationToken ct)
        {
            var groups=await Task.WhenAll(new[]{item.Title,item.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Select(async title=>
            {
                try{return (Items:await search(title,ct),Error:(Exception?)null);}
                catch(Exception error) when(!ct.IsCancellationRequested){return (Items:(IReadOnlyList<SourceEntry>)[],Error:error);}
            }));
            if(groups.All(x=>x.Error!=null))throw groups.First().Error!;
            return groups.SelectMany(x=>x.Items).Where(x=>LiveCatalog.Matches(item,x)).ToArray();
        }
        var api=new LiveCatalog(client);
        var providers=new List<ReleaseSource>
        {
            new("RuTor",ct=>Resolved((item,t)=>Aliases(item,api.Releases,t),ct)),
            new("NNM-Club",ct=>Resolved(new NnmClubSource(client).Search,ct)),
            new("MegaPeer",ct=>Resolved(new MegaPeerSource(client).Search,ct)),
            new("BigFanGroup",ct=>Resolved(new BigFanGroupSource(client).Search,ct)),
            new("RuTracker · через Knaben",ct=>Resolved(KnabenSource.Search,ct)),
            new("The Pirate Bay",ct=>Resolved(new PirateBaySource(client).Search,ct)),
            new("Internet Archive",ct=>Resolved((item,t)=>Aliases(item,(title,token)=>client.SearchArchive(title,"Фильмы",1,token),t),ct))
        };
        if(series)
        {
            providers.Add(new("Nyaa",ct=>Resolved(async (item,t)=>
            {
                var title=item.OriginalTitle??item.Title;
                return title.Any(c=>c is >= '\u0400' and <= '\u04FF')?[]:(await client.SearchNyaa(title,t)).Where(x=>LiveCatalog.Matches(item,x)).ToArray();
            },ct)));
            providers.Add(new("EZTV",ct=>Resolved(async (item,t)=>
            {
                var title=item.OriginalTitle??item.Title;
                if(title.Any(c=>c is >= '\u0400' and <= '\u04FF'))return [];
                var identity=await client.FindSeriesIdentity(title,item.Year,t);return identity==null?[]:await client.SearchEztv(identity,t);
            },ct)));
        }
        return providers.ToArray();
    }
}
