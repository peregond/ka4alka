using System.IO;
using System.Text.RegularExpressions;
namespace Kachalka;

public static class GameReleaseSources
{
    static readonly Regex MetadataTags=new(@"\[(?:P|L|DL|PC|РС|ПК|CD|DVD|RIP|P2P|Scene|Portable|RUS\b[^\]]*|ENG\b[^\]]*|MULTI\d*\b[^\]]*|v\.?\s*\d[^\]]*|RePack\b[^\]]*|(?:FitGirl|DODI)(?:\s+Repack)?)\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    public static string[] Queries(MediaItem game)=>new[]{game.OriginalTitle}.Concat(MediaIdentity.Queries(game))
        .Where(name=>!string.IsNullOrWhiteSpace(name)).Select(name=>name!.Replace("™","").Replace("®","").Replace('’','\'').Trim())
        .Where(name=>name.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    public static bool WindowsRelease(string title)=>!Regex.IsMatch(title,@"(?i)\b(?:PS[1-5]|PSP|PlayStation|Xbox\d*|Switch|Nintendo|Wii|3DS|NDS|SNES|Android|iOS|Linux|Mac|MacOS|Mac OS|OSX|Wine)\b");
    public static bool Matches(MediaItem game,SourceEntry entry)
    {
        if(game.Section!="Игры"||!WindowsRelease(entry.Title))return false;
        var title=MetadataTags.Replace(entry.Title," ").Trim();
        foreach(var candidate in Regex.Split(title,@"\s+[/|]\s+"))
        foreach(var name in MediaIdentity.Names(game))
        {
            var expected=MediaIdentity.Normalize(name);var actual=MediaIdentity.Normalize(candidate);
            if(expected.Length==0)continue;
            if(actual==expected)return true;
            if(!actual.StartsWith(expected+" ",StringComparison.Ordinal))continue;
            var suffix=actual[(expected.Length+1)..];
            var package=Regex.Replace(suffix,@"^(?:19|20)\d{2}\s+","");
            // Do not mistake a numbered sequel, soundtrack or an update-only package for this game.
            if(Regex.IsMatch(package,@"^(?:soundtrack|саундтрек|ost|patch|патч|update|обновление|dlc|любительские карты|custom maps)\b"))continue;
            if(char.IsDigit(suffix[0])&&!Regex.IsMatch(suffix,@"^(?:19|20)\d{2}\b"))continue;
            if(!Regex.IsMatch(suffix,@"^(?:(?:19|20)\d{2}|v\s*\d+|version|версия|build|билд|pc|рс|пк|windows|repack|репак|portable|digital|deluxe|ultimate|complete|goty|game of the year|special|anniversary|collector|gog|steam|epic|origin|license|лицензия|ru|rus|russian|en|eng|english|multi\d*|rune|codex|fitgirl|dodi)\b"))continue;
            return true;
        }
        return false;
    }
    public static Uri RutorQuery(string title,string host="rutor.info")=>new("https://"+host+"/search/0/8/000/2/"+Uri.EscapeDataString(title.Trim()));
    public static IReadOnlyList<SourceEntry> ParseRutor(byte[] data,MediaItem game)=>LiveCatalog.ParseRutor(data).Where(row=>Matches(game,row)).ToArray();
    public static ReleaseSource[] Create(SourceClient client,MediaItem game,IEnumerable<SourceConfig> configured)
    {
        async Task<IReadOnlyList<SourceEntry>> Rutor(CancellationToken ct)
        {
            Exception? last=null;var answered=false;
            foreach(var query in Queries(game))
            {
            foreach(var host in new[]{"rutor.info","rutor.is"})
            {
                try
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var data=await client.Read(RutorQuery(query,host),4*1024*1024,timeout.Token);
                    if(!System.Text.Encoding.UTF8.GetString(data).Contains("id=\"index\"",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("RuTor не вернул таблицу поиска.");
                    answered=true;
                    var rows=await Task.Run(()=>ParseRutor(data,game),ct);
                    if(rows.Count>0)return rows;
                    break; // A valid empty answer needs the next alias, not the same query on a mirror.
                }
                catch(Exception error)when(!ct.IsCancellationRequested){last=error;}
            }
            }
            if(answered)return [];
            ct.ThrowIfCancellationRequested();throw last??new IOException("RuTor недоступен.");
        }
        return new[]{new ReleaseSource("RuTor · игры",ct=>Task.Run(()=>Rutor(ct),ct)),new ReleaseSource("Knaben",ct=>Task.Run(()=>KnabenSource.Search(game,ct),ct))}
            .Concat(configured.Take(3).Select(source=>new ReleaseSource(source.Name,ct=>Task.Run<IReadOnlyList<SourceEntry>>(async ()=>(await client.SearchTorznab(source,Queries(game).FirstOrDefault()??game.Title,ct,4010)).Where(row=>Matches(game,row)).ToArray(),ct)))).DistinctBy(source=>source.Name).ToArray();
    }
}
