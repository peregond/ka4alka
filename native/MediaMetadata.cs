namespace Kachalka;

public record MediaMetadataResult(MediaItem Item,bool Available);

// Detail sources settle independently. A slow index cannot hide a description
// and credits that the catalog page has already returned.
public static class MediaMetadata
{
    public static bool HasDescription(MediaItem item)=>!string.IsNullOrWhiteSpace(item.Description)&&item.Description is not "Описание временно недоступно." and not "Загружаем описание…";
    public static bool HasFullDetails(MediaItem item)=>HasDescription(item)&&item.People.Length>0;
    public static bool Useful(MediaItem item)=>HasDescription(item)||item.People.Length>0||!string.IsNullOrWhiteSpace(item.OriginalTitle)||item.GenreKeys.Length>0||!string.IsNullOrWhiteSpace(item.Genre)||item.CountryKeys.Length>0||item.Kinopoisk!="—"||item.Imdb!="—";

    public static MediaItem Merge(MediaItem prior,MediaItem fresh)=>prior with
    {
        Description=HasDescription(fresh)?fresh.Description:HasDescription(prior)?prior.Description:null,
        OriginalTitle=string.IsNullOrWhiteSpace(fresh.OriginalTitle)?prior.OriginalTitle:fresh.OriginalTitle,
        ImageUrl=string.IsNullOrWhiteSpace(fresh.ImageUrl)?prior.ImageUrl:fresh.ImageUrl,
        OnlineId=fresh.OnlineId??prior.OnlineId,ImdbId=fresh.ImdbId??prior.ImdbId,
        Kinopoisk=Score(fresh.Kinopoisk)?fresh.Kinopoisk:prior.Kinopoisk,
        Imdb=Score(fresh.Imdb)?fresh.Imdb:prior.Imdb,
        Genre=string.IsNullOrWhiteSpace(fresh.Genre)?prior.Genre:fresh.Genre,Country=string.IsNullOrWhiteSpace(fresh.Country)?prior.Country:fresh.Country,
        GenreKeys=fresh.GenreKeys.Length>0?fresh.GenreKeys:prior.GenreKeys,CountryKeys=fresh.CountryKeys.Length>0?fresh.CountryKeys:prior.CountryKeys,
        People=fresh.People.Length>0?ZonaMovieMetadata.MergePeople(prior.People,fresh.People):prior.People,Awards=fresh.Awards.Length>0?fresh.Awards:prior.Awards,Collections=fresh.Collections.Length>0?fresh.Collections:prior.Collections
    };
    static bool Score(string value)=>double.TryParse(value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var score)&&score is >0 and <=10;

    public static async Task<MediaMetadataResult> Load(MediaItem item,Func<CancellationToken,Task<MediaItem>> indexed,Func<CancellationToken,Task<MediaItem>> direct,Action<MediaItem>? updated=null,CancellationToken ct=default,TimeSpan? timeout=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout??TimeSpan.FromSeconds(15));
        async Task<MediaItem?> Read(Func<CancellationToken,Task<MediaItem>> source)
        {
            try
            {
                var fresh=await source(deadline.Token).WaitAsync(deadline.Token);
                return fresh.Id==item.Id?fresh:null;
            }
            catch(Exception) when(!ct.IsCancellationRequested){return null;}
        }
        var indexTask=Read(indexed);var directTask=Read(direct);
        var remaining=new List<Task<MediaItem?>>{indexTask,directTask};MediaItem? indexResult=null,directResult=null;
        var merged=item;var available=false;
        while(remaining.Count>0)
        {
            var completed=await Task.WhenAny(remaining);remaining.Remove(completed);var fresh=await completed;
            ct.ThrowIfCancellationRequested();
            if(ReferenceEquals(completed,indexTask))indexResult=fresh;else directResult=fresh;
            if(fresh==null)continue;
            available=true;merged=indexResult==null?item:Merge(item,indexResult);
            if(directResult!=null)merged=Merge(merged,directResult);
            updated?.Invoke(merged);
        }
        return new(merged,available);
    }
}
