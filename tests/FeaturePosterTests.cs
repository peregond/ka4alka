using Kachalka;
using System.Text;
using System.Text.Json;

static class FeaturePosterTests
{
    public static void Run()
    {
        static byte[] Response(string id,string url)=>Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{d=new[]{new{id,i=new{imageUrl=url}}}}));
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS: "+reason);}
        const string id="tt1234567",original="https://m.media-amazon.com/images/M/real-poster._V1_.jpg";
        Check(FeaturePoster.FromSuggestion(Response(id,original),id)=="https://m.media-amazon.com/images/M/real-poster._V1_QL85_UX1000_.jpg","feature artwork uses a bounded-resolution poster for the exact IMDb identity");
        Check(FeaturePoster.FromSuggestion(Response("tt7654321",original),id)==null,"similarly named films cannot replace a feature with another film's artwork");
        foreach(var url in new[]{"http://m.media-amazon.com/images/M/poster.jpg","https://m.media-amazon.com.evil.test/images/M/poster.jpg","https://user:password@m.media-amazon.com/images/M/poster.jpg"})
            Check(FeaturePoster.FromSuggestion(Response(id,url),id)==null,"feature poster rejects an unrelated or credential-bearing image URL");
        Check(FeaturePoster.FromSuggestion(Encoding.UTF8.GetBytes("{\"d\":[]}"),id)==null,"a missing high-resolution image retains the existing catalog artwork");
    }
}
