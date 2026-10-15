using System.Net;
using System.Text.RegularExpressions;
using System.Windows;

namespace Kachalka;

public static class FeatureSummary
{
    public static string Shorten(string? description)
    {
        if(string.IsNullOrWhiteSpace(description))return "";
        var text=WebUtility.HtmlDecode(description[..Math.Min(description.Length,8192)]);
        text=Regex.Replace(Regex.Replace(text,@"<[^>]*>"," "),@"\s+"," ").Trim();
        if(text is "Описание пока недоступно." or "Описание временно недоступно." or "Загружаем описание…")return "";
        if(text.Length<=240)return text;
        var end=text.LastIndexOf(' ',240);if(end<160)end=240;
        if(char.IsHighSurrogate(text[end-1])&&char.IsLowSurrogate(text[end]))end--;
        return text[..end].TrimEnd(' ','.',',',';',':')+"…";
    }
}

public partial class MainWindow
{
    Func<MediaItem,CancellationToken,Task<MediaItem>>? featureSummaryProvider;
    string KnownFeatureSummary(MediaItem item)
    {
        if(item.Section=="Игры"&&gameDetails.TryGetValue(item.Id,out var game)&&FeatureSummary.Shorten(game.Item.Description) is {Length:>0} gameSummary)return gameSummary;
        if(cardMetadata.TryGetValue(item.Id,out var cached)&&cached.IsCompletedSuccessfully&&FeatureSummary.Shorten(cached.Result.Description) is {Length:>0} summary)return summary;
        return FeatureSummary.Shorten(item.Description);
    }
    void UpdateFeatureSummary(FeatureCarouselView view,MediaItem item,bool loading=false)
    {
        view.Summary.Text=KnownFeatureSummary(item);
        view.SummaryPlaceholder.Visibility=loading&&view.Summary.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
    }
    void StartFeatureSummary(FeatureCarouselView view,MediaItem item)
    {
        UpdateFeatureSummary(view,item);
        // Steam's existing banner detail request supplies its description; do not fetch it twice.
        if(view.Summary.Text.Length>0||item.Section=="Игры"||(!item.IsLive&&featureSummaryProvider==null)||metadataLifetime.IsCancellationRequested||!view.Frame.IsLoaded||view.SummaryRequest!=null)return;
        var request=view.SummaryRequest=CancellationTokenSource.CreateLinkedTokenSource(metadataLifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(15));
        UpdateFeatureSummary(view,item,true);
        _=LoadFeatureSummary(view,item,request);
    }
    async Task LoadFeatureSummary(FeatureCarouselView view,MediaItem item,CancellationTokenSource request)
    {
        bool Active()=>!closed&&!closing&&!request.IsCancellationRequested&&ReferenceEquals(carousel,view)&&ReferenceEquals(view.Bound,item)&&view.Frame.IsLoaded;
        try
        {
            // Rapid arrow clicks only request text for the slide the user settles on.
            await Task.Delay(180,request.Token);
            // A banner subscribes to the common metadata cache. Leaving it must not cancel
            // the same lookup used by a catalog card or an opened detail page.
            var pending=featureSummaryProvider?.Invoke(item,request.Token)??Metadata(item,false,metadataLifetime.Token);
            var fresh=await pending.WaitAsync(request.Token);
            if(Active()&&fresh.Id==item.Id)
            {
                var text=FeatureSummary.Shorten(fresh.Description);
                if(text.Length>0)view.Summary.Text=text;
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(Active())ErrorLog.Write(error);}
        finally
        {
            if(ReferenceEquals(view.SummaryRequest,request))
            {
                view.SummaryRequest=null;view.SummaryPlaceholder.Visibility=Visibility.Collapsed;
            }
            request.Dispose();
        }
    }
}
