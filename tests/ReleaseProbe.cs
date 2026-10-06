using Kachalka;
static class ReleaseProbe
{
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
