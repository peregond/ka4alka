using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
namespace Kachalka;

// Public server response only: no TMDB credential is shipped in the desktop app.
public static class FeatureBackdrop
{
    // IMDb search cards have no Zona/catalog id. Reuse the exact-identity metadata endpoint,
    // whose text-free/English landscape is also suitable behind the Russian title.
    public static string? IdentityFor(MediaItem item)=>OnlineIndexClient.IdFor(item)??
        (item.Cinema&&Regex.IsMatch(item.ImdbId??"",@"^tt\d{5,10}$")?"imdb:"+(item.Section=="Сериалы"?"series":"movies")+":"+item.ImdbId:null);
    public static string RequestPath(string id,string language)
    {
        if(id.StartsWith("imdb:",StringComparison.Ordinal))
        {var parts=id.Split(':');return "api/localized-media?language=en&imdb="+Uri.EscapeDataString(parts[2])+"&section="+parts[1];}
        return "api/backdrop?id="+Uri.EscapeDataString(id)+"&language="+AppLanguage.Normalize(language);
    }
    public static string? ReadResponse(byte[] bytes,string id)
    {
        if(!id.StartsWith("imdb:",StringComparison.Ordinal))return FromResponse(bytes,id);
        var parts=id.Split(':');return EnglishMedia.Parse(bytes,parts[2],parts[1])?.Backdrop;
    }
    public static string? ValidUrl(string? value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="image.tmdb.org"||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return null;
        return Regex.IsMatch(uri.AbsolutePath,@"^/t/p/w1280/[a-zA-Z0-9_-]+\.jpg$")?uri.AbsoluteUri:null;
    }
    public static string? FromResponse(byte[] bytes,string id)
    {
        using var document=JsonDocument.Parse(bytes);var root=document.RootElement;
        if(!root.TryGetProperty("id",out var identity)||identity.ValueKind!=JsonValueKind.String||identity.GetString()!=id)throw new InvalidDataException("Фон принадлежит другой карточке.");
        if(!root.TryGetProperty("backdrop",out var backdrop))throw new InvalidDataException("Неверный ответ источника фонов.");
        if(backdrop.ValueKind==JsonValueKind.Null)return null;
        if(backdrop.ValueKind!=JsonValueKind.Object||!backdrop.TryGetProperty("source",out var source)||source.GetString()!="TMDB"||!backdrop.TryGetProperty("url",out var url)||url.ValueKind!=JsonValueKind.String||ValidUrl(url.GetString()) is not {} valid)throw new InvalidDataException("Неверный адрес фона.");
        return valid;
    }
    // TMDB backdrops are about 16:9. Steam's library hero is a 3.1:1 strip, so games accept a wider ratio.
    public static bool Landscape(int width,int height,double widest=2.5)=>width>=780&&height>0&&(double)width/height is var ratio&&ratio>=1.6&&ratio<=widest;
}
