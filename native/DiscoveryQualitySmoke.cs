namespace Kachalka;

public partial class MainWindow
{
    async Task CheckDiscoveryShelfQuality(Action<bool,string> check)
    {
        var category=section;var home=CatalogRegions.HomeCountry(prefs.HomeCountry);var pageKey=category+"|sort-date|1";var popularKey=category+"||1";var regionKey=category+"|"+home;
        catalogPages.TryGetValue(pageKey,out var oldPage);catalogPages.TryGetValue(popularKey,out var oldPopular);discoveryRegions.TryGetValue(regionKey,out var oldRegions);
        var oldItems=liveItems;var oldHeight=prefs.CatalogQualityHeight;var oldHide=prefs.HidePoorQuality;var oldProvider=catalogQualityProvider;
        var main=Enumerable.Range(1,2).Select(id=>new MediaItem(-998610-id,"Проверка качества главной страницы "+id,category,"",2026,"—","—","#526B69"){PageUrl=LiveCatalog.Base+(category=="Фильмы"?"/movies/":"/tvseries/")+"quality-discovery-main-"+id}).ToArray();
        var popular=main[0] with{Id=-998620,Title="Позднее популярное 4K",PageUrl=LiveCatalog.Base+"/movies/quality-discovery-popular"};
        var native=main[0] with{Id=-998621,Title="Позднее отечественное 4K",PageUrl=LiveCatalog.Base+"/movies/quality-discovery-native",CountryKeys=[home]};
        var cancelled=native with{Id=-998622,Title="Отменённая проверка",PageUrl=LiveCatalog.Base+"/movies/quality-discovery-cancelled"};
        var fixtures=main.Concat(new[]{popular,native,cancelled}).ToArray();
        var started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledStarted=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var cancelledRelease=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=new List<int>();var active=0;var maximum=0;
        CatalogPage Page(MediaItem[] items)=>new(items,false,CatalogChoices.Genres,CatalogChoices.Countries);
        async Task WaitUntil(Func<bool> ready)
        {
            var end=DateTime.UtcNow.AddSeconds(6);while(!ready()&&DateTime.UtcNow<end)await Task.Delay(20);
            if(!ready())throw new Exception("Discovery quality fixture did not settle.");
        }
        try
        {
            CancelCatalogQualityCheck();ResetDiscoveryData();prefs.CatalogQualityHeight=2160;prefs.HidePoorQuality=false;
            foreach(var item in fixtures){cardMetadata[item.Id]=Task.FromResult(item);requestedDetails.Add(item.Id);qualitySnapshots.Remove(item.Id);catalogQualityAttempts.Remove(item.Id);}
            catalogQualityProvider=async(item,token)=>
            {
                calls.Add(item.Id);maximum=Math.Max(maximum,++active);
                try
                {
                    if(main.Any(candidate=>candidate.Id==item.Id)){if(main.All(candidate=>calls.Contains(candidate.Id)))started.TrySetResult(true);await release.Task.WaitAsync(token);}
                    if(item.Id==cancelled.Id){cancelledStarted.TrySetResult(true);await cancelledRelease.Task;}
                    return [new SourceEntry("discovery-quality-"+item.Id,item.Title+" WEB-DL 2160p","Fixture","","magnet:?xt=urn:btih:"+new string('1',40),null)];
                }
                finally{active--;}
            };
            catalogPages[pageKey]=Page(main);catalogPages[popularKey]=Page([]);discoveryRegions[regionKey]=new([],[],false);
            liveItems=main.ToList();livePage=1;liveLoading=false;liveKey=CurrentCatalogKey;Render();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(6));var request=catalogQualityRequest;
            catalogPages[popularKey]=Page([popular]);StartDiscoveryQualityCheck();SetDiscoveryRegionalItems(category,home,[],[native]);
            check(request!=null&&ReferenceEquals(request,catalogQualityRequest)&&!request.IsCancellationRequested&&calls.Count==2&&catalogQualityQueue?.Scheduled.Count==4,"Late shelf-only quality candidates join the current request without restarting main-page lookups");
            check(!CatalogQualityMatches(popular)&&!CatalogQualityMatches(native),"Unknown shelf quality remains excluded until a real resolution result arrives");
            release.TrySetResult(true);await WaitUntil(()=>catalogQualityRequest==null&&catalogQualityAttempts.ContainsKey(native.Id));UpdateLayout();await Task.Delay(60);UpdateLayout();
            check(calls.Count(id=>id==popular.Id)==1&&calls.Count(id=>id==native.Id)==1&&maximum==2,"Popular and native shelf-only titles receive one lookup through the existing two-slot quality queue");
            check(discoveryShelves[DiscoveryShelfKind.Popular].Row.Content is CatalogRow popularRow&&popularRow.Items.Any(item=>item.Id==popular.Id)&&discoveryShelves[DiscoveryShelfKind.Native].Row.Content is CatalogRow nativeRow&&nativeRow.Items.Any(item=>item.Id==native.Id),"Verified 4K shelf-only titles appear in their poster rows after quality lookup");
            var completedCalls=calls.Count;Render();UpdateLayout();await Task.Delay(60);
            check(calls.Count==completedCalls&&catalogQualityRequest==null,"A completed discovery quality queue does not restart on an unchanged render");
            prefs.CatalogQualityHeight=1080;CancelCatalogQualityCheck();liveKey=CurrentCatalogKey;Render();UpdateLayout();
            check(discoveryShelves[DiscoveryShelfKind.Popular].Row.Content is CatalogRow hdPopular&&hdPopular.Items.Length==0&&discoveryShelves[DiscoveryShelfKind.Native].Row.Content is CatalogRow hdNative&&hdNative.Items.Length==0,"Shelf quality selection requires the exact resolution and preserves verified country classification");
            prefs.CatalogQualityHeight=2160;CancelCatalogQualityCheck();catalogPages[popularKey]=Page([cancelled]);liveKey=CurrentCatalogKey;Render();
            await cancelledStarted.Task.WaitAsync(TimeSpan.FromSeconds(6));var cancelRequest=catalogQualityRequest;section="Настройки";Render();
            check(cancelRequest!=null&&cancelRequest.IsCancellationRequested&&catalogQualityRequest==null,"Leaving the catalog cancels queued discovery quality work");
            cancelledRelease.TrySetResult(true);await Task.Delay(80);
            check(section=="Настройки"&&!catalogQualityAttempts.ContainsKey(cancelled.Id)&&!FreshQuality(cancelled),"A late quality response cannot restore old shelf results after navigation");
        }
        finally
        {
            release.TrySetResult(true);cancelledRelease.TrySetResult(true);CancelCatalogQualityCheck();catalogQualityProvider=oldProvider;
            prefs.CatalogQualityHeight=oldHeight;prefs.HidePoorQuality=oldHide;section=category;
            if(oldPage==null)catalogPages.Remove(pageKey);else catalogPages[pageKey]=oldPage;
            if(oldPopular==null)catalogPages.Remove(popularKey);else catalogPages[popularKey]=oldPopular;
            if(oldRegions==null)discoveryRegions.Remove(regionKey);else discoveryRegions[regionKey]=oldRegions;
            foreach(var item in fixtures){qualitySnapshots.Remove(item.Id);catalogQualityAttempts.Remove(item.Id);cardMetadata.Remove(item.Id);requestedDetails.Remove(item.Id);}
            liveItems=oldItems;liveKey=CurrentCatalogKey;Render();UpdateLayout();
        }
    }
}
