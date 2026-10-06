using System.IO;

namespace Kachalka;

public partial class MainWindow
{
    async Task FetchDetails(MediaItem item)
    {
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
            liveReleases[item.Id]=ReleaseSearch.WithSaved(fresh,saved);view.Sources=checks.Values.ToArray();RefreshDetail(item.Id);
        }
        void IndexRows(IEnumerable<SourceEntry> rows,SourceState state)
        {
            foreach(var group in rows.GroupBy(x=>x.Source+(x.Via==null?"":" · через "+x.Via)))checks[group.Key]=Remember(new(group.Key,state,group.Count()));
        }
        IndexRows(saved,SourceState.Saved);checks["Онлайн-индекс"]=Remember(new("Онлайн-индекс",SourceState.Searching));view.Sources=checks.Values.ToArray();
        await Task.Yield();if(!IsCurrent())return;
        async Task<MediaItem> Resolve()
        {
            try
            {
                var detail=await Metadata(item,true);
                if(IsCurrent())
                {
                    item.SetScores(detail.Kinopoisk,detail.Imdb);detail.SetScores(detail.Kinopoisk,detail.Imdb);
                    liveItems=liveItems.Select(x=>x.Id==detail.Id?detail:x).ToArray();if(current?.Id==item.Id)current=detail;
                    if(prefs.LiveFavorites.Any(x=>x.Id==detail.Id)){prefs.LiveFavorites=prefs.LiveFavorites.Select(x=>x.Id==detail.Id?detail:x).ToList();prefs.Save();}
                    try{await catalogIndex.AddAsync([detail],token);}catch(IOException){}catch(UnauthorizedAccessException){}
                    RefreshDetail(item.Id);
                }
                return detail;
            }
            catch{return item;}
        }
        var detailTask=Resolve();var entered=false;
        try
        {
            await releaseSlots.WaitAsync(token);entered=true;
            var indexSettled=false;
            var indexProgress=new Progress<ReleaseSearchUpdate>(update=>
            {
                if(!IsCurrent()||indexSettled)return;
                foreach(var source in update.Sources)checks[source.Name]=Remember(source);
                if(update.Complete){fresh.Clear();fresh.AddRange(update.Items);if(update.Items.Length>0)view.ReceivedUtc=DateTime.UtcNow;IndexRows(update.Items,SourceState.Indexed);}
                Publish();
            });
            var indexed=await ReleaseSearch.RunAsync([new("Онлайн-индекс",ct=>onlineIndex.Releases(item,ct))],indexProgress,token);
            indexSettled=true;
            if(!IsCurrent())return;
            // Apply final results here as well: Progress callbacks are posted to the UI queue.
            foreach(var source in indexed.Sources)checks[source.Name]=Remember(source);
            fresh.Clear();fresh.AddRange(indexed.Items);if(indexed.Items.Length>0)view.ReceivedUtc=DateTime.UtcNow;IndexRows(indexed.Items,SourceState.Indexed);
            if(indexed.Items.Length==0||indexed.Items.All(x=>x.TorrentUrl==null))
            {
                MediaItem resolved;
                try{resolved=await detailTask.WaitAsync(TimeSpan.FromSeconds(8),token);}catch(TimeoutException){resolved=item;}
                var api=new LiveCatalog(sourceClient);
                var aliases=new[]{resolved.Title,resolved.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                async Task<IReadOnlyList<SourceEntry>> Aliases(Func<string,CancellationToken,Task<IReadOnlyList<SourceEntry>>> search,CancellationToken ct)
                {
                    var groups=await Task.WhenAll(aliases.Select(async title=>
                    {
                        try{return (Items:await search(title,ct),Error:(Exception?)null);}
                        catch(Exception error) when(!ct.IsCancellationRequested){return (Items:(IReadOnlyList<SourceEntry>)[],Error:error);}
                    }));
                    if(groups.All(x=>x.Error!=null))throw groups.First().Error!;
                    return groups.SelectMany(x=>x.Items).Where(x=>LiveCatalog.Matches(resolved,x)).ToArray();
                }
                var providers=new List<ReleaseSource>
                {
                    new("RuTor",ct=>Aliases(api.Releases,ct)),
                    new("Internet Archive",ct=>Aliases((title,t)=>sourceClient.SearchArchive(title,"Фильмы",1,t),ct)),
                    new("RuTracker · через Knaben",ct=>KnabenSource.Search(resolved,ct)),
                    new("NNM-Club",ct=>new NnmClubSource(sourceClient).Search(resolved,ct)),
                    new("MegaPeer",ct=>new MegaPeerSource(sourceClient).Search(resolved,ct))
                };
                var searchName=resolved.OriginalTitle??resolved.Title;
                if(item.Section=="Сериалы"&&!searchName.Any(c=>c is >= '\u0400' and <= '\u04FF'))
                {
                    providers.Add(new("Nyaa",async ct=>(await sourceClient.SearchNyaa(searchName,ct)).Where(x=>LiveCatalog.Matches(resolved,x)).ToArray()));
                    providers.Add(new("EZTV",async ct=>{var identity=await sourceClient.FindSeriesIdentity(searchName,item.Year,ct);return identity==null?[]:await sourceClient.SearchEztv(identity,ct);}));
                }
                var fallbackSettled=false;
                var fallbackProgress=new Progress<ReleaseSearchUpdate>(update=>
                {
                    if(!IsCurrent()||fallbackSettled)return;
                    fresh.Clear();fresh.AddRange(indexed.Items);fresh.AddRange(update.Items);
                    foreach(var source in update.Sources)checks[source.Name]=Remember(source);
                    if(update.Items.Length>0)view.ReceivedUtc=DateTime.UtcNow;Publish();
                });
                var direct=await ReleaseSearch.RunAsync(providers,fallbackProgress,token);
                fallbackSettled=true;
                if(!IsCurrent())return;
                fresh.Clear();fresh.AddRange(indexed.Items);fresh.AddRange(direct.Items);
                foreach(var source in direct.Sources)checks[source.Name]=Remember(source);
                if(direct.Items.Length>0)view.ReceivedUtc=DateTime.UtcNow;
            }
            if(!IsCurrent())return;
            view.Checking=false;Publish();
            // Do not change the cache timestamp when every provider fails. Partial success keeps saved alternatives.
            if(fresh.Count>0)try{await catalogIndex.CacheReleasesAsync(item,ReleaseSearch.WithSaved(fresh,saved),view.Sources);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception) when(!token.IsCancellationRequested){checks["Поиск источников"]=new("Поиск источников",SourceState.Unavailable,CheckedUtc:DateTime.UtcNow);}
        finally{if(entered)releaseSlots.Release();if(IsCurrent()){view.Checking=false;Publish();}}
    }
}
