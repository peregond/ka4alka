using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;

string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
var input=Option("--catalog");var output=Option("--report")??"test-output/catalog-search-audit.json";
var watch=Stopwatch.StartNew();byte[]? payload=null;MediaItem[] rows;
if(input!=null)
{
    if(new FileInfo(input).Length>32*1024*1024)throw new InvalidDataException("Catalog audit input exceeds 32 MiB.");
    payload=await File.ReadAllBytesAsync(input);using var document=JsonDocument.Parse(payload);
    var feed=document.RootElement.ValueKind==JsonValueKind.Array
        ?JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,generatedAtUtc=DateTime.UtcNow,items=document.RootElement}):payload;
    rows=SharedCatalog.Parse(feed,DateTime.UtcNow).Items.Select(MediaIdentity.Enrich).DistinctBy(item=>(item.Section,item.PageUrl)).ToArray();
}
else{await BundledCatalog.EnsureLoadedAsync();rows=BundledCatalog.All.Select(MediaIdentity.Enrich).ToArray();}
if(rows.Length==0)throw new InvalidDataException("Cannot audit an empty catalog.");
var failures=new List<object>();var limited=new List<object>();var checkedQueries=0;var namesCount=0;
var cache=new Dictionary<string,HashSet<string>>();
string Key(MediaItem item)=>item.Section+"|"+(item.PageUrl??item.Id.ToString());
foreach(var group in rows.GroupBy(item=>item.Section))
{
    var catalog=group.ToArray();Console.WriteLine($"Auditing {group.Key}: {catalog.Length} records.");
    foreach(var item in catalog)
    {
        var names=MediaIdentity.Names(item);namesCount+=names.Length;
        foreach(var name in names)
        {
            var variants=new[]{name,name.Normalize(NormalizationForm.FormD),item.Year>0?name+" "+item.Year:name}.Distinct();
            foreach(var query in variants)
            {
                checkedQueries++;var parsed=new CatalogSearchQuery(query);
                if(parsed.Score(item)==0){failures.Add(new{scope="native",reason="own_title_not_matched",item=item.PageUrl,item.Title,query});continue;}
                var key=group.Key+"|"+query;
                if(!cache.TryGetValue(key,out var found))
                {
                    found=CatalogSearch.Find(catalog,query,200).Select(Key).ToHashSet();cache[key]=found;
                }
                if(!found.Contains(Key(item)))
                {
                    var matches=catalog.Count(candidate=>parsed.Score(candidate)>0);
                    if(matches>200)limited.Add(new{item=item.PageUrl,item.Title,query,matches});
                    else failures.Add(new{scope="native",reason="lost_from_results",item=item.PageUrl,item.Title,query,matches});
                }
                if(checkedQueries%1000==0)Console.WriteLine($"Checked {checkedQueries} queries in {watch.Elapsed.TotalSeconds:F1}s; failures={failures.Count}.");
            }
        }
        if(names.Length==0)failures.Add(new{scope="native",reason="no_indexed_names",item=item.PageUrl,item.Title});
    }
}
var report=new
{
    schemaVersion=1,generatedAtUtc=DateTime.UtcNow,input=input??"embedded",sha256=payload==null?null:Convert.ToHexStringLower(SHA256.HashData(payload)),
    networkRequests=0,records=rows.Length,movies=rows.Count(item=>item.Section=="Фильмы"),series=rows.Count(item=>item.Section=="Сериалы"),
    names=namesCount,queries=checkedQueries,elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,2),
    coverage=new{oldestYear=rows.Min(item=>item.Year),newestYear=rows.Max(item=>item.Year),missingOriginalTitle=rows.Count(item=>string.IsNullOrWhiteSpace(item.OriginalTitle)),missingImdbId=rows.Count(item=>item.ImdbId==null)},
    failures,windowLimited=limited,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Audit complete: {rows.Length} records, {checkedQueries} queries, {failures.Count} failures, {limited.Count} limited windows. Report: {output}");
if(failures.Count>0)Environment.ExitCode=1;
