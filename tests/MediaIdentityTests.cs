using System.IO;
namespace Kachalka.Tests;
static class MediaIdentityTests
{
    public static async Task Run()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var south=new MediaItem(-101,"Южный парк","Сериалы","",1997,"—","—","#526B69"){PageUrl="https://w6.zona.plus/tvseries/south-park-1997"};
        var english=south with{Id=-102,Title="South Park",PageUrl="https://www.imdb.com/title/tt0121955/",ImdbId="tt0121955"};
        var merged=MediaIdentity.Merge([south,english]);
        Check(merged.Length==1&&merged[0].Id==south.Id&&merged[0].ImdbId=="tt0121955"&&merged[0].OriginalTitle=="South Park","Russian and IMDb cards merge without losing the catalog identity");
        Check(MediaIdentity.Merge([english,south]).Single().Id==south.Id,"canonical catalog identity is independent of source arrival order");
        var solar=south with{Id=-103,Title="Обратная сторона Земли",Year=2020,PageUrl="https://w6.zona.plus/tvseries/solar-opposites-2020"};
        var other=solar with{Id=-104,Title="Солнечные противоположности",PageUrl="https://www.imdb.com/title/tt8910922/",ImdbId="tt8910922"};
        Check(MediaIdentity.Merge([solar,other]).Length==1&&MediaIdentity.Names(solar).Contains("Solar Opposites"),"alternate Russian translations use the same verified identity");
        Check(MediaIdentity.Merge([solar,solar with{Id=-105,Year=2021}]).Length==2,"identical translated names in different years are not merged");
        Check(MediaIdentity.Merge([other,other with{Id=-106,ImdbId="tt0000001"}]).Length==2,"conflicting IMDb ids are never merged");
        Check(MediaIdentity.Merge([solar,solar with{Id=-107,Section="Фильмы"}]).Length==2,"a series and a movie remain separate");
        var first=solar with{Id=-110,Title="Северная грань",PageUrl="https://w6.zona.plus/tvseries/first"};
        var second=first with{Id=-111,Title="Граница севера",PageUrl="https://w6.zona.plus/tvseries/second"};
        var bridge=first with{Id=-112,Title="Northern Border",ImdbId="tt1234567",PageUrl="https://www.imdb.com/title/tt1234567/",TitleAliases=[first.Title,second.Title]};
        Check(MediaIdentity.Merge([first,second,bridge]).Length==1,"verified metadata bridges previously separate alternate translations");
        Check(MediaIdentity.Queries(other).Contains("Обратная сторона Земли")&&MediaIdentity.Queries(other).Contains("Solar Opposites"),"tracker queries include both Russian names and the original name");
        Check(MediaIdentity.OriginalQuery("Солнечные противоположности")=="Solar Opposites"&&MediaIdentity.OriginalQuery("Южный парк")=="South Park","known Russian queries also find the exact original title when the Russian catalog is unavailable");
        Check(LiveCatalog.Matches(solar,new SourceEntry("identity","Solar Opposites S01 1080p","RuTor","",null,null)),"English torrents match the Russian catalog card");
        var root=Path.Combine(Path.GetTempPath(),"kachalka-identity-"+Guid.NewGuid().ToString("N"));
        try {
            var index=new CatalogIndex(root);await index.AddAsync([MediaIdentity.Enrich(solar),MediaIdentity.Enrich(english)]);
            var restored=new CatalogIndex(root);
            Check(restored.Search("Сериалы","Солнечные противоположности").Single().ImdbId=="tt8910922","learned aliases survive a restart and are searchable");
            Check(restored.Search("Сериалы","South Park").Count>0,"IMDb-only identities are retained in the local database");
            var named=other with{TitleAliases=["Иное проверенное название"]};await index.AddAsync([named]);
            Check(new CatalogIndex(root).Search("Сериалы","Иное проверенное название").Count==1,"additional provider aliases persist without a new application build");
        } finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
