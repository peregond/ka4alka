using System.Globalization;
using System.Text.RegularExpressions;
namespace Kachalka;

public record CatalogChoice(string Key,string Label)
{
    public override string ToString()=>Label;
}
public record CatalogSelection(string Genre="",string Country="",int? Year=null,int Rating=0,string Order="date",string Collection="all",string Region="",string HomeCountry="")
{
    public bool IsDefault=>Genre==""&&Country==""&&Year==null&&Rating==0&&Order=="date"&&Collection=="all"&&Region=="";
    public string Filter
    {
        get
        {
            var parts=new List<string>();
            void Add(string prefix,string value){if(value.Length==0)return;if(!Regex.IsMatch(value,"^[a-z]+(?:-[a-z]+)*$"))throw new ArgumentException("Invalid catalog filter");parts.Add(prefix+value);}
            if(Region is not ("" or "all" or "native" or "foreign"))throw new ArgumentException("Invalid catalog region");
            Add("genre-",Genre);if(Year is >=1900 and <=2100)parts.Add("year-"+Year);Add("country-",Country.Length>0?Country:Region=="native"?CatalogRegions.HomeCountry(HomeCountry):"");
            var order=Collection=="popular"?"popular":Collection=="rated"?"rating":Order;
            if(order is not ("date" or "popular" or "rating"))throw new ArgumentException("Invalid catalog order");
            var rating=Collection=="rated"?Math.Max(8,Rating):Rating;
            if(order=="rating"&&rating==0)rating=1;
            if(rating is >0 and <=9)parts.Add("rating-"+rating);
            if(order!="popular")parts.Add("sort-"+order);return string.Join("/",parts);
        }
    }
    public bool Matches(MediaItem item)
    {
        if(Year.HasValue&&item.Year!=Year)return false;
        if(Genre.Length>0&&!item.GenreKeys.Contains(Genre))return false;
        if(Country.Length>0&&!item.CountryKeys.Contains(Country))return false;
        return (Rating==0||CatalogPaging.Rating(item)>=Rating)&&CatalogRegions.Matches(item,Region,HomeCountry);
    }
}
public record CatalogPage(MediaItem[] Items,bool HasNext,CatalogChoice[] Genres,CatalogChoice[] Countries);
public static class CatalogPaging
{
    public const int Size=40,SourceSize=60,Limit=250;
    public static (int Page,int Offset) SourceWindow(int page)
    {
        var offset=(Math.Clamp(page,1,Limit)-1)*Size;
        return (offset/SourceSize+1,offset%SourceSize);
    }
    public static double Rating(MediaItem item)=>double.TryParse(item.Kinopoisk.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&value is >=0 and <=10?value:-1;
    public static int[] Numbers(int current,int? last=null)
    {
        current=Math.Clamp(current,1,Limit);var max=Math.Clamp(last??Math.Min(Limit,Math.Max(10,current+4)),1,Limit);
        var start=Math.Max(1,Math.Min(current-4,max-9));
        return Enumerable.Range(start,Math.Min(10,max-start+1)).ToArray();
    }
}
