using System.Text;
using Kachalka;
static class CatalogPagingTests
{
    sealed class SourcePages:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath=="/api/catalog")
            {
                var payload=System.Text.Json.JsonSerializer.Serialize(new{items=Enumerable.Range(1,40).Select(x=>new{id="movies:window-"+x,section="movies",title="Фильм "+x,year=2024,pageUrl="https://w6.zona.plus/movies/window-"+x}),hasMore=false});
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(payload,Encoding.UTF8,"application/json")});
            }
            var page=int.Parse(System.Text.RegularExpressions.Regex.Match(request.RequestUri!.Query,@"page=(\d+)").Groups[1].Value);
            var cards=string.Concat(Enumerable.Range((page-1)*60+1,60).Select(x=>$"<li class='results-item-wrap'><a itemprop='url' href='/movies/window-{x}'><span itemprop='name'>Фильм {x}</span></a></li>"));
            var next=page<3?$"<link rel='next' href='/movies/filter/sort-date?page={page+1}'>":"";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(cards+next,Encoding.UTF8,"text/html")});
        }
    }

    public static void Run()
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var filtered=new CatalogSelection("komediia","rossiia",2024,7,"rating");
        Check(filtered.Filter=="genre-komediia/year-2024/country-rossiia/rating-7/sort-rating","source filter combines genre, year, country and rating without changing their meanings");
        Check(new CatalogSelection(Collection:"popular").Filter==""&&new CatalogSelection(Collection:"rated").Filter=="rating-8/sort-rating","collections map to verified public catalog routes");
        try{_ = new CatalogSelection(Genre:"../../admin").Filter;throw new Exception("Filter accepted a path");}catch(ArgumentException){Check(true,"catalog rejects injected filter paths");}
        var html="<select><option value='genre-film-nuar'>фильм-нуар</option><option value='country-rossiia'>Россия</option></select><li class='results-item-wrap'><a itemprop='url' href='/movies/fixture'><span itemprop='name'>Фильм</span></a><span class='results-item-year'>2024</span></li><a href='/movies/filter/sort-date?page=2'>Далее</a>";
        var page=LiveCatalog.ParsePage(Encoding.UTF8.GetBytes(html),"Фильмы",1);
        Check(page.Items.Length==1&&page.HasNext&&page.Genres.Single().Key=="film-nuar"&&page.Countries.Single().Label=="Россия","catalog extracts source filter choices and real next-page link");
        Check(!LiveCatalog.ParsePage(Encoding.UTF8.GetBytes(html),"Фильмы",2).HasNext,"current-page or unrelated links cannot create a phantom next page");
        var sixty=string.Concat(Enumerable.Range(1,60).Select(x=>$"<li class='results-item-wrap'><a itemprop='url' href='/movies/fixture-{x}'><span itemprop='name'>Фильм {x}</span></a></li>"));
        Check(LiveCatalog.Parse(Encoding.UTF8.GetBytes(sixty),"Фильмы").Count==60,"source parser retains all 60 cards instead of discarding 20");
        Check(CatalogPaging.SourceWindow(1)==(1,0)&&CatalogPaging.SourceWindow(2)==(1,40)&&CatalogPaging.SourceWindow(3)==(2,20),"40-card pages cover 60-card source pages without skipping films");
        using(var client=new SourceClient(new SourcePages()))
        {
            var apiPage=new OnlineIndexClient(client,new Uri("https://index.example.test/")).BrowsePage("Фильмы",1,CancellationToken.None).GetAwaiter().GetResult();
            Check(apiPage.Items.Length==40&&!apiPage.HasNext,"native client respects backend hasMore=false even on a full last page");
            var catalog=new LiveCatalog(client);var all=new List<MediaItem>();
            for(var n=1;n<=5;n++)
            {
                var result=catalog.BrowsePage("Фильмы",n,new CatalogSelection(Genre:"fixture-pages"),CancellationToken.None,true).GetAwaiter().GetResult();
                Check(result.HasNext==(n<5),"source next-page state matches UI page "+n);all.AddRange(result.Items);
            }
            Check(all.Select(x=>x.Title).SequenceEqual(Enumerable.Range(1,180).Select(x=>"Фильм "+x)),"native source browsing retains all films across 60-to-40 page boundaries");
        }
        var nextLink=html.Replace("<a href='/movies/filter/sort-date?page=2'>Далее</a>","<link rel='next' href='/movies/filter/sort-date?page=2'>");
        Check(LiveCatalog.ParsePage(Encoding.UTF8.GetBytes(nextLink),"Фильмы",1).HasNext,"head rel=next link keeps the catalog navigable without a visible anchor");
        Check(BundledCatalog.Count("Фильмы")>=2000,"application includes at least 2000 distinct real movies");
        var first=BundledCatalog.Page("Фильмы",1);var distant=BundledCatalog.Page("Фильмы",50);
        Check(first.Items.Length==40&&first.HasNext&&distant.Items.Length==40&&!first.Items.Select(x=>x.Id).Intersect(distant.Items.Select(x=>x.Id)).Any(),"offline pages 1 and 50 contain distinct movies");
        var last=(BundledCatalog.Count("Фильмы")+39)/40;
        Check(!BundledCatalog.Page("Фильмы",last).HasNext&&BundledCatalog.Page("Фильмы",last+1).Items.Length==0,"snapshot reports its actual last page");
        Check(CatalogPaging.Numbers(1).SequenceEqual(Enumerable.Range(1,10))&&CatalogPaging.Numbers(12).Contains(12)&&CatalogPaging.Numbers(CatalogPaging.Limit).Max()==CatalogPaging.Limit,"page numbers cover current selection within source limits");
        Check(CatalogPaging.Numbers(3,3).SequenceEqual([1,2,3]),"known last page removes impossible numbered destinations");
        var item=new MediaItem(1,"Фильм","Фильмы","комедия",2024,"7,5","—","#526B69"){GenreKeys=["komediia"],CountryKeys=["rossiia"]};
        Check(filtered.Matches(item)&&!filtered.Matches(item with{CountryKeys=["ssha"]})&&!filtered.Matches(item with{Kinopoisk="—"})&&!filtered.Matches(item with{Kinopoisk="99"}),"saved filters combine country and genre and reject missing or invalid ratings");
    }
}
