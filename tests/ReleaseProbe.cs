using Kachalka;
static class ReleaseProbe
{
    public static async Task AffectedFilms()
    {
        using var client=new SourceClient();
        foreach(var media in new[]{new MediaItem(-101,"Интерстеллар","Фильмы","",2014,"—","—","#526B69"){OriginalTitle="Interstellar"},new MediaItem(-102,"Южный парк","Сериалы","",1997,"—","—","#526B69"){OriginalTitle="South Park"}})
        {
            var native=NativeReleaseSources.Create(client,media).Where(x=>x.Name is "BigFanGroup" or "The Pirate Bay");
            var scan=await ReleaseSearch.RunAsync(native,ct:CancellationToken.None);
            foreach(var source in scan.Sources){Console.WriteLine($"PROBE media={media.Title}; source={source.Name}; state={source.State}; releases={source.Count}");if(source.State!=SourceState.Ready)throw new Exception("New public source did not return actual matched video releases: "+source.Name+" / "+media.Title);}
            var torrent=scan.Items.First(x=>x.Source=="BigFanGroup");var path=await client.TorrentFile(torrent,CancellationToken.None);
            Console.WriteLine($"PROBE validated public BigFanGroup torrent for {media.Title}: {new System.IO.FileInfo(path).Length} bytes");
        }
        foreach(var title in new[]{"До последнего грамма","Объект преступления"})
        {
            var film=BundledCatalog.Search("Фильмы",title).Single(item=>item.Title==title);
            if(string.IsNullOrWhiteSpace(film.OriginalTitle)||film.Imdb=="—")throw new Exception("Affected film is missing confirmed metadata: "+title);
            var result=await ReleaseSearch.RunAsync(new[]{new ReleaseSource("NNM-Club",ct=>new NnmClubSource(client).Search(film,ct))},ct:CancellationToken.None);
            Console.WriteLine($"PROBE film={title}; original={film.OriginalTitle}; IMDb={film.Imdb}; releases={result.Items.Length}; source={result.Sources.Single().State}");
            if(title=="До последнего грамма"&&result.Items.Length==0)throw new Exception("The verified NNM Screener release was not found.");
        }
    }
    public static async Task Run()
    {
        using var client=new SourceClient();var catalog=new LiveCatalog(client);
        foreach(var film in new[]{new MediaItem(-101,"Big Buck Bunny","Фильмы","",2008,"—","—","#526B69"){OriginalTitle="Big Buck Bunny",PageUrl=LiveCatalog.Base+"/movies/big-buck-bunny"},new MediaItem(-102,"Интерстеллар","Фильмы","",2014,"—","—","#526B69"){OriginalTitle="Interstellar",PageUrl=LiveCatalog.Base+"/movies/interstellar"}})
        {
            Console.WriteLine("PROBE film="+film.Title+" year="+film.Year);
            async Task<IReadOnlyList<SourceEntry>> Rutor(CancellationToken ct)=>(await catalog.Releases(film.Title,ct)).Where(x=>LiveCatalog.Matches(film,x)).ToArray();
            var providers=new ReleaseSource[]{new("RuTor",Rutor),new("NNM-Club",ct=>new NnmClubSource(client).Search(film,ct)),new("MegaPeer",ct=>new MegaPeerSource(client).Search(film,ct)),new("RuTracker via Knaben",ct=>KnabenSource.Search(film,ct))};
            var result=await ReleaseSearch.RunAsync(providers,ct:CancellationToken.None);
            foreach(var source in result.Sources)Console.WriteLine($"PROBE source={source.Name} status={source.State} count={source.Count}");
            foreach(var row in result.Items.Take(3))Console.WriteLine("PROBE result="+row.Source+" "+row.Title);
        }
    }
}
