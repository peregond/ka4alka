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
    CancellationTokenSource? featuredRequest;
    readonly Dictionary<string,DiscoveryShelfView> featuredShelves=[];
    TextBlock? featuredState;
    StackPanel? featuredPanel;
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
    void CancelFeaturedWork()
    {
        featuredRequest?.Cancel();featuredRequest?.Dispose();featuredRequest=null;
        featuredShelves.Clear();featuredState=null;featuredPanel=null;
        if(current==null&&section is not (FeaturedSection or "Настройки"))featuredReturn=null;
    }
    void OpenFeaturedCollection(string id)
    {
        featuredCollectionId=id;ResetCatalogNavigationPosition();Render();
    }
    void RenderFeatured()
    {
        var group=featuredGroups?.FirstOrDefault(group=>group.Collection.Id==featuredCollectionId);
        var definition=group?.Collection??FeaturedCatalog.Collections.FirstOrDefault(row=>row.Id==featuredCollectionId);
        var title=Text(definition?.Label??FeaturedSection,compactHeight?22:28);title.FontWeight=FontWeights.SemiBold;title.Name="FeaturedHeading";PageHeader.Children.Add(title);
        PageHeader.Children.Add(Text(definition?.Summary??"Выбираем истории, в которые стоит погрузиться.",13,true));
        if(featuredCollectionId.Length>0)
        {
            SetBack("Все подборки",()=>OpenFeaturedCollection(""));ShowCatalog([]);
            featuredState=Text("Загружаем подборки…",14,true);PageHeader.Children.Add(featuredState);
        }
        else
        {
            var panel=featuredPanel=new StackPanel{Name="FeaturedCollections"};
            Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            var definitions=featuredGroups?.Select(row=>row.Collection).ToArray()??FeaturedCatalog.Collections.ToArray();
            foreach(var collection in definitions)AddFeaturedShelf(panel,collection);
            featuredState=Text("Загружаем подборки…",13,true);panel.Children.Add(featuredState);
        }
        if(featuredGroups!=null)RefreshFeaturedRows();else ReloadFeatured();
        ResizeFeaturedShelves();
    }
    void AddFeaturedShelf(StackPanel panel,FeaturedCollection collection)
    {
        var view=AddShelf(panel,"Featured"+collection.Id.Replace("-",""),collection.Label,()=>OpenFeaturedCollection(collection.Id),collection.IsGames?"GameRow":"MediaRow",1);
        featuredShelves[collection.Id]=view;
        if(collection.Summary.Length>0&&view.Row.Parent is StackPanel shelf)
        {var description=TextRaw(collection.Summary,13,true);description.Margin=new(0,0,0,12);shelf.Children.Insert(1,description);}
        view.Set([],true);
    }
    async void ReloadFeatured()
    {
        if(closed||section!=FeaturedSection||current!=null)return;
        featuredRequest?.Cancel();featuredRequest?.Dispose();
        var request=featuredRequest=CancellationTokenSource.CreateLinkedTokenSource(metadataLifetime.Token);
        var token=request.Token;
        try
        {
            var groups=featuredProvider!=null?await featuredProvider(token):await Load();
            if(token.IsCancellationRequested||closed||section!=FeaturedSection||current!=null)return;
            featuredGroups=groups;
            RefreshFeaturedRows();
            async Task<FeaturedGroup[]> Load()
            {
                await BundledCatalog.EnsureLoadedAsync(token);
                await sharedCatalog.RestoreAsync(token);
                return await Task.Run(()=>FeaturedCatalog.Resolve(sharedCatalog.All,sharedCatalog.ActiveCollections),token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error)
        {
            if(closed||token.IsCancellationRequested)return;
            ErrorLog.Write(error);
            foreach(var shelf in featuredShelves.Values)shelf.Set([],false);
            if(featuredState!=null){featuredState.Text=AppLanguage.T("Не удалось загрузить подборки. Попробуй ещё раз.");featuredState.Visibility=Visibility.Visible;}
        }
    }
    MediaItem[] FeaturedVisible(FeaturedGroup group)=>group.Items.Where(item=>PersonallyVisible(item)&&!NoDownloads(item)&&(item.Section=="Игры"||CatalogQualityMatches(item))).ToArray();
    void RefreshFeaturedRows()
    {
        if(section!=FeaturedSection||current!=null||featuredGroups==null)return;
        if(featuredCollectionId.Length==0)
        {
            foreach(var group in featuredGroups)
            {
                if(!featuredShelves.ContainsKey(group.Collection.Id)&&featuredPanel!=null)AddFeaturedShelf(featuredPanel,group.Collection);
                if(featuredShelves.TryGetValue(group.Collection.Id,out var shelf))shelf.Set(FeaturedVisible(group),false);
            }
            if(featuredState!=null)featuredState.Visibility=Visibility.Collapsed;
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
        foreach(var pair in featuredShelves)
        {
            var games=FeaturedCatalog.Collections.FirstOrDefault(row=>row.Id==pair.Key)?.IsGames==true;
            pair.Value.Resize(games?WindowSizing.GameColumns(width):WindowSizing.PosterColumns(width));
        }
    }
}
