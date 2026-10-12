using System.IO;
using System.Text.RegularExpressions;
namespace Kachalka;

public static class GameReleaseSources
{
    public static bool WindowsRelease(string title)=>!Regex.IsMatch(title,@"(?i)\b(?:PS[1-5]|PSP|PlayStation|Xbox\d*|Switch|Nintendo|Wii|3DS|NDS|SNES|Android|iOS|Linux|Mac|MacOS|Mac OS|OSX|Wine)\b");
    public static bool Matches(MediaItem game,SourceEntry entry)
    {
        if(game.Section!="Игры"||!WindowsRelease(entry.Title))return false;
        var title=Regex.Replace(entry.Title,@"^(?:\s*\[[^\]]{1,80}\]\s*)+","");
        foreach(var candidate in Regex.Split(title,@"\s+[/|]\s+"))
        foreach(var name in MediaIdentity.Names(game))
        {
            var expected=MediaIdentity.Normalize(name);var actual=MediaIdentity.Normalize(candidate);
            if(expected.Length==0)continue;
            if(actual==expected)return true;
            if(!actual.StartsWith(expected+" ",StringComparison.Ordinal))continue;
            var suffix=actual[(expected.Length+1)..];
            // Do not mistake a numbered sequel, soundtrack or an update-only package for this game.
            if(Regex.IsMatch(suffix,@"^(?:soundtrack|саундтрек|patch|патч|update|обновление|dlc)\b"))continue;
            if(char.IsDigit(suffix[0])&&!Regex.IsMatch(suffix,@"^(?:19|20)\d{2}\b"))continue;
            if(!Regex.IsMatch(suffix,@"^(?:(?:19|20)\d{2}|v\d+|version|версия|build|билд|pc|windows|repack|репак|digital|deluxe|ultimate|complete|goty|game of the year|special|anniversary|collector|gog|steam|epic|origin|license|лицензия|ru|rus|russian|en|eng|english|multi\d*|rune|codex|fitgirl|dodi)\b"))continue;
            return true;
        }
        return false;
    }
    public static Uri RutorQuery(string title,string host="rutor.info")=>new("https://"+host+"/search/0/8/000/2/"+Uri.EscapeDataString(title.Trim()));
    public static ReleaseSource[] Create(SourceClient client,MediaItem game,IEnumerable<SourceConfig> configured)
    {
        async Task<IReadOnlyList<SourceEntry>> Rutor(CancellationToken ct)
        {
            Exception? last=null;
            foreach(var host in new[]{"rutor.info","rutor.is"})
            {
                try
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var data=await client.Read(RutorQuery(game.OriginalTitle??game.Title,host),4*1024*1024,timeout.Token);
                    var rows=await Task.Run(()=>LiveCatalog.ParseRutor(data).Where(row=>Regex.IsMatch(row.Title,@"(?i)\b(?:PC|Windows)\b")&&Matches(game,row)).ToArray(),ct);
                    if(rows.Length>0)return rows;
                    if(!System.Text.Encoding.UTF8.GetString(data).Contains("id=\"index\"",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("RuTor не вернул таблицу поиска.");
                    return rows;
                }
                catch(Exception error)when(!ct.IsCancellationRequested){last=error;}
            }
            ct.ThrowIfCancellationRequested();throw last??new IOException("RuTor недоступен.");
        }
        return new[]{new ReleaseSource("RuTor · игры",Rutor),new ReleaseSource("RuTracker · через Knaben",ct=>KnabenSource.Search(game,ct))}
            .Concat(configured.Take(3).Select(source=>new ReleaseSource(source.Name,async ct=>(await client.SearchTorznab(source,game.OriginalTitle??game.Title,ct,4010)).Where(row=>Matches(game,row)).ToArray()))).DistinctBy(source=>source.Name).ToArray();
    }
}
