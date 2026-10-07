using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using HtmlAgilityPack;
namespace Kachalka;

// Linkless Zona credits are resolved through Wikipedia; each catalog work is
// accepted only after the film's own credits confirm the same name and role.
public sealed class CinemaPeople(SourceClient client)
{
    static string Normalize(string text)=>Regex.Replace(text.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static string Text(HtmlNode? node)=>Regex.Replace(HtmlEntity.DeEntitize(node?.InnerText??""),@"\s+"," ").Trim();
    public record Work(string Title,int Year);
    public static Work[] Works(string html)
    {
        var doc=new HtmlDocument();doc.LoadHtml(html);var works=new List<Work>();
        foreach(var table in doc.DocumentNode.SelectNodes("//table[contains(@class,'wikitable')]")??Enumerable.Empty<HtmlNode>())
        {
            var heading=Text(table.SelectSingleNode(".//tr"));
            if(!heading.Contains("Год",StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(heading,@"(?i)название|фильм"))continue;
            int? year=null;
            foreach(var row in table.SelectNodes(".//tr[td]")??Enumerable.Empty<HtmlNode>())
            {
                var cells=row.SelectNodes("./td");if(cells==null)continue;
                var titles=new List<string>();
                foreach(var cell in cells)
                {
                    var value=Text(cell);var match=Regex.Match(value,@"^(?:19|20)\d{2}$");
                    if(match.Success){year=int.Parse(match.Value);continue;}
                    var link=cell.SelectSingleNode(".//a[starts-with(@href,'/wiki/') and not(contains(@href,':'))]");
                    if(link!=null)titles.Add(Text(link));
                }
                if(year.HasValue&&titles.Count>0&&titles[0].Length>1)works.Add(new(titles[0],year.Value));
            }
        }
        return works.DistinctBy(x=>(x.Title,x.Year)).ToArray();
    }
    public async Task<PersonProfile> Load(CinemaPerson person,MediaItem? origin,IEnumerable<MediaItem> known,CancellationToken ct)
    {
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(person.Name+"|"+person.Role)));
        var cache=Path.Combine(Preferences.DataDir,"people",key+".json");
        PersonProfile? saved=null;
        try{if(File.Exists(cache))saved=JsonSerializer.Deserialize<PersonProfile>(await File.ReadAllTextAsync(cache,ct));}catch(IOException){}catch(JsonException){}
        bool Matches(MediaItem film)=>film.People.Any(x=>Normalize(x.Name)==Normalize(person.Name)&&x.Role==person.Role);
        var films=known.Concat(saved?.Filmography??[]).Concat(origin==null?[]:[origin]).Where(Matches).DistinctBy(x=>x.Id).ToList();
        if(saved is {Description.Length:>0,Filmography.Length:>1}&&DateTime.UtcNow-File.GetLastWriteTimeUtc(cache)<TimeSpan.FromDays(7))return saved with{Person=person,Filmography=films.ToArray()};
        string biography=saved?.Description??"",source=saved?.SourceUrl??"";
        var confirmed=new System.Collections.Concurrent.ConcurrentBag<MediaItem>();
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(75));
            var token=deadline.Token;
            var url="https://ru.wikipedia.org/w/api.php?action=query&format=json&redirects=1&prop=extracts%7Cpageprops&exintro=1&explaintext=1&titles="+Uri.EscapeDataString(person.Name);
            using var json=JsonDocument.Parse(await client.Read(new Uri(url),4*1024*1024,token));
            var pages=json.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject().Select(x=>x.Value).ToArray();
            var page=pages.SingleOrDefault(x=>!x.TryGetProperty("missing",out _)&&!x.TryGetProperty("invalid",out _));
            if(page.ValueKind!=JsonValueKind.Object||page.TryGetProperty("pageprops",out var props)&&props.TryGetProperty("disambiguation",out _))throw new InvalidDataException("Нет однозначной биографии участника.");
            var title=page.GetProperty("title").GetString()!;
            biography=page.TryGetProperty("extract",out var extract)?extract.GetString()??"":"";
            if(!Regex.IsMatch(biography,@"(?i)акт[её]р|актрис|режисс[её]р|оператор|кинематограф|кинорежисс"))throw new InvalidDataException("Статья не описывает участника кино.");
            source="https://ru.wikipedia.org/wiki/"+Uri.EscapeDataString(title.Replace(' ','_'));
            var parse="https://ru.wikipedia.org/w/api.php?action=parse&format=json&prop=text&redirects=1&page="+Uri.EscapeDataString(title);
            using var article=JsonDocument.Parse(await client.Read(new Uri(parse),4*1024*1024,token));
            var candidates=Works(article.RootElement.GetProperty("parse").GetProperty("text").GetProperty("*").GetString()??"").OrderByDescending(x=>x.Year).Take(16).ToArray();
            var catalog=new LiveCatalog(client);using var slots=new SemaphoreSlim(3);
            await Task.WhenAll(candidates.Select(async work=>
            {
                await slots.WaitAsync(token);
                try
                {
                    foreach(var section in new[]{"Фильмы","Сериалы"})
                    {
                        var results=await catalog.Browse(section,work.Title,1,token);
                        var film=results.FirstOrDefault(x=>x.Year==work.Year&&Normalize(x.Title)==Normalize(work.Title));
                        if(film==null)continue;
                        var detail=await catalog.Detail(film,token);if(Matches(detail)){confirmed.Add(detail);return;}
                    }
                }
                catch(Exception)when(!ct.IsCancellationRequested){ /* A single unavailable work does not invalidate confirmed films. */ }
                finally{slots.Release();}
                return;
            }));

        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception error){ErrorLog.Write(error); /* Keep confirmed works and cached biography when the source is unavailable. */ }
        films.AddRange(confirmed);
        var result=new PersonProfile(person,biography,films.DistinctBy(x=>x.Id).ToArray(),source.Length>0?source:null);
        if(result.Description.Length>0||result.Filmography.Length>0)
        {
            try{Directory.CreateDirectory(Path.GetDirectoryName(cache)!);await File.WriteAllTextAsync(cache,JsonSerializer.Serialize(result),ct);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return result;
    }
}
