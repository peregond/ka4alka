using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Kachalka;

public partial class MainWindow
{
    Border? releaseLoadingIndicator;
    int releaseLoadingItemId;

    void RenderReleaseLoading(WrapPanel target,MediaItem item)
    {
        releaseLoadingItemId=item.Id;
        var content=new StackPanel();
        var label=Text("Ищем раздачи…",11);label.Margin=new(0,0,0,4);
        label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");content.Children.Add(label);
        var progress=new ProgressBar{IsIndeterminate=true,Height=3,BorderThickness=new(0)};
        progress.SetResourceReference(Control.ForegroundProperty,"Accent");progress.SetResourceReference(Control.BackgroundProperty,"Edge");
        AutomationProperties.SetName(progress,"Поиск раздач: прогресс");content.Children.Add(progress);
        releaseLoadingIndicator=new Border{Child=content,Width=108,Margin=new(10,5,0,5),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
        AutomationProperties.SetName(releaseLoadingIndicator,"Поиск раздач");target.Children.Add(releaseLoadingIndicator);
        RefreshReleaseLoadingIndicator();
    }
    void RefreshReleaseLoadingIndicator()
    {
        if(releaseLoadingIndicator==null)return;
        var checking=current?.IsLive==true&&current.Id==releaseLoadingItemId&&releaseViews.TryGetValue(current.Id,out var view)&&view.Checking;
        releaseLoadingIndicator.Visibility=checking?Visibility.Visible:Visibility.Collapsed;
        RefreshReleaseSourceStatus();
    }

    async Task FetchDetails(MediaItem item)
    {
        // Movie information has its own request and lifetime; source retries or
        // cancellation must not discard a completed description and credits.
        var detailTask=FetchDetailMetadata(item);
        if(!releaseViews.TryGetValue(item.Id,out var view))releaseViews[item.Id]=view=new();
        view.Request?.Cancel();view.Request?.Dispose();var request=view.Request=new CancellationTokenSource();var token=request.Token;
        var prior=view.Sources.GroupBy(x=>x.Name).ToDictionary(x=>x.Key,x=>x.Last().LastSuccessUtc);
        var saved=liveReleases.GetValueOrDefault(item.Id)?.ToArray()??[];
        var fresh=new List<SourceEntry>();var checks=new Dictionary<string,SourceCheck>();
        view.Saved=saved.Length>0;view.SavedUtc??=view.ReceivedUtc;view.ReceivedUtc=null;view.Checking=true;
        bool IsCurrent()=>!closed&&!token.IsCancellationRequested&&ReferenceEquals(view.Request,request);
        SourceCheck Remember(SourceCheck check)=>check.LastSuccessUtc.HasValue?check:check with{LastSuccessUtc=prior.GetValueOrDefault(check.Name)};
        void Publish()
        {
            if(!IsCurrent())return;
            var keys=fresh.Select(ReleaseSearch.Identity).ToHashSet();
            view.Saved=saved.Any(x=>!keys.Contains(ReleaseSearch.Identity(x)));
            if(view.Saved)cachedReleaseViews.Add(item.Id);else cachedReleaseViews.Remove(item.Id);
            liveReleases[item.Id]=ReleaseSearch.WithSaved(fresh,saved);view.Sources=checks.Values.ToArray();
            qualitySnapshots.Remove(item.Id);ApplyKnownQuality(item);if(current?.Id==item.Id)ApplyKnownQuality(current);
            foreach(var card in liveItems.Concat(prefs.LiveFavorites).Concat(catalogDisplay).Where(x=>x.Id==item.Id))ApplyKnownQuality(card);
            RefreshDetail(item.Id);
            if(!view.Checking&&(prefs.HidePoorQuality||prefs.CatalogQualityHeight!=0)&&current==null&&(section is "Фильмы" or "Сериалы")&&liveItems.Concat(prefs.LiveFavorites).Any(x=>x.Id==item.Id))RenderCatalogKeepingPosition();
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
        IndexRows(saved,SourceState.Saved);checks["Онлайн-индекс"]=Remember(new("Онлайн-индекс",SourceState.Searching));view.Sources=checks.Values.ToArray();
        await Task.Yield();if(!IsCurrent())return;
        var entered=false;
        try
        {
            await releaseSlots.WaitAsync(token);entered=true;
            async Task<MediaItem> Resolved(CancellationToken ct)
            {
                try{return await detailTask.WaitAsync(TimeSpan.FromSeconds(8),ct);}
                catch(TimeoutException){return item;}
            }
            // Supplement cached/indexed rows concurrently: a single screen copy must not suppress better sources.
            var providers=new[]{new ReleaseSource("Онлайн-индекс",ct=>onlineIndex.Releases(item,ct,allowRelay:true))}
                .Concat(NativeReleaseSources.Create(sourceClient,Resolved,item.Section=="Сериалы"));
            var settled=false;
            void Apply(ReleaseSearchUpdate update)
            {
                fresh.Clear();fresh.AddRange(update.Items);
                foreach(var source in update.Sources)checks[source.Name]=Remember(source);
                if(update.Items.Length>0)view.ReceivedUtc=update.Items.Select(x=>x.DataReceivedUtc).Max();
                IndexRows(update.Items,SourceState.Ready);Publish();
            }
            var progress=new Progress<ReleaseSearchUpdate>(update=>{if(IsCurrent()&&!settled)Apply(update);});
            var result=await ReleaseSearch.RunAsync(providers,progress,token);
            settled=true;if(!IsCurrent())return;Apply(result);
            if(!IsCurrent())return;
            view.Checking=false;Publish();
            // Do not change the cache timestamp when every provider fails. Partial success keeps saved alternatives.
            if(fresh.Count>0||ReleaseAvailability.ConfirmedEmpty(fresh,result.Sources))try{await catalogIndex.CacheReleasesAsync(item,fresh.Count==0?[]:ReleaseSearch.WithSaved(fresh,saved),result.Sources,token);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception) when(!token.IsCancellationRequested){checks["Поиск источников"]=new("Поиск источников",SourceState.Unavailable,CheckedUtc:DateTime.UtcNow);}
        finally{if(entered)releaseSlots.Release();if(IsCurrent()){view.Checking=false;Publish();}}
    }
}
