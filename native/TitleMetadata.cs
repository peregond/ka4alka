using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// Text for a title the Russian catalog lacks, as the site's /api/title answers it: TheTVDB for series, TMDB for films.
// The site holds the keys; the app only reads this fixed answer, and accepts it only for the card's own IMDb id.
public static class TitleMetadata
{
    static readonly Dictionary<string,string> Genres=new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"]="боевик",["Adventure"]="приключения",["Animation"]="мультфильм",["Anime"]="аниме",["Children"]="детский",["Kids"]="детский",["Comedy"]="комедия",["Crime"]="криминал",
        ["Documentary"]="документальный",["Drama"]="драма",["Family"]="семейный",["Fantasy"]="фэнтези",["Food"]="кулинария",["Game Show"]="игровое шоу",["History"]="история",
        ["Horror"]="ужасы",["Legal"]="юридический",["Medical"]="медицина",["Music"]="музыка",["Mystery"]="детектив",["Nature"]="природа",["News"]="новости",["Reality"]="реалити-шоу",
        ["Romance"]="мелодрама",["Science Fiction"]="фантастика",["Science-Fiction"]="фантастика",["Soap"]="мыльная опера",["Sport"]="спорт",["Sports"]="спорт",["Supernatural"]="мистика",
        ["Suspense"]="триллер",["Talk Show"]="ток-шоу",["Thriller"]="триллер",["Travel"]="путешествия",["War"]="военный",["Western"]="вестерн",["Espionage"]="шпионский",["Adult"]="для взрослых"
    };
    // TheTVDB names a series' country with an ISO 3166 alpha-3 code; TMDB already answers in Russian words.
    static readonly Dictionary<string,string> Countries=new(StringComparer.OrdinalIgnoreCase)
    {
        ["usa"]="США",["gbr"]="Великобритания",["can"]="Канада",["aus"]="Австралия",["jpn"]="Япония",["kor"]="Южная Корея",["deu"]="Германия",["fra"]="Франция",["rus"]="Россия",
        ["chn"]="Китай",["esp"]="Испания",["ita"]="Италия",["swe"]="Швеция",["dnk"]="Дания",["nor"]="Норвегия",["fin"]="Финляндия",["nld"]="Нидерланды",["bel"]="Бельгия",
        ["pol"]="Польша",["ukr"]="Украина",["blr"]="Беларусь",["ind"]="Индия",["mex"]="Мексика",["bra"]="Бразилия",["arg"]="Аргентина",["tur"]="Турция",["isr"]="Израиль",["nzl"]="Новая Зеландия",["irl"]="Ирландия"
    };

    public static string Genre(string value)=>Genres.TryGetValue(value.Trim(),out var russian)?russian:value.Trim();

    static string? Text(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static string[] Strings(JsonElement row,string name,int limit)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Array
        ?value.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!.Trim()).Where(x=>x.Length>0).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(limit).ToArray():Array.Empty<string>();

    public static MediaItem Apply(MediaItem item,JsonElement row)
    {
        var section=item.Section=="Сериалы"?"series":"movies";
        if(row.ValueKind!=JsonValueKind.Object||Text(row,"section")!=section||Text(row,"imdbId") is not {} imdb||!imdb.Equals(item.ImdbId,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Сайт вернул сведения о другом названии.");
        var overview=Text(row,"overview")?.Trim();
        var genres=Strings(row,"genres",4).Select(Genre).Distinct().ToArray();
        var countries=Strings(row,"countries",3).Select(x=>Regex.IsMatch(x,"^[A-Za-z]{3}$")?Countries.GetValueOrDefault(x,""):x).Where(x=>x.Length>0).Distinct().ToArray();
        var year=row.TryGetProperty("year",out var y)&&y.ValueKind==JsonValueKind.Number&&y.TryGetInt32(out var value)&&value is >=1880 and <=2100?value:0;
        var original=Text(row,"originalTitle")?.Trim();
        var people=item.People;
        // Credits the card already has (from another source) are kept; only missing roles are added.
        CinemaPerson[] Credits(string key,string role)=>people.Any(x=>x.Role==role)?[]:Strings(row,key,12).Select(name=>new CinemaPerson(name,role,"")).ToArray();
        people=people.Concat(Credits("directors","Режиссёры")).Concat(Credits("actors","Актёры")).ToArray();
        return item with
        {
            Description=!string.IsNullOrWhiteSpace(overview)?overview:item.Description,
            Genre=string.IsNullOrWhiteSpace(item.Genre)&&genres.Length>0?string.Join(", ",genres):item.Genre,
            Country=string.IsNullOrWhiteSpace(item.Country)&&countries.Length>0?string.Join(", ",countries):item.Country,
            Year=item.Year>0?item.Year:year,
            OriginalTitle=string.IsNullOrWhiteSpace(item.OriginalTitle)&&!string.IsNullOrWhiteSpace(original)?original:item.OriginalTitle,
            People=people
        };
    }
}
