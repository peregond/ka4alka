namespace Kachalka;

public static class ReleaseAvailability
{
    public static bool Downloadable(SourceEntry entry)=>!string.IsNullOrWhiteSpace(entry.TorrentUrl)||entry.Source=="Internet Archive";
    public static bool ConfirmedEmpty(IEnumerable<SourceEntry> entries,IReadOnlyList<SourceCheck>? checks)=>
        !entries.Any(Downloadable)&&checks is {Count:>0}&&checks.All(x=>x.State==SourceState.Empty);
    public static bool Hidden(ReleaseCache? cache,DateTime now)=>cache!=null&&cache.SavedUtc>now.AddDays(-1)&&cache.SavedUtc<=now.AddMinutes(5)&&ConfirmedEmpty(cache.Items,cache.Sources);
}

public partial class MainWindow
{
    bool NoDownloads(MediaItem item)=>ReleaseAvailability.Hidden(catalogIndex.CachedReleaseSnapshot(item),DateTime.UtcNow);
    async Task CheckCatalogAvailability(IReadOnlyList<MediaItem> items,string key,string category,CancellationToken token,bool force=false)
    {
        // Scan just the current page, never all thousands of metadata records.
        // UI smoke fixtures use deterministic cached evidence instead of public requests.
        if(Environment.GetCommandLineArgs().Any(x=>x.EndsWith("-smoke-test",StringComparison.Ordinal)))return;
        using var slots=new SemaphoreSlim(2,2);
        try
        {
            await Task.WhenAll(items.Where(x=>x.IsLive).Select(async item=>
            {
                await slots.WaitAsync(token);
                try
                {
                    if(closed||liveKey!=key||section!=category||current!=null||favoritesOnly)return;
                    var cache=catalogIndex.CachedReleaseSnapshot(item);
                    if(!force&&cache?.SavedUtc>DateTime.UtcNow.AddDays(-1))return;
                    ReleaseSearchUpdate result;
                    try
                    {
                        var indexed=await onlineIndex.Releases(item,token);
                        if(indexed.Any(ReleaseAvailability.Downloadable))
                        {
                            qualitySnapshots.Remove(item.Id);
                            await catalogIndex.CacheReleasesAsync(item,indexed,[new("Онлайн-индекс",SourceState.Ready,indexed.Count,DateTime.UtcNow,DateTime.UtcNow)]);
                            return;
                        }
                        var detail=item;try{detail=await Metadata(item).WaitAsync(TimeSpan.FromSeconds(8),token);}catch(TimeoutException){}
                        result=await ReleaseSearch.RunAsync(NativeReleaseSources.Create(sourceClient,detail),ct:token);
                        result=result with{Sources=result.Sources.Append(new SourceCheck("Онлайн-индекс",SourceState.Empty,CheckedUtc:DateTime.UtcNow,LastSuccessUtc:DateTime.UtcNow)).ToArray()};
                    }
                    catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                    catch{ return; } // An unavailable index is not evidence of an empty catalog.
                    await catalogIndex.CacheReleasesAsync(item,result.Items,result.Sources);
                    if(ReleaseAvailability.ConfirmedEmpty(result.Items,result.Sources)&&!closed&&liveKey==key&&section==category&&current==null&&!favoritesOnly)
                        RenderCatalogKeepingPosition();
                }
                catch(System.IO.IOException){}
                catch(UnauthorizedAccessException){}
                finally{slots.Release();}
            }));
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){ErrorLog.Write(error);}
    }
}
