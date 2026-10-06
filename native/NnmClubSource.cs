using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Kachalka;

// NNM-Club exposes public search results and torrent files without a session.
public sealed class NnmClubSource(SourceClient client)
{
    static readonly Uri Base=new("https://nnmclub.to/forum/");
    static readonly Regex TopicPath=new(@"^viewtopic\.php\?t=\d{1,12}$",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex DownloadPath=new(@"^download\.php\?id=(\d{1,12})$",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex VideoCategory=new(@"(?:сериал|кино|фильм|мультфильм|аниме|video|видео)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex SeriesCategory=new(@"(?:сериал|аниме)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);

    public static Uri QueryUri(string title)=>new(Base,"tracker.php?nm="+Uri.EscapeDataString(title.Trim()));

    public async Task<IReadOnlyList<SourceEntry>> Search(MediaItem item,CancellationToken ct)
    {
        var queries=new[]{item.Title,item.OriginalTitle}
            .Where(query=>!string.IsNullOrWhiteSpace(query))
            .Select(query=>query!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2);
        var entries=new List<SourceEntry>();
        foreach(var query in queries)
        {
            try{entries.AddRange(Parse(await client.Read(QueryUri(query),2*1024*1024,ct),item));}
            catch(Exception) when(!ct.IsCancellationRequested){/* Keep releases from the other title. */}
        }
        return entries.DistinctBy(entry=>entry.Id).OrderByDescending(entry=>entry.Seeds??-1).Take(100).ToArray();
    }

    public static IReadOnlyList<SourceEntry> Parse(byte[] data,MediaItem item)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var html=new HtmlDocument();html.LoadHtml(Encoding.GetEncoding(1251).GetString(data));
        var rows=html.DocumentNode.SelectNodes("//tr[.//a[contains(concat(' ',normalize-space(@class),' '),' topictitle ')]]");
        if(rows==null)return [];
        var entries=new List<SourceEntry>();
        foreach(var row in rows.Take(200))
        {
            var titleLink=row.SelectSingleNode(".//a[contains(concat(' ',normalize-space(@class),' '),' topictitle ')]");
            var categoryLink=row.SelectSingleNode("./td[2]//a[starts-with(@href,'tracker.php?f=')]");
            var downloadLink=row.SelectSingleNode(".//a[starts-with(@href,'download.php?id=')]");
            var topic=titleLink?.GetAttributeValue("href","")??"";
            var download=downloadLink?.GetAttributeValue("href","")??"";
            if(!TopicPath.IsMatch(topic)||!DownloadPath.IsMatch(download))continue;
            var category=HtmlEntity.DeEntitize(categoryLink?.InnerText??"").Trim();
            if(!VideoCategory.IsMatch(category)||item.Section=="Сериалы"&&!SeriesCategory.IsMatch(category)||item.Section=="Фильмы"&&SeriesCategory.IsMatch(category))continue;
            var title=Regex.Replace(HtmlEntity.DeEntitize(titleLink?.InnerText??"").Trim(),@"\s+"," ");
            if(title.Length is 0 or >500)continue;
            var id=DownloadPath.Match(download).Groups[1].Value;
            var sizeText=row.SelectSingleNode("./td[.//u][1]//u")?.InnerText;
            long? size=long.TryParse(sizeText,out var bytes)&&bytes>0?bytes:null;
            var seedText=row.SelectSingleNode("./td[@title='Seeders']")?.InnerText;
            int? seeds=int.TryParse(HtmlEntity.DeEntitize(seedText??"").Trim(),out var count)&&count>=0?count:null;
            var entry=new SourceEntry("NNM-Club:"+id,title,"NNM-Club",new Uri(Base,topic).AbsoluteUri,new Uri(Base,download).AbsoluteUri,null,size,seeds);
            if(!LiveCatalog.Matches(item,entry))continue;
            if(item.Section=="Фильмы"&&item.Year>0&&!Regex.IsMatch(title,@"(?<!\d)"+item.Year+@"(?!\d)"))continue;
            entries.Add(entry);
        }
        return entries.DistinctBy(entry=>entry.Id).ToArray();
    }
}
