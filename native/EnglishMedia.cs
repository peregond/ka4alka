using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Kachalka;

public sealed record EnglishMedia(string Language,string Title,string Description,string? Poster,string? Backdrop,string[] Genres,string[] Countries,string[] Actors,string[] Directors)
{
    public CinemaPerson[] People=>Directors.Select(name=>new CinemaPerson(name,"Режиссёры","")).Concat(Actors.Select(name=>new CinemaPerson(name,"Актёры",""))).ToArray();
    public static string? PosterUrl(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="image.tmdb.org"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,@"^/t/p/w500/[a-zA-Z0-9_-]+\.jpg$")?uri.AbsoluteUri:null;
    public static EnglishMedia? Parse(byte[] bytes,string identity,string section)
    {
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.GetProperty("id").GetString()!=identity||root.GetProperty("section").GetString()!=section)throw new InvalidDataException("Localization belongs to another title.");
        var row=root.GetProperty("item");if(row.ValueKind==JsonValueKind.Null)return null;
        var data=row.Deserialize<EnglishMedia>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(data==null||data.Language!="en"||string.IsNullOrWhiteSpace(data.Title)||data.Title.Length>300||data.Description==null||data.Description.Length>4000||data.Genres==null||data.Countries==null||data.Actors==null||data.Directors==null)throw new InvalidDataException("Invalid localization.");
        return data with{Poster=PosterUrl(data.Poster),Backdrop=FeatureBackdrop.ValidUrl(data.Backdrop),Genres=data.Genres.Take(6).ToArray(),Countries=data.Countries.Take(4).ToArray(),Actors=data.Actors.Take(12).ToArray(),Directors=data.Directors.Take(3).ToArray()};
    }
}

