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
        // Rubrics drawn from the catalog: newest first, and a ranking only from titles that both sources rate highly.
        MediaItem Rated(int id,int year,string kp,string imdb,string section="Фильмы",string poster="https://example.test/p.jpg",string description="Описание")
            =>new(id,"Rated "+id,section,"",year,kp,imdb,"#123456"){OnlineId="movies:rated-"+id,ImageUrl=poster,Description=description};
        var catalog=new[]{Rated(21,2026,"7.0","7.0"),Rated(22,2025,"8.5","8.5"),Rated(23,2026,"8.0","8.0"),Rated(24,2026,"9.5","—"),Rated(25,2026,"8.2","8.2","Игры"),Rated(26,2026,"8.2","8.2",poster:""),Rated(27,2026,"8.2","8.2",description:""),Rated(28,1950,"9.0","9.0")};
        var derived=FeaturedCatalog.Resolve(catalog);
        var fresh=derived.Single(row=>row.Collection.Id==FeaturedCatalog.FreshId);
        Check(fresh.Items.Select(item=>item.Id).SequenceEqual(new[]{23,21,24,22,28}),"Fresh lists the newest year first and the better rated title first within a year; games and titles without poster or text are left out.");
        var top=derived.Single(row=>row.Collection.Id==FeaturedCatalog.TopRatedId);
        Check(top.Items.Select(item=>item.Id).SequenceEqual(new[]{22,23})&&top.Collection.Layout=="ranking","The ranking needs both ratings, keeps only well rated titles and is laid out as a ranking.");
        Check(FeaturedCatalog.Resolve([a,b]).All(row=>row.Collection.Id is not (FeaturedCatalog.FreshId or FeaturedCatalog.TopRatedId)),"Rubrics without presentable titles are not shown.");
        // Genre shelves: best rated first, a title only on the first shelf that takes it, and no shelf for a genre with too few titles.
        MediaItem Genre(int id,string rating,params string[] keys)=>new(id,"Genre "+id,"Фильмы","",2024,rating,rating,"#123456"){OnlineId="movies:genre-"+id,ImageUrl="https://example.test/p.jpg",Description="Описание",GenreKeys=keys};
        var genreCatalog=Enumerable.Range(0,8).Select(index=>Genre(100+index,(8.0-index*.1).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),"fantastika","triller"))
            .Concat(Enumerable.Range(0,7).Select(index=>Genre(200+index,"7.0","triller"))).Append(Genre(300,"5.0","komediia")).ToArray();
        var genreGroups=FeaturedCatalog.Resolve(genreCatalog).Where(row=>row.Collection.Id.StartsWith("genre-",StringComparison.Ordinal)).ToArray();
        Check(genreGroups.Select(row=>row.Collection.Id).SequenceEqual(new[]{"genre-fantastika","genre-triller"}),"Only genres with enough well rated titles get a shelf, in the fixed genre order.");
        Check(genreGroups[0].Items.Select(item=>item.Id).SequenceEqual(Enumerable.Range(0,8).Select(index=>100+index)),"A genre shelf lists its titles best rated first.");
        Check(genreGroups[1].Items.Select(item=>item.Id).SequenceEqual(Enumerable.Range(0,7).Select(index=>200+index))&&genreGroups[0].Collection.Kicker.Length>0,"A title is on one genre shelf only.");
        Check(first.Layout is "lead" or "shelf" or "ranking"&&definitions.All(row=>row.Layout is "shelf" or "lead" or "ranking"),"Layouts are known.");
        var lang=AppLanguage.Current;
        try{AppLanguage.Set("en");Check(first.Label==first.TitleEn&&first.Summary==first.DescriptionEn,"English headings and descriptions use authored translations.");}
        finally{AppLanguage.Set(lang);}
        AwardsCatalogTests.Run();
        Console.WriteLine("PASS: curated membership, stable ordering, missing/duplicate IDs, shared collections, Steam identity and English labels");
    }
}
