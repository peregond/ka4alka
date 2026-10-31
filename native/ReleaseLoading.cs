using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Kachalka;

public partial class MainWindow
{
    ReleaseAudioRulesClient? releaseAudioRules;
    Border? releaseLoadingIndicator;
    int releaseLoadingItemId;

    void RenderReleaseLoading(WrapPanel target,MediaItem item)
    {
        releaseLoadingItemId=item.Id;
        var content=new StackPanel();
        var label=Text("Ищем раздачи…",11);label.Margin=new(0,0,0,4);
        label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");content.Children.Add(label);
        // A calm breathing line instead of a running bar; the release rows below breathe the same way.
        var progress=new Border{Name="ReleaseLoadingPulse",Height=3,CornerRadius=new(1.5)};progress.SetResourceReference(Border.BackgroundProperty,"Accent");
        AutomationProperties.SetName(progress,AppLanguage.T("Поиск раздач: прогресс"));content.Children.Add(Breathe(progress));
        releaseLoadingIndicator=new Border{Child=content,Width=108,Margin=new(10,5,0,5),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
        AutomationProperties.SetName(releaseLoadingIndicator,AppLanguage.T("Поиск раздач"));target.Children.Add(releaseLoadingIndicator);
        RefreshReleaseLoadingIndicator();
    }
    void RefreshReleaseLoadingIndicator()
    {
        if(releaseLoadingIndicator==null)return;
        var checking=current?.IsLive==true&&current.Id==releaseLoadingItemId&&releaseViews.TryGetValue(current.Id,out var view)&&view.Checking;
        releaseLoadingIndicator.Visibility=checking?Visibility.Visible:Visibility.Collapsed;
        RefreshReleaseSourceStatus();
    }

    async Task RestoreAndFetchDetails(MediaItem item)
    {
        var cached=await Task.Run(()=>catalogIndex.CachedReleaseSnapshot(item));
        if(closing||closed||current?.Id!=item.Id||activePerson!=null){requestedDetails.Remove(item.Id);return;}
        if(cached is {Items.Length:>0}&&!liveReleases.ContainsKey(item.Id))
        {
            liveReleases[item.Id]=cached.Items;cachedReleaseViews.Add(item.Id);
            releaseViews[item.Id]=new(){Saved=true,SavedUtc=cached.SavedUtc,Sources=cached.Sources??[]};
            QueueDetailRefresh(item.Id,releasesOnly:true);
        }
        await FetchDetails(item);
    }

    void CancelStaleDetailWork()
    {
        var active=activePerson==null&&(section is "Фильмы" or "Сериалы" or "ТВ Шоу")?current?.Id:null;
        foreach(var pair in releaseViews.Where(pair=>pair.Key!=active&&pair.Value.Checking).ToArray())
        {
            pair.Value.Request?.Cancel();pair.Value.Checking=false;requestedDetails.Remove(pair.Key);
        }
        foreach(var pair in detailMetadataViews.Where(pair=>pair.Key!=active&&pair.Value.Loading).ToArray())
        {
            pair.Value.Request.Cancel();detailMetadataViews.Remove(pair.Key);cardMetadata.Remove(pair.Key);resolvedPriorityMetadata.Remove(pair.Key);
        }
    }

    async Task FetchDetails(MediaItem item)
    {
        // Movie information has its own request and lifetime; source retries or
        // cancellation must not discard a completed description and credits.
        var detailTask=FetchDetailMetadata(item);
        if(!releaseViews.TryGetValue(item.Id,out var view))releaseViews[item.Id]=view=new();
        view.Request?.Cancel();view.Request?.Dispose();var request=view.Request=new CancellationTokenSource();var token=request.Token;
        var releasesSettled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(current?.Id==item.Id)castAfterReleases=Task.WhenAny(releasesSettled.Task,Task.Delay(1500));
        var prior=view.Sources.GroupBy(x=>x.Name).ToDictionary(x=>x.Key,x=>x.Last().LastSuccessUtc);
        var saved=liveReleases.GetValueOrDefault(item.Id)?.ToArray()??[];
        var fresh=new List<SourceEntry>();var checks=new Dictionary<string,SourceCheck>();
        view.Saved=saved.Length>0;view.SavedUtc??=view.ReceivedUtc;view.ReceivedUtc=null;view.Checking=true;
        bool IsCurrent()=>!closed&&!token.IsCancellationRequested&&ReferenceEquals(view.Request,request);
        SourceCheck Remember(SourceCheck check)=>check.LastSuccessUtc.HasValue?check:check with{LastSuccessUtc=prior.GetValueOrDefault(check.Name)};
        var releaseRowsDirty=true;var publishedChecking=view.Checking;
        void Publish()
        {
            if(!IsCurrent())return;
            view.Sources=checks.Values.ToArray();
            if(releaseRowsDirty)
            {
                var keys=fresh.Select(ReleaseSearch.Identity).ToHashSet();
                view.Saved=saved.Any(x=>!keys.Contains(ReleaseSearch.Identity(x)));
                if(view.Saved)cachedReleaseViews.Add(item.Id);else cachedReleaseViews.Remove(item.Id);
                liveReleases[item.Id]=ReleaseSearch.WithSaved(fresh,saved);
                qualitySnapshots.Remove(item.Id);ApplyKnownQuality(item);if(current?.Id==item.Id)ApplyKnownQuality(current);
                foreach(var card in liveItems.Concat(prefs.LiveFavorites).Concat(catalogDisplay).Where(x=>x.Id==item.Id))ApplyKnownQuality(card);
            }
            if(releaseRowsDirty||publishedChecking!=view.Checking)QueueDetailRefresh(item.Id,releasesOnly:true);
            else RefreshReleaseLoadingIndicator();
            releaseRowsDirty=false;publishedChecking=view.Checking;
            if(!view.Checking&&(prefs.HidePoorQuality||prefs.CatalogQualityHeight!=0)&&current==null&&(section is "Фильмы" or "Сериалы" or "ТВ Шоу")&&liveItems.Concat(prefs.LiveFavorites).Any(x=>x.Id==item.Id))RenderCatalogKeepingPosition();
        }
        void IndexRows(IEnumerable<SourceEntry> rows,SourceState state)
        {
            foreach(var group in rows.GroupBy(ReleaseFreshness.SourceName))
            {
                // A reply from the online index does not mean that the named
                // tracker answered our request. Preserve each provider's own state.
                if(checks.TryGetValue(group.Key,out var checkedSource)&&checkedSource.State!=SourceState.Saved)continue;
                var rowState=state==SourceState.Ready&&group.All(x=>x.DataProvider!=group.Key)?SourceState.Indexed:state;
                checks[group.Key]=Remember(new(group.Key,rowState,group.Count()));
            }
        }
        IndexRows(saved,SourceState.Saved);if(!ImdbSuggestions.IsImdbOnly(item))checks["Онлайн-индекс"]=Remember(new("Онлайн-индекс",SourceState.Searching));view.Sources=checks.Values.ToArray();
        await Task.Yield();if(!IsCurrent()){releasesSettled.TrySetResult();return;}
        var entered=false;
        try
        {
            await releaseSlots.WaitAsync(token);entered=true;
            Task<MediaItem> Resolved(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                // Descriptions and cast can be slow. Trackers can search the known title immediately.
                return Task.FromResult(detailTask.IsCompletedSuccessfully?detailTask.Result:item);
            }
            async Task<MediaItem> Aliases(CancellationToken ct)
            {
                // English-only sources need an original title only when the known name is Cyrillic.
                // Russian providers start immediately and do not join this wait.
                var known=await Resolved(ct);
                if(ImdbSuggestions.IsImdbOnly(known)||!string.IsNullOrWhiteSpace(known.OriginalTitle)||!known.Title.Any(c=>c is >= '\u0400' and <= '\u04FF'))return known;
                try{return await detailTask.WaitAsync(TimeSpan.FromSeconds(8),ct);}
                catch(TimeoutException){return known;}
            }
            // Supplement cached/indexed rows concurrently: a single screen copy must not suppress better sources.
            // The online index only knows catalog titles; an IMDb-only title goes straight to the trackers.
            var providers=(ImdbSuggestions.IsImdbOnly(item)?[]:new[]{new ReleaseSource("Онлайн-индекс",ct=>onlineIndex.Releases(item,ct,allowRelay:true))})
                .Concat(NativeReleaseSources.Create(sourceClient,Resolved,item.Section=="Сериалы",Aliases));
            var settled=false;
            void Apply(ReleaseSearchUpdate update)
            {
                if(!fresh.SequenceEqual(update.Items)){fresh.Clear();fresh.AddRange(update.Items);releaseRowsDirty=true;}
                foreach(var source in update.Sources)checks[source.Name]=Remember(source);
                if(update.Items.Length>0)view.ReceivedUtc=update.Items.Select(x=>x.DataReceivedUtc).Max();
                IndexRows(update.Items,SourceState.Ready);Publish();
            }
            var progress=new Progress<ReleaseSearchUpdate>(update=>{if(IsCurrent()&&!settled)Apply(update);});
            var rulesClient=releaseAudioRules??=new(sourceClient);
            var rules=Task.Run(()=>rulesClient.Refresh(token),token);
            var result=await ReleaseSearch.RunAsync(providers,progress,token);
            await rules;
            settled=true;if(!IsCurrent())return;Apply(result);
            if(!IsCurrent())return;
            view.Checking=false;Publish();
            // Do not change the cache timestamp when every provider fails. Partial success keeps saved alternatives.
            if(fresh.Count>0||ReleaseAvailability.ConfirmedEmpty(fresh,result.Sources))try{await catalogIndex.CacheReleasesAsync(item,fresh.Count==0?[]:ReleaseSearch.WithSaved(fresh,saved),result.Sources,token);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception) when(!token.IsCancellationRequested){checks["Поиск источников"]=new("Поиск источников",SourceState.Unavailable,CheckedUtc:DateTime.UtcNow);}
        finally{releasesSettled.TrySetResult();if(entered)releaseSlots.Release();if(IsCurrent()){view.Checking=false;Publish();}}
    }
}
