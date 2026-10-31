namespace Kachalka;
public static class CatalogChoices
{
    public static readonly CatalogChoice[] Genres=[
        new("drama","драма"),
        new("komediia","комедия"),
        new("triller","триллер"),
        new("melodrama","мелодрама"),
        new("boevik","боевик"),
        new("kriminal","криминал"),
        new("uzhasy","ужасы"),
        new("prikliucheniia","приключения"),
        new("detektiv","детектив"),
        new("fantastika","фантастика"),
        new("semeinyi","семейный"),
        new("fentezi","фэнтези"),
        new("multfilm","мультфильм"),
        new("korotkometrazhka","короткометражка"),
        new("voennyi","военный"),
        new("istoriia","история"),
        new("dokumentalnyi","документальный"),
        new("biografiia","биография"),
        new("miuzikl","мюзикл"),
        new("muzyka","музыка"),
        new("vestern","вестерн"),
        new("sport","спорт"),
        new("detskii","детский"),
        new("anime","аниме"),
        new("film-nuar","фильм-нуар"),
        new("realnoe-tv","реальное ТВ"),
        new("kontcert","концерт"),
        new("novosti","новости"),
        new("igra","игра"),
        new("tok-shou","ток-шоу"),
    ];
    public static readonly CatalogChoice[] Countries=[
        new("ssha","США"),
        new("rossiia","Россия"),
        new("frantciia","Франция"),
        new("velikobritaniia","Великобритания"),
        new("kanada","Канада"),
        new("germaniia","Германия"),
        new("italiia","Италия"),
        new("indiia","Индия"),
        new("iaponiia","Япония"),
        new("ispaniia","Испания"),
        new("gonkong","Гонконг"),
        new("koreia-iuzhnaia","Корея Южная"),
        new("kitai","Китай"),
        new("belgiia","Бельгия"),
        new("avstraliia","Австралия"),
        new("polsha","Польша"),
        new("shvetciia","Швеция"),
        new("meksika","Мексика"),
        new("chekhiia","Чехия"),
        new("daniia","Дания"),
        new("niderlandy","Нидерланды"),
        new("irlandiia","Ирландия"),
        new("shveitcariia","Швейцария"),
        new("argentina","Аргентина"),
        new("ukraina","Украина"),
        new("norvegiia","Норвегия"),
        new("vengriia","Венгрия"),
        new("avstriia","Австрия"),
        new("braziliia","Бразилия"),
        new("taivan","Тайвань"),
    ];
}

// "ТВ Шоу" is a view over the series catalogue: its titles are ordinary series
// whose source genre is one of ShowGenres, so items keep the "Сериалы" section.
public static class CatalogSections
{
    public const string Shows="ТВ Шоу";
    public static readonly CatalogChoice[] ShowGenres=[new("realnoe-tv","реальное ТВ"),new("tok-shou","ток-шоу"),new("igra","игра")];
    public static bool IsShowGenre(string? key)=>ShowGenres.Any(genre=>genre.Key==key);
    // The section an item of a catalogue view belongs to.
    public static string ItemSection(string section)=>section==Shows?"Сериалы":section;
    public static bool IsShow(MediaItem item)=>item.Section=="Сериалы"&&item.GenreKeys.Any(IsShowGenre);
}
