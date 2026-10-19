namespace Kachalka;

// The identical trusted poster routes are used by both native interfaces.
public static class CoverSource
{
    public static string? ProxyPath(string url)
    {
        if(ZonaPoster.ViaSite(url) is {} zona)return zona;
        if(EnglishMedia.PosterUrl(url) is {} poster)return "api/backdrop-image?size=w500&file="+Uri.EscapeDataString("/"+new Uri(poster).Segments.Last());
        return null;
    }
    public static async Task<byte[]> ReadAsync(SourceClient source,OnlineIndexClient online,string url,int max,CancellationToken ct)
    {
        if(url.StartsWith("//",StringComparison.Ordinal))url="https:"+url;
        var proxy=ProxyPath(url);
        if(proxy==null)return await source.Read(SourceClient.WebUri(url),max,ct);
        using var direct=CancellationTokenSource.CreateLinkedTokenSource(ct);direct.CancelAfter(TimeSpan.FromSeconds(8));
        try{return await source.Read(SourceClient.WebUri(url),max,direct.Token);}
        catch(Exception)when(!ct.IsCancellationRequested){}
        return await online.ReadSite(proxy,max,ct);
    }
}
