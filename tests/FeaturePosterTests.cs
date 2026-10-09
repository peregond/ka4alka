using Kachalka;
using System.Text;
using System.Text.Json;

static class FeaturePosterTests
{
    public static void Run()
    {
        static byte[] Response(string id,string url)=>Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,backdrop=new{url,source="TMDB",tmdbId=1}}));
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS: "+reason);}
        const string id="movies:film",original="https://image.tmdb.org/t/p/w1280/landscape.jpg";
        Check(FeatureBackdrop.FromResponse(Response(id,original),id)==original,"landscape response belongs to the exact catalog identity");
        static bool Rejected(Action action){try{action();return false;}catch(Exception e)when(e is InvalidDataException or JsonException or InvalidOperationException){return true;}}
        Check(Rejected(()=>FeatureBackdrop.FromResponse(Response("series:film",original),id)),"films and series cannot exchange their banner artwork");
        foreach(var url in new[]{"http://image.tmdb.org/t/p/w1280/x.jpg","https://image.tmdb.org.evil.test/t/p/w1280/x.jpg","https://u:p@image.tmdb.org/t/p/w1280/x.jpg","https://image.tmdb.org:123/t/p/w1280/x.jpg","https://image.tmdb.org/t/p/original/x.jpg","https://image.tmdb.org/t/p/w1280/x.jpg?token=secret","https://image.tmdb.org/t/p/w1280/../x.jpg"})
            Check(Rejected(()=>FeatureBackdrop.FromResponse(Response(id,url),id)),"untrusted or unbounded image addresses are rejected");
        Check(FeatureBackdrop.FromResponse(Encoding.UTF8.GetBytes("{\"id\":\"movies:film\",\"backdrop\":null}"),id)==null,"missing artwork keeps the portrait separate from the banner background");
        Check(Rejected(()=>FeatureBackdrop.FromResponse(Encoding.UTF8.GetBytes("{\"id\":\"movies:film\"}"),id)),"malformed responses cannot poison the no-artwork cache");
        Check(FeatureBackdrop.Landscape(1280,720)&&!FeatureBackdrop.Landscape(1280,1920)&&!FeatureBackdrop.Landscape(300,168)&&!FeatureBackdrop.Landscape(1280,100),"portrait, undersized and excessively wide images are rejected");
    }
}
