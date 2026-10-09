namespace Kachalka;

public partial class MainWindow
{
    CancellationTokenSource? discoveryRegionRequest;
    string? discoveryRegionKey;
    int discoveryRegionGeneration;

    void SyncRegionalDiscoveryContext()
    {
        var key=section+"|"+prefs.HomeCountry;
        if(DiscoveryCatalog&&(discoveryRegionKey==null||discoveryRegionKey==key))return;
        discoveryRegionGeneration++;discoveryRegionRequest?.Cancel();discoveryRegionRequest?.Dispose();discoveryRegionRequest=null;
        if(discoveryRegionKey!=null&&discoveryRegions.TryGetValue(discoveryRegionKey,out var pending)&&pending.Loading)discoveryRegions.Remove(discoveryRegionKey);
        discoveryRegionKey=null;
    }
    void ResetRegionalDiscovery()
    {
        discoveryRegionGeneration++;discoveryRegionRequest?.Cancel();discoveryRegionRequest?.Dispose();discoveryRegionRequest=null;discoveryRegionKey=null;
    }
    void StartRegionalDiscovery(string category,MediaItem[] cards)
    {
        var home=prefs.HomeCountry;var key=category+"|"+home;
        if(!DiscoveryCatalog||section!=category||discoveryRegionRequest!=null&&discoveryRegionKey==key||discoveryRegions.TryGetValue(key,out var saved)&&!saved.Loading)return;
        discoveryRegionRequest?.Cancel();discoveryRegionRequest?.Dispose();
        var request=discoveryRegionRequest=new CancellationTokenSource(TimeSpan.FromSeconds(15));discoveryRegionKey=key;var generation=++discoveryRegionGeneration;
        var remembered=DiscoveryRegionalItems(category,home,cards);SetDiscoveryRegionalItems(category,home,remembered.Foreign,remembered.Native,true);
        _=LoadRegionalDiscovery(category,home,cards,request,generation);
    }
    async Task LoadRegionalDiscovery(string category,string home,MediaItem[] cards,CancellationTokenSource request,int generation)
    {
        MediaItem[] foreign=[],native=[];var token=request.Token;var finished=false;
        bool Current()=>!closed&&generation==discoveryRegionGeneration&&DiscoveryCatalog&&section==category&&prefs.HomeCountry==home;
        try
        {
            var known=await RegionalCatalog.WithCachedCountries(cards,token);
            foreign=known.Where(item=>CatalogRegions.IsForeign(item,home)).ToArray();native=known.Where(item=>CatalogRegions.IsNative(item,home)).ToArray();
            if(!Current())return;
            SetDiscoveryRegionalItems(category,home,foreign,native,true);
            var provider=new RegionalCatalog(sourceClient);
            async Task<(bool Native,MediaItem[] Items)> Region(bool isNative)
            {
                var remembered=isNative?native:foreign;var latest=remembered;
                if(remembered.Length>=8)return (isNative,remembered);
                void Progress(CatalogPage page)
                {
                    var items=page.Items.Concat(remembered).DistinctBy(item=>item.Id).Take(40).ToArray();latest=items;
                    void Apply()
                    {
                        if(finished||token.IsCancellationRequested||!Current())return;
                        if(isNative)native=items;else foreign=items;
                        SetDiscoveryRegionalItems(category,home,foreign,native,true);
                    }
                    if(Dispatcher.CheckAccess())Apply();else Dispatcher.BeginInvoke(new Action(Apply));
                }
                try
                {
                    var page=await provider.BrowsePage(category,1,isNative?"native":"foreign",home,token,progress:Progress);
                    return (isNative,page.Items.Concat(remembered).DistinctBy(item=>item.Id).Take(40).ToArray());
                }
                catch(OperationCanceledException){return (isNative,latest);}
                catch(Exception error)when(error is not OutOfMemoryException){return (isNative,latest);}
            }
            var pending=new List<Task<(bool Native,MediaItem[] Items)>>{Region(false),Region(true)};
            while(pending.Count>0)
            {
                var completed=await Task.WhenAny(pending);pending.Remove(completed);var result=await completed;
                if(Current())
                {
                    if(result.Native)native=result.Items;else foreign=result.Items;
                    SetDiscoveryRegionalItems(category,home,foreign,native,pending.Count>0);
                }
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is not OutOfMemoryException){}
        finally
        {
            finished=true;
            if(generation==discoveryRegionGeneration)
            {
                discoveryRegionRequest=null;discoveryRegionKey=null;
                if(Current())SetDiscoveryRegionalItems(category,home,foreign,native,false);
            }
            request.Dispose();
        }
    }
}
