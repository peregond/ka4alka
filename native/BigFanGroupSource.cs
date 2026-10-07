using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Kachalka;

public sealed class BigFanGroupSource(SourceClient client)
{
    static readonly Uri Base=new("https://bigfangroup.org/");
    static readonly HashSet<int> MovieCategories=[13,14,15,18,19,20,21,22,23,24,26,27,28,29,30,31,33,36,39,47,48,51,52,53];
    static Encoding PageEncoding{get{Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);return Encoding.GetEncoding(1251);}}
    public static Uri QueryUri(string title)
    {
        var encoded=string.Concat(PageEncoding.GetBytes(title.Trim()).Select(x=>"%"+x.ToString("X2",CultureInfo.InvariantCulture)));
        return new(Base,"browse.php?ajax=1&search="+encoded+"&cat=0&incldead=1&year=0&format=0&s=seed&d=desc");
    }
    public async Task<IReadOnlyList<SourceEntry>> Search(MediaItem item,CancellationToken ct)
    {
        var groups=await Task.WhenAll(new[]{item.Title,item.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Select(async title=>
        {
            try{return (Items:Parse(await client.Read(QueryUri(title),2_000_000,ct),item),Error:(Exception?)null);}
            catch(Exception error) when(!ct.IsCancellationRequested){return (Items:(IReadOnlyList<SourceEntry>)[],Error:error);}
        }));
        if(groups.All(x=>x.Error!=null))throw groups.First().Error!;
        return groups.SelectMany(x=>x.Items).DistinctBy(x=>x.Id).OrderByDescending(x=>x.Seeds??-1).Take(100).ToArray();
    }
    public static IReadOnlyList<SourceEntry> Parse(byte[] bytes,MediaItem item)
    {
        var html=new HtmlDocument();html.LoadHtml(PageEncoding.GetString(bytes));
        var rows=html.DocumentNode.SelectNodes("//tbody[@id='highlighted']/tr[.//a[starts-with(@href,'browse.php?cat=')]]");
        if(rows==null)return [];
        var result=new List<SourceEntry>();
        foreach(var row in rows.Take(200))
        {
            var link=row.SelectSingleNode(".//a[starts-with(@href,'details.php?id=')]");
            var match=Regex.Match(link?.GetAttributeValue("href","")??"",@"\Adetails\.php\?id=([1-9]\d{0,11})\z");
            var categoryLink=row.SelectSingleNode(".//a[starts-with(@href,'browse.php?cat=')]");
            var catMatch=Regex.Match(categoryLink?.GetAttributeValue("href","")??"",@"\Abrowse\.php\?cat=(\d+)\z");
            if(!match.Success||!catMatch.Success||!int.TryParse(catMatch.Groups[1].Value,out var category))continue;
            var title=Regex.Replace(HtmlEntity.DeEntitize(link?.InnerText??""),@"\s+"," ").Trim();
            // This tracker also puts animated series in the Мультфильм section.
            title=Regex.Replace(title,@"(?i)\bCезон","Сезон");
            var serial=category==11||Regex.IsMatch(title,@"(?i)\b(?:сезон|серии|S\d{1,2})");
            if(item.Section=="Сериалы"?!(serial&&(MovieCategories.Contains(category)||category is 11 or 12)):serial||!MovieCategories.Contains(category))continue;
            if(title.Length is 0 or >500)continue;
            var sizeText=HtmlEntity.DeEntitize(row.SelectSingleNode("./td[6]")?.InnerText??"");
            var sizeMatch=Regex.Match(sizeText,@"(?i)(\d+(?:[.,]\d+)?)\s*(TB|GB|MB|KB|ТБ|ГБ|МБ|КБ)");
            long? size=null;
            if(sizeMatch.Success&&double.TryParse(sizeMatch.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var amount))
            {
                var factor=sizeMatch.Groups[2].Value.ToUpperInvariant() switch{"TB" or "ТБ"=>1099511627776d,"GB" or "ГБ"=>1073741824d,"MB" or "МБ"=>1048576d,_=>1024d};
                if(amount>0&&amount*factor<long.MaxValue)size=(long)(amount*factor);
            }
            var seedText=HtmlEntity.DeEntitize(row.SelectSingleNode("./td[7]")?.InnerText??"").Trim();
            int? seeds=int.TryParse(seedText,out var count)&&count>=0?count:null;
            var id=match.Groups[1].Value;
            var entry=new SourceEntry("BigFanGroup:"+id,title,"BigFanGroup",new Uri(Base,"details.php?id="+id).AbsoluteUri,new Uri(Base,"download.php?id="+id).AbsoluteUri,null,size,seeds);
            if(!LiveCatalog.Matches(item,entry))continue;
            if(item.Section=="Фильмы"&&item.Year>0&&!Regex.IsMatch(title,@"(?<!\d)"+item.Year+@"(?!\d)"))continue;
            result.Add(entry);
        }
        return result.DistinctBy(x=>x.Id).ToArray();
    }
}
