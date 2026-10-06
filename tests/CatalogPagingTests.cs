using System.Text;
using Kachalka;
static class CatalogPagingTests
{
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
        Check(CatalogPaging.Numbers(1).SequenceEqual(Enumerable.Range(1,10))&&CatalogPaging.Numbers(12).Contains(12)&&CatalogPaging.Numbers(40).Max()==40,"page numbers cover current selection within source limits");
        Check(CatalogPaging.Numbers(3,3).SequenceEqual([1,2,3]),"known last page removes impossible numbered destinations");
        var item=new MediaItem(1,"Фильм","Фильмы","комедия",2024,"7,5","—","#526B69"){GenreKeys=["komediia"],CountryKeys=["rossiia"]};
        Check(filtered.Matches(item)&&!filtered.Matches(item with{CountryKeys=["ssha"]})&&!filtered.Matches(item with{Kinopoisk="—"}),"saved filters combine country and genre and never interpret missing ratings as real scores");
    }
}
