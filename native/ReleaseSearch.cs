using System.Text.RegularExpressions;

namespace Kachalka;

public enum SourceState { Searching, Ready, Empty, Unavailable, TimedOut, Indexed, Saved }
public record SourceCheck(string Name,SourceState State,int Count=0,DateTime? CheckedUtc=null,DateTime? LastSuccessUtc=null);
public record ReleaseSearchUpdate(SourceEntry[] Items,SourceCheck[] Sources,bool Complete);
public record ReleaseSource(string Name,Func<CancellationToken,Task<IReadOnlyList<SourceEntry>>> Search);

public static class ReleaseSearch
{
    public static string Identity(SourceEntry entry)
    {
        if(Regex.IsMatch(entry.Id,@"(?i)^[a-f0-9]{40}$"))return entry.Id.ToUpperInvariant();
        var hash=Regex.Match(entry.TorrentUrl??"",@"(?i)urn:btih:([a-f0-9]{40})");
        return hash.Success?hash.Groups[1].Value.ToUpperInvariant():entry.TorrentUrl??(entry.PageUrl.Length>0?entry.PageUrl:entry.Source+"|"+entry.Id);
    }
    public static SourceEntry[] Distinct(IEnumerable<SourceEntry> rows)=>rows.GroupBy(Identity).Select(group=>group.OrderByDescending(x=>x.Seeds??-1).First()).ToArray();
    // A newly received row replaces the same saved torrent even if its seeder count has fallen.
    public static SourceEntry[] WithSaved(IEnumerable<SourceEntry> fresh,IEnumerable<SourceEntry> saved)=>Distinct(fresh).Concat(saved).DistinctBy(Identity).Take(300).ToArray();

    public static async Task<ReleaseSearchUpdate> RunAsync(IEnumerable<ReleaseSource> providers,IProgress<ReleaseSearchUpdate>? progress=null,CancellationToken ct=default,TimeSpan? timeout=null)
    {
        ct.ThrowIfCancellationRequested();
        var sources=providers.ToArray();
        if(sources.Select(x=>x.Name).Distinct().Count()!=sources.Length)throw new ArgumentException("Source names must be unique.");
        var checks=sources.Select(x=>new SourceCheck(x.Name,SourceState.Searching)).ToArray();
        var groups=new SourceEntry[sources.Length][];var sync=new object();var remaining=sources.Length;
        ReleaseSearchUpdate Snapshot()=>new(Distinct(groups.SelectMany(x=>x??[])),checks.ToArray(),remaining==0);
        progress?.Report(Snapshot());
        var tasks=sources.Select(async (provider,index)=>
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout??TimeSpan.FromSeconds(25));
            SourceEntry[] items=[];SourceCheck check;
            try
            {
                var rows=await provider.Search(deadline.Token).WaitAsync(deadline.Token);
                var now=DateTime.UtcNow;
                items=rows.Select(row=>row with{DataReceivedUtc=now,DataProvider=provider.Name}).ToArray();
                check=new(provider.Name,items.Length>0?SourceState.Ready:SourceState.Empty,items.Length,now,now);
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested){check=new(provider.Name,SourceState.TimedOut,CheckedUtc:DateTime.UtcNow);}
            catch(Exception) when(!ct.IsCancellationRequested){check=new(provider.Name,SourceState.Unavailable,CheckedUtc:DateTime.UtcNow);}
            ct.ThrowIfCancellationRequested();
            DiagnosticLog.Write("source-search",new{Source=check.Name,State=check.State.ToString(),check.Count});
            lock(sync){groups[index]=items;checks[index]=check;remaining--;progress?.Report(Snapshot());}
        }).ToArray();
        await Task.WhenAll(tasks);ct.ThrowIfCancellationRequested();
        lock(sync)return Snapshot();
    }
}
