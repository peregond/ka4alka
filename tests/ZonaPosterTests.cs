using Kachalka;
using System.Text;
using System.Text.Json;

static class ZonaPosterTests
{
    public static void Run()
    {
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS: "+reason);}
        const string cover="https://img3.zonapic.com/images/film_240/5626/5626362.jpg";
        Check(ZonaPoster.Normalize(cover)==cover&&ZonaPoster.Normalize("//img3.zonapic.com/images/film_240/5626/5626362.jpg")==cover&&ZonaPoster.Normalize("  "+cover+" ")==cover,"a catalog cover is recognised, including a protocol-relative address");
        foreach(var bad in new[]
        {
            null,"","http://img3.zonapic.com/images/film_240/5626/5626362.jpg","https://img5.zonapic.com/images/film_240/5626/5626362.jpg","https://img3.zonapic.com.evil.test/images/film_240/5626/5626362.jpg",
            "https://user@img3.zonapic.com/images/film_240/5626/5626362.jpg","https://img3.zonapic.com:8443/images/film_240/5626/5626362.jpg",cover+"?x=1",cover+"#x",
            "https://img3.zonapic.com/images/actor/972/972478.jpg","https://img3.zonapic.com/images/film_big/5626/5626362.jpg","https://img3.zonapic.com/images/film_240/5626/5626362.svg",
            "https://image.tmdb.org/t/p/w500/x.jpg","/images/film_240/5626/5626362.jpg","javascript:alert(1)"
        })
            Check(ZonaPoster.Normalize(bad)==null,"only the image host and path shape of a film cover are accepted: "+(bad??"null"));
        Check(ZonaPoster.ViaSite(cover)=="api/poster?src="+Uri.EscapeDataString(cover)&&ZonaPoster.ViaSite("https://image.tmdb.org/t/p/w500/x.jpg")==null&&ZonaPoster.ViaSite(null)==null,"the site's proxy is asked only for covers it would serve");

        static byte[] Page(string head,string body="")=>Encoding.UTF8.GetBytes("<html><head>"+head+"</head><body>"+body+"</body></html>");
        Check(ZonaPoster.FromPage(Page($"<meta property=\"og:image\" content=\"{cover}\">"))==cover,"a page names its own cover in Open Graph");
        Check(ZonaPoster.FromPage(Page($"<link rel=\"image_src\" href=\"{cover}\">"))==cover,"the old image_src link is understood");
        Check(ZonaPoster.FromPage(Page("",$"<div itemscope><meta itemprop=\"image\" content=\"{cover}\"></div>"))==cover,"the schema.org image of the page is understood");
        const string other="https://img1.zonapic.com/images/film_240/1/2.jpg";
        Check(ZonaPoster.FromPage(Page($"<meta property=\"og:image\" content=\"{cover}\">",$"<meta itemprop=\"image\" content=\"{other}\">"))==cover,"the page's own cover wins over images further down");
        Check(ZonaPoster.FromPage(Page("",$"<ul><li class=\"results-item-wrap big\"><meta itemprop=\"image\" content=\"{other}\"></li></ul>"))==null,"a recommended film's cover is never taken for the page's own");
        Check(ZonaPoster.FromPage(Page("<meta property=\"og:image\" content=\"https://example.test/logo.png\">"))==null&&ZonaPoster.FromPage(Page(""))==null,"a page without a catalog cover yields none");

        static JsonElement Json(string text){using var document=JsonDocument.Parse(text);return document.RootElement.Clone();}
        Check(ZonaPoster.FromJson(Json($"{{\"name_rus\":\"Южный парк\",\"cover\":\"{cover}\"}}"))==cover,"the catalog's JSON cover field is used");
        Check(ZonaPoster.FromJson(Json($"{{\"thumb\":\"{other}\",\"description\":\"текст\"}}"))==other,"any field of the JSON that is a catalog cover is found");
        Check(ZonaPoster.FromJson(Json($"{{\"thumb\":\"{other}\",\"cover\":\"{cover}\"}}"))==cover,"the usual field wins over an arbitrary one");
        Check(ZonaPoster.FromJson(Json("{\"cover\":\"https://example.test/a.jpg\",\"year\":1997}"))==null&&ZonaPoster.FromJson(Json("[1]"))==null,"JSON without a catalog cover yields none");
    }
}
