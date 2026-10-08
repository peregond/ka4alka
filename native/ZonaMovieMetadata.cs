using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Kachalka;

// The catalog's own movie endpoint exposes richer credits in its JSON
// representation. These fields belong to the identified movie, not to an
// adjacent recommendation or an inferred external provider.
public static class ZonaMovieMetadata
{
    static string Text(string value)=>Regex.Replace(HtmlEntity.DeEntitize(Regex.Replace(value,@"<[^>]+>"," ")),@"\s+"," ").Trim();
    static string Normalize(string value)=>Regex.Replace(Text(value).ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static string Scalar(JsonElement value)=>value.ValueKind switch{JsonValueKind.String=>value.GetString()??"",JsonValueKind.Number=>value.GetRawText(),_=>""};
    static string Field(JsonElement value,string name)=>value.TryGetProperty(name,out var field)?Scalar(field):"";
    static string? Identifier(string value)=>Regex.IsMatch(value,@"^[1-9]\d{0,17}$")?value:null;
    static string Score(string value)=>double.TryParse(value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var score)&&score is >0 and <=10?score.ToString("0.#",CultureInfo.InvariantCulture):"—";

    public static string? PhotoUrl(string? value,string? sourcePersonId=null)
    {
        var photo=CinemaPeople.ZonaPhotoUrl(value);if(photo==null)return null;
        if(sourcePersonId!=null&&Regex.Match(new Uri(photo).AbsolutePath,@"/(\d+)\.[a-z]+$",RegexOptions.IgnoreCase).Groups[1].Value!=sourcePersonId)return null;
        return photo;
    }
    public static MediaItem? Parse(byte[] bytes,MediaItem item)
    {
        if(CinemaMetadata.CatalogUrl(item.PageUrl,"/movies/","/tvseries/")==null)return null;
        JsonDocument json;
        try{json=JsonDocument.Parse(bytes);}catch(JsonException){return null;}
        using(json)
        {
            var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty(item.Section=="Сериалы"?"serial":"movie",out var movie)||movie.ValueKind!=JsonValueKind.Object)return null;
            if(movie.TryGetProperty("serial",out var serial))
            {
                bool? isSeries=serial.ValueKind is JsonValueKind.True or JsonValueKind.False?serial.GetBoolean():bool.TryParse(Scalar(serial),out var flag)?flag:null;
                if(isSeries!=null&&isSeries!=(item.Section=="Сериалы"))return null;
            }
            var name=Text(Field(movie,"name_rus"));var original=Text(Field(movie,"name_original"));
            if(!int.TryParse(Field(movie,"year"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var year)||year is <1800 or >2100||item.Year>0&&year!=item.Year)return null;
            var expected=new[]{item.Title,item.OriginalTitle??""}.Select(Normalize).Where(x=>x.Length>0).ToArray();
            if(!new[]{name,original}.Select(Normalize).Any(x=>x.Length>0&&expected.Contains(x,StringComparer.Ordinal)))return null;
            (string Name,string Key)[] Choices(string field,string selectedField)
            {
                var ids=Field(movie,selectedField).Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(Identifier).OfType<string>().ToHashSet(StringComparer.Ordinal);
                if(ids.Count==0||!root.TryGetProperty(field,out var rows)||rows.ValueKind!=JsonValueKind.Array)return [];
                return rows.EnumerateArray().Where(row=>row.ValueKind==JsonValueKind.Object&&ids.Contains(Field(row,"id")))
                    .Select(row=>(Name:Text(Field(row,"name")),Key:Field(row,"translit")))
                    .Where(row=>row.Name.Length is >0 and <=200).Select(row=>(row.Name,Regex.IsMatch(row.Key,@"^[a-z0-9-]{1,100}$",RegexOptions.IgnoreCase)?row.Key:""))
                    .Distinct().Take(30).ToArray();
            }
            var genres=Choices("genres","genreId");var countries=Choices("countries","country_id");
            var people=new List<CinemaPerson>();
            if(root.TryGetProperty("persons",out var persons)&&persons.ValueKind==JsonValueKind.Object)
            foreach(var (key,role) in new[]{("director","Режиссёры"),("operator","Операторы"),("cinematographer","Операторы"),("actors","Актёры")})
            {
                if(!persons.TryGetProperty(key,out var rows)||rows.ValueKind!=JsonValueKind.Array)continue;
                foreach(var row in rows.EnumerateArray().Take(80))
                {
                    if(row.ValueKind!=JsonValueKind.Object)continue;
                    var personName=Text(Field(row,"name"));var english=Text(Field(row,"name_eng"));
                    if(personName.Length==0)personName=english;
                    if(personName.Length is <1 or >150)continue;
                    var id=Identifier(Field(row,"i"));
                    people.Add(new(personName,role,"",PhotoUrl:PhotoUrl(Field(row,"cover"),id),OriginalName:english.Length is >0 and <=150?english:null,SourcePersonId:id));
                }
            }
            return item with
            {
                Description=Text(Field(movie,"description")),OriginalTitle=original.Length>0?original:null,
                Kinopoisk=Score(Field(movie,"rating_kinopoisk")),Imdb=Score(Field(movie,"rating_imdb")),
                People=people.DistinctBy(x=>(Normalize(x.Name),x.Role,x.SourcePersonId)).Take(80).OrderBy(x=>x.Role switch{"Актёры"=>0,"Режиссёры"=>1,_=>2}).ToArray(),
                Genre=string.Join(", ",genres.Select(x=>x.Name)),Country=string.Join(", ",countries.Select(x=>x.Name)),
                GenreKeys=genres.Select(x=>x.Key).Where(x=>x.Length>0).Distinct().ToArray(),CountryKeys=countries.Select(x=>x.Key).Where(x=>x.Length>0).Distinct().ToArray(),CountryKeysComplete=true,Collections=[]
            };
        }
    }
    public static CinemaPerson[] MergePeople(IEnumerable<CinemaPerson> prior,IEnumerable<CinemaPerson> fresh)
    {
        var result=fresh.ToList();
        foreach(var old in prior)
        {
            var index=result.FindIndex(x=>Normalize(x.Name)==Normalize(old.Name)&&x.Role==old.Role&&(x.SourcePersonId==null||old.SourcePersonId==null||x.SourcePersonId==old.SourcePersonId)&&
                (string.IsNullOrWhiteSpace(x.ProfileUrl)||string.IsNullOrWhiteSpace(old.ProfileUrl)||string.Equals(x.ProfileUrl,old.ProfileUrl,StringComparison.OrdinalIgnoreCase))&&
                (x.PageUrl.Length==0||old.PageUrl.Length==0||string.Equals(x.PageUrl,old.PageUrl,StringComparison.OrdinalIgnoreCase)));
            if(index<0){result.Add(old);continue;}
            var current=result[index];
            result[index]=current with
            {
                PageUrl=current.PageUrl.Length>0?current.PageUrl:old.PageUrl,
                ProfileUrl=current.ProfileUrl??old.ProfileUrl,PhotoUrl=current.PhotoUrl??PhotoUrl(old.PhotoUrl,current.SourcePersonId??old.SourcePersonId),
                OriginalName=current.OriginalName??old.OriginalName,SourcePersonId=current.SourcePersonId??old.SourcePersonId
            };
        }
        return result.OrderBy(x=>x.Role switch{"Режиссёры"=>0,"Операторы"=>1,"Актёры"=>2,_=>3}).Take(80).OrderBy(x=>x.Role switch{"Актёры"=>0,"Режиссёры"=>1,_=>2}).ToArray();
    }
}
