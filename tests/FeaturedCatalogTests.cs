using Kachalka;
public static class FeaturedCatalogTests
{
    public static void Run()
    {
        void Check(bool pass,string message){if(!pass)throw new Exception(message);}
        var definitions=FeaturedCatalog.Collections;
        Check(definitions.Count>=4&&definitions.Select(row=>row.Id).Distinct().Count()==definitions.Count,"Curated IDs must be stable and unique.");
        foreach(var definition in definitions)
        {
            Check(definition.Title.Length>0&&definition.TitleEn.Length>0,"Both languages need a title.");
            Check(definition.IsGames!=((definition.MediaIds?.Length??0)>0),"Each shelf must use one card format.");
        }
        var first=definitions.First(row=>!row.IsGames);var ids=first.MediaIds!;
        MediaItem Film(int id,string online)=>new(id,"Fixture "+id,"Фильмы","",2024,"7","7","#123456"){OnlineId=online};
        var a=Film(11,ids[0]);var b=Film(12,ids[1]);var unrelated=Film(13,"movies:unrelated");
        var groups=FeaturedCatalog.Resolve([b,unrelated,a,a]);
        Check(groups.Single(row=>row.Collection.Id==first.Id).Items.Select(item=>item.Id).SequenceEqual(new[]{11,12}),"Editorial order survives shuffled/duplicate catalog data; missing IDs are skipped.");
        var games=groups.Single(row=>row.Collection.IsGames).Items;
        Check(games.Length>0&&games.All(item=>item.Section=="Игры"&&GameCatalog.TryId(item.PageUrl,out _)),"Game cards must open real Steam IDs through the normal game detail view.");
        var seasonal=new SeasonalCollection("test","Seasonal",[ids[1],"movies:missing"]);
        var seasonalGroups=FeaturedCatalog.Resolve([a,b],[seasonal]);
        Check(seasonalGroups[0].Collection.Id=="seasonal-test"&&seasonalGroups[0].Items.Single().Id==12,"Shared editorial collections join the hub using exact IDs.");
        var lang=AppLanguage.Current;
        try{AppLanguage.Set("en");Check(first.Label==first.TitleEn&&first.Summary==first.DescriptionEn,"English headings and descriptions use authored translations.");}
        finally{AppLanguage.Set(lang);}
        Console.WriteLine("PASS: curated membership, stable ordering, missing/duplicate IDs, shared collections, Steam identity and English labels");
    }
}
