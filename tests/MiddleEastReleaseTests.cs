using System.Text.Json;
namespace Kachalka;

static class MiddleEastReleaseTests
{
    public const string Link="kachalka://movie/odnazhdy-na-blizhnem-vostoke?title=%D0%9E%D0%B4%D0%BD%D0%B0%D0%B6%D0%B4%D1%8B%2520%D0%BD%D0%B0%2520%D0%91%D0%BB%D0%B8%D0%B6%D0%BD%D0%B5%D0%BC%2520%D0%92%D0%BE%D1%81%D1%82%D0%BE%D0%BA%D0%B5&year=2026";
    public static MediaItem Movie()=>MediaIdentity.Enrich(new(-101,"Однажды на Ближнем Востоке","Фильмы","",2026,"—","—","#526B69")
    {PageUrl=LiveCatalog.Base+"/movies/odnazhdy-na-blizhnem-vostoke",OriginalTitle="Huan ying laai long can guan"});
    public static void Run()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var target=ShareLink.Parse(Link);
        Check(target is {Year:2026,Slug:"odnazhdy-na-blizhnem-vostoke"},"reported browser-escaped film link retains its exact catalog identity and year");
        var movie=LinkedMovie();
        Check(movie.Title=="Однажды на Ближнем Востоке"&&!MediaIdentity.Queries(movie).Any(x=>x.Contains("%20")),"canonical page metadata repairs the shared title hint before release queries on both platforms");
        Check(ShareLink.Parse(ShareLink.Create(Movie() with{Title="100%20 & / ?"}))?.Title=="100%20 & / ?","share links still round-trip literal percent sequences without recursive URL decoding");
        Check(movie.ImdbId=="tt34386754"&&MediaIdentity.Queries(movie).Contains("Once Upon a Time in the Middle East"),"verified English title is queried even while the catalog retains its Chinese transliteration");
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","knaben-middle-east-2026.json"));
        var row=KnabenSource.Parse(bytes,movie).Single();
        Check(row.Source=="The Pirate Bay"&&row.Via=="Knaben"&&row.Seeds==411&&row.Size==3307124817&&row.Quality=="Full HD"&&ReleaseAvailability.Downloadable(row),"observed 1080p WEB release from Knaben's TPB index remains downloadable with original source and counts");
        Check(row.TorrentUrl!.Contains("F81D21727E4DB6E7E70290230C230570108473BB")&&row.PageUrl=="https://thepiratebay.org/description.php?id=84302551","observed info hash is retained and the exact TPB proxy page maps to its canonical source");
        Check(KnabenSource.Parse(bytes,movie with{Year=2025}).Count==0&&KnabenSource.Parse(bytes,movie with{ImdbId="tt0000001",Title="Another film",OriginalTitle=null,TitleAliases=[]}).Count==0,"a different year or film cannot inherit this release");
        using var doc=JsonDocument.Parse(bytes);var hit=doc.RootElement.GetProperty("hits")[0];
        var fields=hit.EnumerateObject().ToDictionary(x=>x.Name,x=>(object)x.Value.Clone());
        fields["category"]="Games / PC";
        Check(KnabenSource.Parse(JsonSerializer.SerializeToUtf8Bytes(new{hits=new[]{fields}}),movie).Count==0,"broadening indexed trackers does not admit games into film results");
        fields["category"]="Movies / HD";fields["tracker"]="Unknown tracker";
        Check(KnabenSource.Parse(JsonSerializer.SerializeToUtf8Bytes(new{hits=new[]{fields}}),movie).Count==0,"unrecognized trackers remain excluded");
    }
    static MediaItem LinkedMovie()
    {
        var target=ShareLink.Parse(Link)!;
        var card=DownloadMetadata.Card(new(){MediaTitle=target.Title,MediaSection=target.Section,MediaPageUrl=target.PageUrl,MediaYear=target.Year})!;
        return MediaIdentity.Enrich(DownloadMetadata.EnrichMedia(card,[Movie()]));
    }
    public static async Task Probe()
    {
        using var client=new SourceClient();
        var result=await ReleaseSearch.RunAsync(NativeReleaseSources.Create(client,LinkedMovie()).Where(x=>x.Name=="Knaben"));
        foreach(var state in result.Sources)Console.WriteLine($"PROBE middle-east: {state.Name} {state.State}, {state.Count} releases");
        if(!result.Items.Any(x=>x.Source=="The Pirate Bay"&&ReleaseAvailability.Downloadable(x)&&LiveCatalog.Matches(Movie(),x)))throw new Exception("The verified film's indexed video release was not found.");
        foreach(var row in result.Items)Console.WriteLine($"PROBE matched: {row.Source}, {row.Title}, seeds={row.Seeds}");
    }
}
