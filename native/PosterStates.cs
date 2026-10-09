using System.Windows;
namespace Kachalka;

// Shows queue state on catalog posters: a progress line while a title downloads and a check once it is complete.
// A title is matched to a queue entry by the catalog page address both of them already store.
public partial class MainWindow
{
    static string PageKey(string? url)=>(url??"").Trim().TrimEnd('/').ToLowerInvariant();
    void ApplyPosterDownloads(IEnumerable<MediaItem> items)
    {
        if(!ready||closed||downloads==null)return;
        Dictionary<string,(bool Done,double Percent)>? map=null;
        foreach(var item in downloads.Items)
        {
            var key=PageKey(item.MediaPageUrl);if(key.Length==0)continue;
            map??=[];
            var done=item.Completed;var percent=done?100:Math.Clamp(item.Progress,0,100);
            if(map.TryGetValue(key,out var previous)){done|=previous.Done;percent=Math.Max(percent,previous.Percent);}
            map[key]=(done,percent);
        }
        foreach(var media in items)
        {
            if(map!=null&&media.PageUrl!=null&&map.TryGetValue(PageKey(media.PageUrl),out var state))media.SetDownloadState(state.Done,state.Percent);
            else media.SetDownloadState(false,-1);
        }
    }
    void ApplyPosterDownloads()
    {
        if(section is not ("Фильмы" or "Сериалы" or "Сохранённое")||current!=null)return;
        IEnumerable<MediaItem> shown=catalogDisplay;
        foreach(var shelf in discoveryShelves.Values)shown=shown.Concat(shelf.Items);
        if(carousel!=null)shown=shown.Concat(carousel.Slides);
        ApplyPosterDownloads(shown);
    }
}
