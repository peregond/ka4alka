using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MonoTorrent;

namespace Kachalka;

public static class DownloadMetadata
{
    public static MediaItem? Card(DownloadItem item)
    {
        var section=item.MediaSection;
        if(section is not ("Фильмы" or "Сериалы")||!Uri.TryCreate(item.MediaPageUrl,UriKind.Absolute,out var page)||page.Scheme!="https"||page.Host!="w6.zona.plus"||!page.IsDefaultPort||page.UserInfo.Length>0||page.Query.Length>0||page.Fragment.Length>0)return null;
        var prefix=section=="Фильмы"?"/movies/":"/tvseries/";
        if(!page.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal))return null;
        var slug=Uri.UnescapeDataString(page.AbsolutePath[prefix.Length..].TrimEnd('/'));
        if(!Regex.IsMatch(slug,@"^[-\p{L}\p{N}]{1,120}$"))return null;
        var id=-(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(page.AbsoluteUri)),0)&int.MaxValue);
        return new(id,item.DisplayName,section,"",item.MediaYear,"—","—","#526B69")
        {PageUrl=page.AbsoluteUri,OnlineId=(section=="Фильмы"?"movies:":"series:")+slug,ImageUrl=item.ImageUrl};
    }
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    static readonly Regex Year=new(@"^(?:19|20)\d{2}\b",RegexOptions.CultureInvariant);
    static readonly Regex ReleaseTag=new(@"^(?:s\d{1,3}(?:e\d{1,3})?|e\d{1,3}|\d{1,2}\s+(?:сезон|season)|season|сезон|серия|серии|episode|ep|complete|полный|все|web|webrip|hdtv|dvdrip|hdrip|bdrip|bluray|bdremux|remux|720p?|1080[pi]?|2160[pi]?|4k|uhd|x264|x265|h264|h265|hevc|avc|mkv|mp4|avi|repack|extended|uncut|directors|hdr|hdr10|dv|dub|mvo)\b",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);

    // Prefer a stored catalog identity or an exact cached torrent identity.
    // Title matching accepts only complete names followed by known release
    // metadata; a sequel number or a remake's different year cannot match.
    public static bool Associate(DownloadItem item,IEnumerable<MediaItem> catalog,Func<MediaItem,IReadOnlyList<SourceEntry>>? cachedReleases=null)
    {
        var rows=catalog.Where(media=>media.Cinema&&
            (string.IsNullOrWhiteSpace(item.MediaSection)||media.Section==item.MediaSection)).ToArray();
        MediaItem? selected;
        if(!string.IsNullOrWhiteSpace(item.MediaPageUrl))
        {
            selected=Unique(rows.Where(media=>SamePage(media.PageUrl,item.MediaPageUrl)));
            return selected!=null&&Apply(item,selected);
        }
        if(cachedReleases!=null)
        {
            var hash=Hash(item.InfoHash)??MagnetHash(item.Source);
            var exact=rows.Where(media=>cachedReleases(media).Any(release=>SameRelease(item,hash,release))).ToArray();
            if(exact.Length>0){selected=Unique(exact);return selected!=null&&Apply(item,selected);}
        }
        var name=!string.IsNullOrWhiteSpace(item.MediaTitle)?item.MediaTitle:!string.IsNullOrWhiteSpace(item.ReleaseTitle)?item.ReleaseTitle:item.Name;
        selected=Unique(rows.Where(media=>TitleMatches(name,media)&&
            (item.MediaYear<=0||media.Year<=0||item.MediaYear==media.Year)));
        return selected!=null&&Apply(item,selected);
    }

    public static bool Apply(DownloadItem item,MediaItem media)
    {
        var image=!string.IsNullOrWhiteSpace(media.ImageUrl)?media.ImageUrl:item.ImageUrl;
        var page=!string.IsNullOrWhiteSpace(media.PageUrl)?media.PageUrl:item.MediaPageUrl;
        var year=media.Year>0?media.Year:item.MediaYear;
        if(item.MediaTitle==media.Title&&item.MediaSection==media.Section&&item.ImageUrl==image&&item.MediaPageUrl==page&&item.MediaYear==year)return false;
        item.MediaTitle=media.Title;item.MediaSection=media.Section;item.ImageUrl=image;item.MediaPageUrl=page;item.MediaYear=year;item.Refresh();return true;
    }

    static MediaItem? Unique(IEnumerable<MediaItem> items)
    {
        static bool Rating(string value)=>double.TryParse(value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var score)&&score is >0 and <=10;
        var groups=items.GroupBy(media=>!string.IsNullOrWhiteSpace(media.PageUrl)?media.Section+"|"+media.PageUrl:
            media.Section+"|"+media.Year+"|"+Normalize(media.Title)).ToArray();
        if(groups.Length!=1)return null;
        var rows=groups[0].ToArray();
        var preferred=rows.OrderByDescending(media=>Regex.IsMatch(media.Title,@"[А-Яа-яЁё]")).ThenByDescending(media=>media.Year>0)
            .ThenByDescending(media=>!string.IsNullOrWhiteSpace(media.OriginalTitle)).First();
        var image=rows.Select(media=>media.ImageUrl).FirstOrDefault(url=>!string.IsNullOrWhiteSpace(url));
        var original=rows.Select(media=>media.OriginalTitle).FirstOrDefault(title=>!string.IsNullOrWhiteSpace(title))??
            rows.Where(media=>!Regex.IsMatch(media.Title,@"[А-Яа-яЁё]")&&Normalize(media.Title)!=Normalize(preferred.Title)).Select(media=>media.Title).FirstOrDefault();
        return preferred with{ImageUrl=!string.IsNullOrWhiteSpace(preferred.ImageUrl)?preferred.ImageUrl:image,OriginalTitle=preferred.OriginalTitle??original,
            Kinopoisk=rows.FirstOrDefault(media=>Rating(media.Kinopoisk))?.Kinopoisk??"—",Imdb=rows.FirstOrDefault(media=>Rating(media.Imdb))?.Imdb??"—",
            Description=rows.FirstOrDefault(media=>!string.IsNullOrWhiteSpace(media.Description))?.Description};
    }

    static bool TitleMatches(string value,MediaItem media)
    {
        var raw=Regex.Replace(value,@"^(?:\s*\[[^\]]{1,40}\]\s*)+","");
        raw=Regex.Replace(raw,@"\.(?:mkv|mp4|avi|mov|wmv|torrent)$","",RegexOptions.IgnoreCase);
        var parts=Regex.Split(raw,@"\s*/\s*|\s+\|\s+").Prepend(raw).Select(Normalize).Distinct().ToArray();
        var aliases=new[]{Normalize(media.Title),Normalize(media.OriginalTitle??"")}.Where(title=>title.Length>0).ToArray();
        var years=Regex.Matches(raw,@"(?<![\p{L}\p{N}])(?:19|20)\d{2}(?![\p{L}\p{N}])").Select(match=>match.Value)
            .Where(year=>!aliases.Any(alias=>alias.Split(' ').Contains(year))).Select(int.Parse).ToArray();
        if(media.Year>0&&years.Length>0&&!years.Contains(media.Year))return false;
        foreach(var part in parts)
        foreach(var title in aliases)
        {
            if(part==title)return true;
            if(!part.StartsWith(title+" ",StringComparison.Ordinal))continue;
            var tail=part[(title.Length+1)..];
            foreach(var alias in aliases.Where(alias=>alias!=title))
                if(tail.StartsWith(alias+" ",StringComparison.Ordinal))tail=tail[(alias.Length+1)..];
            var year=Year.Match(tail);
            if(year.Success)
            {
                if(media.Year>0&&int.Parse(year.Value)!=media.Year)continue;
                return true;
            }
            if(ReleaseTag.IsMatch(tail))return true;
        }
        return false;
    }

    static string Normalize(string value)=>Regex.Replace(value.Normalize(NormalizationForm.FormKC).Replace('ё','е').Replace('Ё','Е').ToUpperInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();

    public static MediaItem EnrichMedia(MediaItem media,IEnumerable<MediaItem> known)
    {
        var best=Unique(known.Where(candidate=>candidate.Section==media.Section&&
            (SamePage(candidate.PageUrl,media.PageUrl)||string.IsNullOrWhiteSpace(media.PageUrl)&&candidate.Id==media.Id&&candidate.Year==media.Year&&Normalize(candidate.Title)==Normalize(media.Title))));
        if(best==null)return media;
        return media with
        {
            Title=Regex.IsMatch(best.Title,@"[А-Яа-яЁё]")?best.Title:media.Title,
            ImageUrl=!string.IsNullOrWhiteSpace(best.ImageUrl)?best.ImageUrl:media.ImageUrl,
            OriginalTitle=best.OriginalTitle??media.OriginalTitle,
            Kinopoisk=best.Kinopoisk!="—"?best.Kinopoisk:media.Kinopoisk,
            Imdb=best.Imdb!="—"?best.Imdb:media.Imdb,
            Description=!string.IsNullOrWhiteSpace(best.Description)?best.Description:media.Description,
            PageUrl=best.PageUrl??media.PageUrl,Year=best.Year>0?best.Year:media.Year
        };
    }
    static string? Hash(string? value)=>value!=null&&Regex.IsMatch(value,@"\A[0-9a-fA-F]{40}(?:[0-9a-fA-F]{24})?\z")?value.ToUpperInvariant():null;
    static string? MagnetHash(string? value)
    {
        if(value?.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase)!=true)return null;
        try{var hashes=MagnetLink.Parse(value).InfoHashes;return hashes.V1?.ToHex().ToUpperInvariant()??hashes.V2?.ToHex().ToUpperInvariant();}catch{return null;}
    }
    static bool SamePage(string? left,string? right)=>!string.IsNullOrWhiteSpace(left)&&!string.IsNullOrWhiteSpace(right)&&
        Uri.TryCreate(left,UriKind.Absolute,out var a)&&Uri.TryCreate(right,UriKind.Absolute,out var b)&&
        a.Scheme is "http" or "https"&&b.Scheme is "http" or "https"&&a.Host.Equals(b.Host,StringComparison.OrdinalIgnoreCase)&&
        (a.Port==b.Port||a.IsDefaultPort&&b.IsDefaultPort)&&a.PathAndQuery.TrimEnd('/')==b.PathAndQuery.TrimEnd('/');
    static bool SameRelease(DownloadItem item,string? hash,SourceEntry release)=>
        hash!=null&&hash==MagnetHash(release.TorrentUrl)||
        SamePage(item.Source,release.TorrentUrl)||SamePage(item.ReleaseUrl,release.TorrentUrl)||
        item.ReleaseSource==release.Source&&(
            !string.IsNullOrWhiteSpace(item.ReleaseId)&&item.ReleaseId==release.Id||SamePage(item.ReleasePageUrl,release.PageUrl));

    public static async Task<int> RepairAsync(DownloadService downloads,IEnumerable<MediaItem> known,CancellationToken ct=default)
    {
        var dataDir=Preferences.DataDir;var initial=known.ToArray();
        var snapshots=downloads.Items.Select(item=>new DownloadItem
        {
            Id=item.Id,Source=item.Source,InfoHash=item.InfoHash,Name=item.Name,ImageUrl=item.ImageUrl,MediaTitle=item.MediaTitle,MediaSection=item.MediaSection,
            MediaPageUrl=item.MediaPageUrl,MediaYear=item.MediaYear,ReleaseTitle=item.ReleaseTitle,ReleaseSource=item.ReleaseSource,ReleaseId=item.ReleaseId,ReleasePageUrl=item.ReleasePageUrl,ReleaseUrl=item.ReleaseUrl
        }).ToArray();
        var repaired=await Task.Run(()=>
        {
            var catalog=LocalCatalog(dataDir,initial,ct);
            var reader=new CachedReleases(dataDir,ct);
            var result=new List<DownloadItem>();
            foreach(var item in snapshots){ct.ThrowIfCancellationRequested();if(Associate(item,catalog,reader.For))result.Add(item);}
            return result;
        },ct);
        ct.ThrowIfCancellationRequested();int changed=0;
        foreach(var result in repaired)
        {
            var item=downloads.Items.FirstOrDefault(item=>item.Id==result.Id&&item.Source==result.Source);
            if(item==null)continue;
            var media=new MediaItem(0,result.MediaTitle!,result.MediaSection!,"",result.MediaYear,"—","—","#526B69"){ImageUrl=result.ImageUrl,PageUrl=result.MediaPageUrl};
            if(Apply(item,media))changed++;
        }
        if(changed>0)downloads.Save();return changed;
    }

    static MediaItem[] LocalCatalog(string dataDir,IEnumerable<MediaItem> known,CancellationToken ct)
    {
        var rows=known.ToList();
        try
        {
            var path=Path.Combine(dataDir,"catalog-index.json");
            if(File.Exists(path)&&new FileInfo(path).Length<=12*1024*1024)
            {
                using var saved=JsonDocument.Parse(File.ReadAllBytes(path));
                if(saved.RootElement.ValueKind==JsonValueKind.Array)
                    foreach(var entry in saved.RootElement.EnumerateArray().Take(3000))
                        if(entry.TryGetProperty("Item",out var row)&&row.Deserialize<MediaItem>(Json) is {} media&&media.Cinema)rows.Add(media);
            }
        }
        catch(IOException){}catch(JsonException){}catch(UnauthorizedAccessException){}
        foreach(var section in new[]{"Фильмы","Сериалы"})
        for(int page=1;page<=Math.Ceiling(BundledCatalog.Count(section)/(double)CatalogPaging.Size);page++)
        {ct.ThrowIfCancellationRequested();rows.AddRange(BundledCatalog.Page(section,page).Items);}
        return rows.Where(media=>media.Cinema).DistinctBy(media=>(media.Section,media.PageUrl,media.Year,media.Title,media.OriginalTitle,media.ImageUrl)).ToArray();
    }

    sealed class CachedReleases(string dataDir,CancellationToken ct)
    {
        readonly Dictionary<string,IReadOnlyList<SourceEntry>> loaded=[];
        long remaining=32*1024*1024;
        public IReadOnlyList<SourceEntry> For(MediaItem media)
        {
            ct.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(media.PageUrl))return [];
            var key=media.Section+"|"+media.PageUrl;
            if(loaded.TryGetValue(key,out var prior))return prior;
            IReadOnlyList<SourceEntry> result=[];
            try
            {
                var path=Path.Combine(dataDir,"release-index",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".json");
                if(File.Exists(path))
                {
                    var length=new FileInfo(path).Length;
                    if(length is >0 and <=3*1024*1024&&length<=remaining)
                    {
                        remaining-=length;
                        var cache=JsonSerializer.Deserialize<ReleaseCache>(File.ReadAllBytes(path),Json);
                        result=cache?.Items?.Take(300).ToArray()??[];
                    }
                }
            }
            catch(IOException){}catch(JsonException){}catch(UnauthorizedAccessException){}
            loaded[key]=result;return result;
        }
    }
}
