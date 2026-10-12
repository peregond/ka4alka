namespace Kachalka;

public sealed record HiddenCatalogTitle(string Identity,int CatalogId,string Title,string Section,int Year);
public static class HiddenCatalog
{
    static string Key(MediaItem item)=>string.IsNullOrWhiteSpace(item.ImdbId)&&string.IsNullOrWhiteSpace(item.PageUrl)?item.Section+"|local:"+item.Id:MediaIdentity.StableKey(item);
    public static HiddenCatalogTitle Title(MediaItem item)
    {
        item=MediaIdentity.Enrich(item);return new(Key(item),item.Id,item.Title,item.Section,item.Year);
    }
    public static bool TitleHidden(MediaItem item,IEnumerable<HiddenCatalogTitle> hidden)
    {
        var key=Key(MediaIdentity.Enrich(item));
        return hidden.Any(row=>row.Section==item.Section&&(row.CatalogId==item.Id||row.Identity==key));
    }
    public static CatalogChoice[] Genres(MediaItem item,IEnumerable<CatalogChoice>? choices=null)
    {
        var labels=item.AllGenres.Split(',').Select(MediaIdentity.Normalize).ToHashSet();
        return (choices??CatalogChoices.Genres).Where(choice=>item.GenreKeys.Contains(choice.Key)||labels.Contains(MediaIdentity.Normalize(choice.Label))).DistinctBy(choice=>choice.Key).ToArray();
    }
    public static bool Hidden(MediaItem item,Preferences prefs,IEnumerable<CatalogChoice>? choices=null)=>
        TitleHidden(item,prefs.HiddenTitles)||Genres(item,choices).Any(choice=>prefs.HiddenGenres.Contains(choice.Key));
}
