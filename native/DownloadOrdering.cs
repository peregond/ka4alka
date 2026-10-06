using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kachalka;

public enum DownloadSort { Newest, Name, Speed, Size, Progress }

// Sort the view rather than the queue. Transfer updates can refresh row values
// without moving a row away from the action the user is about to select.
public static class DownloadOrdering
{
    static readonly StringComparer Names=StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"),true);

    public static DownloadItemComparer Comparer(DownloadSort sort)=>new(sort);
    public static IEnumerable<DownloadItem> Sort(IEnumerable<DownloadItem> items,DownloadSort sort)=>items.OrderBy(x=>x,Comparer(sort));

    public sealed class DownloadItemComparer(DownloadSort sort) : IComparer<DownloadItem>,System.Collections.IComparer
    {
        public int Compare(DownloadItem? left,DownloadItem? right)
        {
            if(ReferenceEquals(left,right))return 0;
            if(left==null)return 1;if(right==null)return -1;
            int result=sort switch
            {
                DownloadSort.Name=>Names.Compare(left.DisplayName,right.DisplayName),
                DownloadSort.Speed=>Speed(right).CompareTo(Speed(left)),
                DownloadSort.Size=>Size(left,right),
                DownloadSort.Progress=>Progress(left,right),
                _=>right.AddedUtc.CompareTo(left.AddedUtc)
            };
            if(result!=0)return result;
            result=right.AddedUtc.CompareTo(left.AddedUtc);
            return result!=0?result:StringComparer.Ordinal.Compare(left.Id,right.Id);
        }
        int System.Collections.IComparer.Compare(object? left,object? right)=>Compare((DownloadItem?)left,(DownloadItem?)right);
        static decimal Speed(DownloadItem item)=>(decimal)Math.Max(0,item.DownloadRate)+Math.Max(0,item.UploadRate);
        static int Size(DownloadItem left,DownloadItem right)
        {
            var leftKnown=left.TotalBytes.HasValue&&left.TotalBytes.Value>=0;
            var rightKnown=right.TotalBytes.HasValue&&right.TotalBytes.Value>=0;
            if(leftKnown!=rightKnown)return leftKnown?-1:1;
            return leftKnown?right.TotalBytes!.Value.CompareTo(left.TotalBytes!.Value):0;
        }
        static int Progress(DownloadItem left,DownloadItem right)
        {
            var leftKnown=double.IsFinite(left.Progress);var rightKnown=double.IsFinite(right.Progress);
            if(leftKnown!=rightKnown)return leftKnown?-1:1;
            return leftKnown?Math.Clamp(right.Progress,0,100).CompareTo(Math.Clamp(left.Progress,0,100)):0;
        }
    }

    // Older queue files retained insertion order but had no creation timestamp.
    // Assign ordered dates once, before any recorded dates, then persist them.
    public static bool AssignMissingAddedUtc(IEnumerable<DownloadItem> items,DateTime legacyBaselineUtc)
    {
        var queue=items.ToArray();var missing=queue.Count(x=>x.AddedUtc==default);
        if(missing==0)return false;
        var baseline=legacyBaselineUtc.Kind==DateTimeKind.Local?legacyBaselineUtc.ToUniversalTime():DateTime.SpecifyKind(legacyBaselineUtc,DateTimeKind.Utc);
        if(baseline==default)baseline=DateTime.UnixEpoch;
        var anchor=queue.Where(x=>x.AddedUtc!=default).Select(x=>x.AddedUtc.Ticks).Append(baseline.Ticks).Min();
        long tick=Math.Max(1,anchor-missing);
        foreach(var item in queue.Where(x=>x.AddedUtc==default))item.AddedUtc=new DateTime(tick++,DateTimeKind.Utc);
        return true;
    }

    // An exact catalog association can fill an old queue entry's missing poster.
    // Do not infer a film from loose substring or torrent-tag matches: remakes
    // and identically named series must keep their placeholder when ambiguous.
    public static bool AssociateMedia(DownloadItem item,IEnumerable<MediaItem> catalog)
    {
        if(!string.IsNullOrWhiteSpace(item.ImageUrl))return false;
        var title=Normalize(item.DisplayName);if(title.Length==0)return false;
        var matches=catalog.Where(media=>media.Cinema&&
            (string.IsNullOrWhiteSpace(item.MediaSection)||media.Section==item.MediaSection)&&
            (Normalize(media.Title)==title||!string.IsNullOrWhiteSpace(media.OriginalTitle)&&Normalize(media.OriginalTitle)==title))
            .GroupBy(media=>(media.Section,media.Year,Title:Normalize(media.Title),Original:Normalize(media.OriginalTitle??""))).ToArray();
        if(matches.Length!=1)return false;
        var match=matches[0].FirstOrDefault(media=>!string.IsNullOrWhiteSpace(media.ImageUrl));
        if(match==null)return false;
        item.ImageUrl=match.ImageUrl;item.MediaTitle=match.Title;item.MediaSection=match.Section;item.Refresh();return true;
    }

    static string Normalize(string value)=>Regex.Replace(value.Normalize(NormalizationForm.FormKC).Replace('ё','е').Replace('Ё','Е').ToUpperInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
}
