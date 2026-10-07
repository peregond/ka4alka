using System.Text;
using System.Text.Json;
using Kachalka;
static class AdditionalSourceTests
{
    public static void Run()
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        var film=new MediaItem(-1,"Интерстеллар","Фильмы","",2014,"—","—","#526B69"){OriginalTitle="Interstellar"};
        var series=new MediaItem(-2,"Южный парк","Сериалы","",1997,"—","—","#526B69"){OriginalTitle="South Park"};
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] Fixture(string name)=>Encoding.GetEncoding(1251).GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".html")));
        var films=BigFanGroupSource.Parse(Fixture("bigfangroup-film"),film);
        var shows=BigFanGroupSource.Parse(Fixture("bigfangroup-series"),series);
        Check(films.Count==1&&films[0].Id=="BigFanGroup:285126"&&films[0].Seeds==421&&films[0].Size==4273492459&&films[0].TorrentUrl=="https://bigfangroup.org/download.php?id=285126","BigFanGroup parses a real public Russian film row with correct torrent, size, seeders");
        Check(shows.Count==1&&shows[0].Id=="BigFanGroup:344266"&&shows[0].Series.MultipleSeasons&&shows[0].Quality=="Full HD","BigFanGroup recognizes animated series and mixed Latin/Cyrillic season labels");
        Check(BigFanGroupSource.Parse(Fixture("bigfangroup-film"),film with{Year=2015}).Count==0&&BigFanGroupSource.Parse(Fixture("bigfangroup-series"),series with{Section="Фильмы"}).Count==0,"BigFanGroup rejects wrong film year, soundtracks, and series in film searches");
        Check(BigFanGroupSource.QueryUri("Интерстеллар").Query.Contains("%C8%ED%F2",StringComparison.OrdinalIgnoreCase),"BigFanGroup Cyrillic queries use its actual CP1251 search encoding");
        object Row(string title,string id="1",string hash="0123456789012345678901234567890123456789",string category="207")=>new{id,name=title,info_hash=hash,seeders="12",size="1024",category};
        var rows=PirateBaySource.Parse(JsonSerializer.SerializeToUtf8Bytes(new[]{Row("Interstellar (2014) 1080p"),Row("Interstellar (2015) 1080p","2"),Row("Interstellar (2014) soundtrack","3",category:"101"),Row("Interstellar (2014) 1080p","0",new string('0',40)),Row("Interstellar (2014) 1080p","4","invalid")}),film);
        Check(rows.Count==1&&rows[0].Seeds==12&&rows[0].Size==1024&&rows[0].TorrentUrl!.StartsWith("magnet:?xt=urn:btih:")&&rows[0].Source=="The Pirate Bay","APIBay accepts matched video magnets and rejects wrong years/categories, zero sentinel and invalid hashes");
        Check(PirateBaySource.Parse(JsonSerializer.SerializeToUtf8Bytes(new[]{Row("South Park S12E08 RUS","9",category:"205")}),series).Count==1&&PirateBaySource.QueryUri("Южный парк",true).Query.EndsWith("cat=205,208"),"APIBay supports matching Russian TV episodes with TV-specific categories");
        using var client=new SourceClient();
        Check(NativeReleaseSources.Create(client,film).Select(x=>x.Name).Contains("BigFanGroup")&&NativeReleaseSources.Create(client,series).Length==9,"the actual native search registry includes the new film and series providers");
    }
}
