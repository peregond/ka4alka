using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
namespace Kachalka;

// Public server response only: no TMDB credential is shipped in the desktop app.
public static class FeatureBackdrop
{
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
    public static bool Landscape(int width,int height)=>width>=780&&height>0&&(double)width/height is >=1.6 and <=2.5;
}
