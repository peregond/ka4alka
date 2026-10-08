using System.Text.RegularExpressions;
namespace Kachalka;

// Country keys describe production countries supplied by the catalog. They
// cannot establish a film's spoken language or where individual scenes were shot.
public static class CatalogRegions
{
    public const string DefaultHomeCountry="rossiia";
    public static string? CountryKey(string? value)
    {
        var key=value?.Trim().ToLowerInvariant();
        return key is {Length:>0 and <=80}&&Regex.IsMatch(key,@"^[a-z]+(?:-[a-z]+)*$")?key:null;
    }
    public static string HomeCountry(string? value)
    {
        var key=CountryKey(value);
        return key!=null&&CatalogChoices.Countries.Any(country=>country.Key==key)?key:DefaultHomeCountry;
    }
    public static string[] CountryKeys(IEnumerable<string>? values)=>values?.Select(CountryKey).OfType<string>().Distinct(StringComparer.Ordinal).Take(30).ToArray()??[];
    public static bool IsNative(MediaItem item,string? homeCountry)=>CountryKeys(item.CountryKeys).Contains(HomeCountry(homeCountry));
    public static bool IsForeign(MediaItem item,string? homeCountry)
    {
        var countries=CountryKeys(item.CountryKeys);
        return item.CountryKeysComplete&&countries.Length>0&&!countries.Contains(HomeCountry(homeCountry));
    }
    public static bool Matches(MediaItem item,string? region,string? homeCountry)=>region switch
    {
        "native"=>IsNative(item,homeCountry),"foreign"=>IsForeign(item,homeCountry),"" or null or "all"=>true,_=>false
    };
    public static MediaItem[] Select(IEnumerable<MediaItem> items,string region,string? homeCountry,int count=8)=>items
        .Where(item=>Matches(item,region,homeCountry)).DistinctBy(item=>item.Id).Take(Math.Clamp(count,0,CatalogPaging.Size)).ToArray();
}
