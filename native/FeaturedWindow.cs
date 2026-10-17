using System.Windows;
using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    const string FeaturedSection="Актуальное";
    bool FeaturedGameGrid=>featuredGroups?.FirstOrDefault(group=>group.Collection.Id==featuredCollectionId)?.Collection.IsGames==true||FeaturedCatalog.Collections.FirstOrDefault(row=>row.Id==featuredCollectionId)?.IsGames==true;
    string featuredCollectionId="";
    string? featuredReturn;
    FeaturedGroup[]? featuredGroups;
    string featuredLanguage="";
    CancellationTokenSource? featuredHubRequest;
    readonly Dictionary<string,DiscoveryShelfView> featuredShelves=[];
    TextBlock? featuredState;
    StackPanel? featuredPanel;
    Button? featuredRetry;
    internal Func<CancellationToken,Task<FeaturedGroup[]>>? featuredProvider;
    void ShowFeatured()
    {
        searchDelay.Stop();liveRequest?.Cancel();liveLoading=false;CancelPeopleSearch(true);
        current=null;activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;featuredReturn=null;catalogBrowseContext=null;
        section=FeaturedSection;submittedQuery="";Search.Text="";favoritesOnly=false;featuredCollectionId="";
        ResetCatalogNavigationPosition();Render();
    }
    string? FeaturedBackLabel()=>featuredReturn!=null?FeaturedSection:null;
    bool ReturnToFeatured()
    {
        if(featuredReturn==null)return false;
        featuredCollectionId=featuredReturn;featuredReturn=null;section=FeaturedSection;current=null;activePerson=null;personOrigin=null;returnPerson=null;
        submittedQuery="";Search.Text="";Render();return true;
    }
    AwardsCatalog? awardsCatalog;
    AwardsCatalog Awards=>awardsCatalog??=new(sourceClient);
    // The backend's award lists are checked once a day; when they changed, the page loads them again.
    async Task RefreshAwardsAsync()
    {
        try{if(await Awards.RefreshAsync(metadataLifetime.Token)&&!closed&&section==FeaturedSection&&current==null)ReloadFeatured();}
        catch(OperationCanceledException){}
        catch(Exception error){ErrorLog.Write(error);}
    }
    void CancelFeaturedWork()
    {
        featuredHubRequest?.Cancel();featuredHubRequest?.Dispose();featuredHubRequest=null;featuredRefresh++;featuredDeferred.Clear();
        featuredShelves.Clear();featuredState=null;featuredPanel=null;featuredRetry=null;featuredHero=null;featuredDigest=null;
        if(current==null&&section is not (FeaturedSection or "Настройки"))featuredReturn=null;
    }
    void OpenFeaturedCollection(string id)
    {
        featuredCollectionId=id;ResetCatalogNavigationPosition();Render();
    }
    void RenderFeatured()
    {
        // Titles of seasonal shelves and awards are resolved in the interface language; a language change needs a new resolve.
        if(featuredGroups!=null&&featuredLanguage!=AppLanguage.Current)featuredGroups=null;
        var group=featuredGroups?.FirstOrDefault(group=>group.Collection.Id==featuredCollectionId);
        var definition=group?.Collection??FeaturedCatalog.Collections.FirstOrDefault(row=>row.Id==featuredCollectionId);
        var title=Text(definition?.Label??FeaturedSection,compactHeight?22:28);title.FontWeight=FontWeights.SemiBold;title.Name="FeaturedHeading";PageHeader.Children.Add(title);
        // The front page shows today's date under the title, like a newspaper; a collection shows its own description.
        PageHeader.Children.Add(definition!=null?Text(definition.Summary,13,true):TextRaw(FeaturedDateLine()+" · "+AppLanguage.T("Что смотреть и во что играть"),13,true));
        if(featuredCollectionId.Length>0)
        {
            SetBack("Все подборки",()=>OpenFeaturedCollection(""));ShowCatalog([]);
            featuredState=Text("Загружаем подборки…",14,true);PageHeader.Children.Add(featuredState);
        }
        else
        {
            var panel=featuredPanel=new StackPanel{Name="FeaturedCollections"};
            // The banner and the digest of top stories come first; both fill in when the collections have loaded.
            featuredHeroKey=featuredDigestKey=null;featuredHeroIds.Clear();featuredUsed.Clear();
            featuredHero=new Grid{Name="FeaturedHero"};featuredDigest=new StackPanel{Name="FeaturedDigest"};
            panel.Children.Add(featuredHero);panel.Children.Add(featuredDigest);
            if(featuredGroups==null)featuredHero.Children.Add(BannerSkeleton());
            Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            var definitions=featuredGroups?.Select(row=>row.Collection).ToArray()??FeaturedCatalog.Collections.ToArray();
            foreach(var collection in definitions)AddFeaturedShelf(panel,collection);
            featuredState=Text("Загружаем подборки…",13,true);panel.Children.Add(featuredState);
        }
        featuredRetry=Button("Повторить загрузку",ReloadFeatured);featuredRetry.Style=(Style)FindResource("QuietButton");featuredRetry.Visibility=Visibility.Collapsed;PageHeader.Children.Add(featuredRetry);
        if(featuredGroups!=null)RefreshFeaturedRows();else ReloadFeatured();
        ResizeFeaturedShelves();
    }
    void AddFeaturedShelf(StackPanel panel,FeaturedCollection collection)
    {
        var view=AddShelf(panel,"Featured"+collection.Id.Replace("-",""),collection.Label,()=>OpenFeaturedCollection(collection.Id),collection.IsGames?"GameRow":"MediaRow",1,collection.Kicker);
        featuredShelves[collection.Id]=view;
        // A rubric with its own shape draws itself into a block under the heading instead of the poster row.
        if(!collection.IsGames&&collection.Layout is "lead" or "ranking"&&view.Row.Parent is StackPanel section)
        {
            var host=new StackPanel{Name="Featured"+collection.Id.Replace("-","")+"Block"};section.Children.Add(host);view.Host=host;
            view.Custom=collection.Layout=="lead"?(items,columns)=>FillFeaturedLead(host,items,columns):(items,columns)=>FillFeaturedRanking(host,items,columns);
        }
        if(collection.Summary.Length>0&&view.Row.Parent is StackPanel shelf)
        {var description=TextRaw(collection.Summary,13,true);description.Margin=new(0,0,0,12);shelf.Children.Insert(1,description);}
        view.Set([],true);
    }
    async void ReloadFeatured()
    {
        if(closed||section!=FeaturedSection||current!=null)return;
        featuredHubRequest?.Cancel();featuredHubRequest?.Dispose();
        var request=featuredHubRequest=CancellationTokenSource.CreateLinkedTokenSource(metadataLifetime.Token);
        var token=request.Token;
        try
        {
            var groups=featuredProvider!=null?await featuredProvider(token).WaitAsync(token):await Load();
            if(token.IsCancellationRequested||closed||section!=FeaturedSection||current!=null)return;
            featuredGroups=groups;featuredLanguage=AppLanguage.Current;
            if(featuredRetry!=null)featuredRetry.Visibility=Visibility.Collapsed;
            RefreshFeaturedRows();
            async Task<FeaturedGroup[]> Load()
            {
                await BundledCatalog.EnsureLoadedAsync(token);
                await sharedCatalog.RestoreAsync(token);await Awards.RestoreAsync(token);
                _=RefreshAwardsAsync();
                return await Task.Run(()=>FeaturedCatalog.Resolve(sharedCatalog.All,sharedCatalog.ActiveCollections,Awards.All),token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error)
        {
            if(closed||token.IsCancellationRequested)return;
            ErrorLog.Write(error);
            if(featuredGroups==null)foreach(var shelf in featuredShelves.Values)shelf.Set([],false);
            featuredHero?.Children.Clear();featuredHeroKey=featuredDigestKey=null;
            if(featuredRetry!=null)featuredRetry.Visibility=Visibility.Visible;
            if(featuredState!=null){featuredState.Text=AppLanguage.T("Не удалось загрузить подборки. Попробуй ещё раз.");featuredState.Visibility=Visibility.Visible;}
        }
    }
    int featuredRefresh;
    readonly Queue<Action> featuredDeferred=new();
    void DrainFeaturedShelves(int version)
    {
        if(version!=featuredRefresh||featuredDeferred.Count==0)return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>
        {
            if(version!=featuredRefresh||closed)return;
            if(featuredDeferred.TryDequeue(out var step))step();
            DrainFeaturedShelves(version);
        }));
    }
    MediaItem[] FeaturedVisible(FeaturedGroup group)=>group.Items.Where(item=>PersonallyVisible(item)&&!NoDownloads(item)&&(item.Section=="Игры"||CatalogQualityMatches(item))).ToArray();
    void RefreshFeaturedRows()
    {
        if(section!=FeaturedSection||current!=null||featuredGroups==null)return;
        if(featuredCollectionId.Length==0)
        {
            BuildFeaturedHero(featuredGroups);BuildFeaturedDigest(featuredGroups);
            featuredRefresh++;featuredDeferred.Clear();
            var active=featuredGroups.Select(group=>group.Collection.Id).ToHashSet(StringComparer.Ordinal);
            foreach(var id in featuredShelves.Keys.Where(id=>!active.Contains(id)).ToArray())
            {
                if(featuredShelves[id].Row.Parent is UIElement row)featuredPanel?.Children.Remove(row);
                featuredShelves.Remove(id);
            }
            for(var index=0;index<featuredGroups.Length;index++)
            {
                var group=featuredGroups[index];
                if(!featuredShelves.ContainsKey(group.Collection.Id)&&featuredPanel!=null)AddFeaturedShelf(featuredPanel,group.Collection);
                if(featuredShelves.TryGetValue(group.Collection.Id,out var shelf))
                {
                    // The banner and the digest hold the first two places of the page.
                    if(featuredPanel!=null&&shelf.Row.Parent is UIElement row&&featuredPanel.Children.IndexOf(row)!=index+2)
                    {featuredPanel.Children.Remove(row);featuredPanel.Children.Insert(Math.Min(index+2,featuredPanel.Children.Count),row);}
                    var visible=FeaturedVisible(group);
                    void Fill()
                    {
                        shelf.Set(visible,false);
                        // A shelf emptied by the person's own filters is hidden rather than shown as unavailable.
                        if(shelf.Row.Parent is UIElement whole)whole.Visibility=visible.Length>0?Visibility.Visible:Visibility.Collapsed;
                    }
                    // The first shelves appear at once; the rest are built one by one while the page is already usable.
                    if(index<3)Fill();else featuredDeferred.Enqueue(Fill);
                }
            }
            if(featuredState!=null)featuredState.Visibility=Visibility.Collapsed;
            DrainFeaturedShelves(featuredRefresh);
        }
        else
        {
            var group=featuredGroups.FirstOrDefault(row=>row.Collection.Id==featuredCollectionId);
            var items=group==null?[]:FeaturedVisible(group);catalogDisplay=items;
            var columns=Math.Max(1,catalogColumns);var rows=items.Chunk(columns).ToArray();
            if(catalogRows!=null)
            {
                for(var i=0;i<rows.Length;i++){if(i<catalogRows.Count)catalogRows[i].Update(rows[i]);else catalogRows.Add(new(rows[i],columns));}
                while(catalogRows.Count>rows.Length)catalogRows.RemoveAt(catalogRows.Count-1);
            }
            if(featuredState!=null){featuredState.Text=AppLanguage.T("В этой подборке пока нет доступных карточек.");featuredState.Visibility=items.Length==0?Visibility.Visible:Visibility.Collapsed;}
            ApplyPosterDownloads(items);
        }
        ResizeFeaturedShelves();
    }
    void ResizeFeaturedShelves()
    {
        if(section!=FeaturedSection||current!=null||featuredCollectionId.Length>0)return;
        var width=Body.ActualWidth>0?Body.ActualWidth:Math.Max(320,Width-280);
        featuredWidth=width;if(carousel!=null)ArrangeFeatureCarousel(width);
        // The digest only changes shape when its column count does; resizing the window by a few pixels must not rebuild it.
        if(featuredGroups!=null&&DigestColumns(width)!=featuredDigestColumns)BuildFeaturedDigest(featuredGroups);
        foreach(var pair in featuredShelves)
        {
            var games=FeaturedCatalog.Collections.FirstOrDefault(row=>row.Id==pair.Key)?.IsGames==true;
            pair.Value.Resize(games?WindowSizing.GameColumns(width):WindowSizing.PosterColumns(width));
        }
    }
}
