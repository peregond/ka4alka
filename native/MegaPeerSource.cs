using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Kachalka;

// MegaPeer's public browse page and torrent downloads use Windows-1251 URLs/content.
public sealed class MegaPeerSource(SourceClient client)
{
    static readonly Uri Base=new("https://megapeer.vip/");
    static readonly Regex TopicPath=new(@"^/torrent/(\d{1,12})/[a-z0-9_-]{1,240}$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex DownloadPath=new(@"^/download/(\d{1,12})$",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex SizePattern=new(@"(?i)(\d+(?:[.,]\d+)?)\s*(TB|GB|MB|KB|ТБ|ГБ|МБ|КБ)",RegexOptions.CultureInvariant|RegexOptions.Compiled);

    static Encoding PageEncoding
    {
        get{Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);return Encoding.GetEncoding(1251);}
    }

    public static Uri QueryUri(string title)
    {
        // The site's title-search mode is stype=0. UTF-8 escaping Cyrillic can return 404.
        var encoded=string.Concat(PageEncoding.GetBytes(title.Trim()).Select(value=>"%"+value.ToString("X2",CultureInfo.InvariantCulture)));
        return new Uri(Base,"browse.php?search="+encoded+"&stype=0");
    }

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
            catch(Exception) when(!ct.IsCancellationRequested){/* A second title can still find releases. */}
        }
        return entries.DistinctBy(entry=>entry.Id).OrderByDescending(entry=>entry.Seeds??-1).Take(100).ToArray();
    }

    public static IReadOnlyList<SourceEntry> Parse(byte[] data,MediaItem item)
    {
        var html=new HtmlDocument();html.LoadHtml(PageEncoding.GetString(data));
        var rows=html.DocumentNode.SelectNodes("//tr[contains(concat(' ',normalize-space(@class),' '),' table_fon ')]");
        if(rows==null)return [];
        var entries=new List<SourceEntry>();
        foreach(var row in rows.Take(200))
        {
            var titleLink=row.SelectSingleNode("./td[2]//a[contains(concat(' ',normalize-space(@class),' '),' url ')]");
            var downloadLink=row.SelectSingleNode("./td[2]//a[starts-with(@href,'/download/')]");
            var topic=titleLink?.GetAttributeValue("href","")??"";
            var download=downloadLink?.GetAttributeValue("href","")??"";
            var topicMatch=TopicPath.Match(topic);
            var downloadMatch=DownloadPath.Match(download);
            if(!topicMatch.Success||!downloadMatch.Success||topicMatch.Groups[1].Value!=downloadMatch.Groups[1].Value)continue;
            var title=Regex.Replace(HtmlEntity.DeEntitize(titleLink?.InnerText??"").Trim(),@"\s+"," ");
            if(title.Length is 0 or >500)continue;
            var sizeText=HtmlEntity.DeEntitize(row.SelectSingleNode("./td[3]")?.InnerText??"").Trim();
            var sizeMatch=SizePattern.Match(sizeText);
            long? size=null;
            if(sizeMatch.Success&&double.TryParse(sizeMatch.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var amount))
            {
                var factor=sizeMatch.Groups[2].Value.ToUpperInvariant() switch{"TB" or "ТБ"=>1099511627776d,"GB" or "ГБ"=>1073741824d,"MB" or "МБ"=>1048576d,_=>1024d};
                var bytes=amount*factor;
                if(bytes>0&&bytes<long.MaxValue)size=(long)bytes;
            }
            var seedText=row.SelectSingleNode("./td[last()]//img[@alt='S']/following-sibling::font[1]")?.InnerText;
            int? seeds=int.TryParse(HtmlEntity.DeEntitize(seedText??"").Trim(),out var count)&&count>=0?count:null;
            var id=downloadMatch.Groups[1].Value;
            var entry=new SourceEntry("MegaPeer:"+id,title,"MegaPeer",new Uri(Base,topic).AbsoluteUri,new Uri(Base,download).AbsoluteUri,null,size,seeds);
            if(!LiveCatalog.Matches(item,entry))continue;
            if(item.Section=="Фильмы"&&item.Year>0&&!Regex.IsMatch(title,@"(?<!\d)"+item.Year+@"(?!\d)"))continue;
            entries.Add(entry);
        }
        return entries.DistinctBy(entry=>entry.Id).ToArray();
    }
}
