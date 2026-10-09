using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Kachalka;
public partial class MainWindow
{
    readonly bool demoCatalog=Environment.GetCommandLineArgs().Any(x=>x is "--smoke-test" or "--layout-smoke-test");
    IReadOnlyList<MediaItem> liveItems=[];
    string liveKey="",liveError="";
    bool liveLoading;
    int livePage=1;
    bool favoritesOnly;
    int? catalogYear;
    string catalogOrder="Сначала новые";
    Button? topAll;
    StackPanel? inlineCatalogFilters;
    ScrollViewer? inlineCatalogFilterScroll;
    Button? catalogFilterBack,catalogFilterForward;
    bool? catalogFiltersCompact;
    sealed record CatalogFilterCaption(string Full,string Compact,string? Icon=null);
    readonly Dictionary<Button,CatalogFilterCaption> catalogFilterCaptions=[];
    Grid? detailHero;
    WrapPanel? detailMetaRow;
    WrapPanel? detailRatings;
    StackPanel? detailInfo;
    Border? detailPoster;
    TextBlock? detailSynopsis,detailTitle;
    StackPanel? detailDescription;
    Button? descriptionToggle;
    int descriptionItemId;
    bool descriptionExpanded;
    CancellationTokenSource? liveRequest;
    readonly Dictionary<int,IReadOnlyList<SourceEntry>> liveReleases=[];
    readonly HashSet<int> cachedReleaseViews=[];
    readonly CatalogIndex catalogIndex=new(Preferences.DataDir);
    readonly HashSet<int> requestedDetails=[];
    readonly Dictionary<int,Task<MediaItem>> cardMetadata=[];
    readonly MetadataScheduler metadataScheduler=new();
    readonly HashSet<int> priorityMetadata=[];
    readonly OnlineIndexClient onlineIndex;
    Task<MediaItem> Metadata(MediaItem item,bool priority=false,CancellationToken ct=default,Action<MediaItem>? updated=null,bool force=false)
    {
        item=DownloadMetadata.EnrichMedia(item,new[]{sharedCatalog.Find(item)}.OfType<MediaItem>().Concat(BundledCatalog.Search(item.Section,item.Title)));
        if(force){cardMetadata.Remove(item.Id);resolvedPriorityMetadata.Remove(item.Id);}
        if(cardMetadata.TryGetValue(item.Id,out var task)&&(!priority||task.IsCompletedSuccessfully&&(resolvedPriorityMetadata.Contains(item.Id)||MediaMetadata.HasFullDetails(task.Result))||priorityMetadata.Contains(item.Id)))return task;
        if(!ct.CanBeCanceled)ct=metadataLifetime.Token;
        async Task<MediaItem> Load()
        {
            if(priority)priorityMetadata.Add(item.Id);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            if(priority)deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                return await metadataScheduler.Run(priority,async ()=>
                {
                    if(priority)
                    {
                        deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                        var result=await MediaMetadata.Load(item,token=>onlineIndex.Detail(item,token),token=>new LiveCatalog(sourceClient).Detail(item,token),updated,ct);
                        if(!result.Available)throw new IOException("Источники не вернули сведения о фильме.");
                        resolvedPriorityMetadata.Add(item.Id);return result.Item;
                    }
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    var indexed=item;
                    try{indexed=MediaMetadata.Merge(item,await onlineIndex.Detail(item,deadline.Token));}
                    catch(Exception) when(!deadline.IsCancellationRequested){}
                    if(MediaMetadata.HasDescription(indexed)&&indexed.GenreKeys.Length>0&&indexed.CountryKeys.Length>0)return indexed;
                    try
                    {
                        return MediaMetadata.Merge(indexed,await new LiveCatalog(sourceClient).Detail(indexed,deadline.Token));
                    }
                    catch(Exception) when(!ct.IsCancellationRequested&&MediaMetadata.Useful(indexed)){return indexed;}
                },deadline.Token);
            }
            finally{if(priority)priorityMetadata.Remove(item.Id);}
        }
        task=Load();if(cardMetadata.Count>=100)cardMetadata.Remove(cardMetadata.Keys.First());cardMetadata[item.Id]=task;return task;
    }
    async Task UpdateCardRatings(MediaItem item)
    {
        var task=Metadata(item);
        try{var data=await task;if(!closed)item.SetScores(data.Kinopoisk,data.Imdb,data.Genre);}
        catch{if(cardMetadata.TryGetValue(item.Id,out var latest)&&ReferenceEquals(task,latest))cardMetadata.Remove(item.Id); /* A missing rating stays unavailable. */ }
    }
    string catalogGenre="",catalogCountry="",catalogCollection="all",catalogRegion="";
    int catalogRating;
    bool catalogHasNext=true,catalogRefreshRequested;
    int? catalogLastPage;
    CatalogChoice[] catalogGenres=CatalogChoices.Genres,catalogCountries=CatalogChoices.Countries;
    readonly Dictionary<string,CatalogPage> catalogPages=[];
    DateTime catalogMemoryBoundary=SharedCatalog.BoundaryUtc(DateTime.UtcNow);
    bool ExpireCatalogPages()
    {
        var boundary=SharedCatalog.BoundaryUtc(DateTime.UtcNow);if(boundary==catalogMemoryBoundary)return false;
        catalogMemoryBoundary=boundary;catalogPages.Clear();ResetDiscoveryData();cardMetadata.Clear();catalogLastPage=null;if(!SearchActive)liveKey="";return true;
    }
    string CurrentCatalogKey=>(SearchActive?"Поиск|"+submittedQuery:section+"|"+livePage+"|"+CatalogSelection.Filter+"|"+CatalogSelection.Collection+"|region:"+catalogRegion+"|home:"+CatalogRegions.HomeCountry(prefs.HomeCountry))+"|quality:"+prefs.CatalogQualityHeight;
    CatalogSelection CatalogSelection=>new(catalogGenre,catalogCountry,catalogYear,catalogRating,catalogOrder switch{"По рейтингу"=>"rating","По популярности"=>"popular",_=>"date"},catalogCollection,catalogRegion,prefs.HomeCountry);
    void ResetCatalogFilters(){catalogYear=null;catalogGenre="";catalogCountry="";catalogRating=0;catalogOrder="Сначала новые";catalogCollection="all";catalogRegion="";catalogLastPage=null;}
    void ChangeCatalogFilter(Action change)
    {
        if(!SearchActive&&Search.Text.Length>0){Search.Text="";searchDelay.Stop();}
        CancelCatalogQualityCheck();change();livePage=1;catalogLastPage=null;if(!SearchActive)liveKey="";Render();
    }
    void GoCatalogPage(int page)
    {
        if(liveLoading||page==livePage)return;
        livePage=Math.Clamp(page,1,catalogLastPage??CatalogPaging.Limit);Render();UpdateLayout();
        FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToTop();
    }
    void RenderLiveCatalog()
    {
        ExpireCatalogPages();
        StartPeopleSearch();
        var selection=CatalogSelection;
        var key=CurrentCatalogKey;
        if(!favoritesOnly&&liveKey!=key){liveKey=key;_ = FetchCatalog(key,section,submittedQuery,livePage);}
        var headingRow=new DockPanel{Margin=new(5,0,8,5)};
        var count=Text(liveLoading?"Загружаем…":$"Страница {livePage}",11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(14,0,0,0);DockPanel.SetDock(count,Dock.Right);headingRow.Children.Add(count);
        var title=favoritesOnly?"Сохранённое":SearchActive?"Результаты поиска":catalogRegion=="native"?section=="Фильмы"?"Отечественные фильмы":"Отечественные сериалы":catalogRegion=="foreign"?section=="Фильмы"?"Иностранные фильмы":"Иностранные сериалы":catalogCollection=="popular"?"Популярное":catalogCollection=="rated"?"Кино с высоким рейтингом":section=="Фильмы"?"Все фильмы":"Все сериалы";
        var heading=Text(title,compactHeight?20:22);heading.FontWeight=FontWeights.Bold;heading.Margin=new(0);headingRow.Children.Add(heading);if(!DiscoveryCatalog)PageHeader.Children.Add(headingRow);
        var subtitle=Text(liveError.Length>0?liveError:favoritesOnly?"Кино, к которому хочется вернуться.":SearchActive?$"Результаты для «{submittedQuery}».":catalogCollection!="all"?"Подборка Zona · обновляется из общего каталога.":"Выбирай историю на сегодня.",13,true);subtitle.Tag="CatalogSubtitle";subtitle.Margin=new(5,3,0,12);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;if(!DiscoveryCatalog)PageHeader.Children.Add(subtitle);
        var toolbar=new DockPanel();catalogToolbar=toolbar;
        var tabs=new StackPanel{Orientation=Orientation.Horizontal};
        var sortBar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(8,0,0,0)};
        if(SearchActive)SearchTabs(tabs);
        else {
        topAll=Button("",()=>{favoritesOnly=false;catalogLastPage=null;livePage=1;Render();});topAll.Style=(Style)FindResource("PillButton");topAll.Content=ChipContent("Все",false);StyleChip(topAll,!favoritesOnly&&!ActiveCatalogFilters().Any());System.Windows.Automation.AutomationProperties.SetName(topAll,"Все");tabs.Children.Add(topAll);
        }
        if(!PeopleOnlySearch)
        {
            AddCatalogFilters(tabs,sortBar);
            tabs.Children.Add(CatalogQualityControls(()=>{livePage=1;Render();}));
        }
        if(!favoritesOnly)
        {
            var refresh=ActionButton("","IconRefresh",()=>{onlineIndex.RetryNow();ResetCatalogQualityChecks();catalogPages.Clear();ResetDiscoveryData();catalogRefreshRequested=true;liveKey="";Render();},"PillButton");catalogRefreshButton=refresh;refresh.ToolTip="Обновить каталог";System.Windows.Automation.AutomationProperties.SetName(refresh,"Обновить каталог");refresh.Padding=new(0);refresh.Width=40;refresh.Margin=new(8,0,0,8);sortBar.Children.Add(refresh);
        }
        inlineCatalogFilters=tabs;
        inlineCatalogFilterScroll=new ScrollViewer{Content=tabs,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,CanContentScroll=false};
        inlineCatalogFilterScroll.ScrollChanged+=(_,_)=>UpdateCatalogFilterOverflow();
        inlineCatalogFilterScroll.SizeChanged+=(_,_)=>UpdateFilterRail();
        inlineCatalogFilterScroll.PreviewMouseWheel+=(_,args)=>
        {
            if(inlineCatalogFilterScroll is not {} scroll||scroll.ScrollableWidth<=0)return;
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-args.Delta/120d*96);args.Handled=true;
        };
        catalogFilterBack=ActionButton("","IconBack",()=>ScrollCatalogFilters(-1),"QuietButton");catalogFilterBack.ToolTip="Предыдущие фильтры";System.Windows.Automation.AutomationProperties.SetName(catalogFilterBack,"Фильтры: назад");catalogFilterBack.Padding=new(6);catalogFilterBack.Width=32;catalogFilterBack.MinHeight=40;catalogFilterBack.Margin=new(0,0,4,8);DockPanel.SetDock(catalogFilterBack,Dock.Left);toolbar.Children.Add(catalogFilterBack);
        DockPanel.SetDock(sortBar,Dock.Right);toolbar.Children.Add(sortBar);
        catalogFilterForward=ActionButton("","IconChevron",()=>ScrollCatalogFilters(1),"QuietButton");catalogFilterForward.ToolTip="Следующие фильтры";System.Windows.Automation.AutomationProperties.SetName(catalogFilterForward,"Фильтры: вперёд");catalogFilterForward.Padding=new(6);catalogFilterForward.Width=32;catalogFilterForward.MinHeight=40;catalogFilterForward.Margin=new(4,0,0,8);DockPanel.SetDock(catalogFilterForward,Dock.Right);toolbar.Children.Add(catalogFilterForward);
        toolbar.Children.Add(inlineCatalogFilterScroll);FilterControls.Children.Add(toolbar);
        RenderActiveCatalogFilters();
        UpdateFilterRail();
        if(PeopleOnlySearch)
        {
            CancelCatalogQualityCheck();
            var people=new StackPanel();RenderPeopleSearchResults(people,true);
            Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=people,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            return;
        }
        IEnumerable<MediaItem> shown=favoritesOnly?prefs.LiveFavorites.Where(x=>x.Section==section&&x.Title.Contains(submittedQuery,StringComparison.CurrentCultureIgnoreCase)).DistinctBy(x=>x.Id).Where(selection.Matches):SearchActive?UnifiedSearch.Filter(liveItems,searchCategory).Where(selection.Matches):liveItems;
        if(favoritesOnly)shown=catalogOrder=="По рейтингу"?shown.OrderByDescending(CatalogPaging.Rating):shown.OrderByDescending(x=>x.Year);
        var local=favoritesOnly||SearchActive;
        var all=shown.Where(x=>favoritesOnly||SearchActive||!NoDownloads(x)).ToArray();
        if(local&&!liveLoading){catalogLastPage=Math.Max(1,(all.Length+CatalogPaging.Size-1)/CatalogPaging.Size);livePage=Math.Min(livePage,catalogLastPage.Value);catalogHasNext=livePage<catalogLastPage;}
        var rawPage=local?all.Skip((livePage-1)*CatalogPaging.Size).Take(CatalogPaging.Size).ToArray():all;
        StartCatalogQualityCheck(rawPage);
        var cards=rawPage.Where(CatalogQualityMatches).ToArray();
        ShowCatalog(cards);
        var list=catalogList!;
        Body.Children.Remove(list);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);WheelScroll.SetIsEnabled(list,false);
        // The scroll viewer extends 6 px past the gutter so hover rings and focus rings are not clipped.
        var content=new StackPanel{Margin=new(6,6,6,0)};RenderPeopleSearchResults(content,false);
        // The main page keeps its filter row between the banner and the shelves. Filtered, searched and paged
        // lists keep the row pinned under the search so selected-filter chips never move the list below them.
        AddDiscovery(content,cards,DiscoveryCatalog?TakeFiltersPanel():null);
        content.Children.Add(list);
        if(cards.Length==0)
        {
            var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=370,Margin=new(24)};
            var label=Text(liveLoading?"Загружаем подборку…":CatalogQualityChecking?"Проверяем качество раздач…":liveError.Length>0?"Каталог пока недоступен":SearchActive&&peopleSearchItems.Length>0?"Фильмы и сериалы не найдены":"Ничего не найдено",23);label.FontWeight=FontWeights.SemiBold;label.TextAlignment=TextAlignment.Center;empty.Children.Add(label);
            var hint=Text(liveLoading?"Это займёт несколько секунд.":CatalogQualityChecking?"Фильмы появятся по мере проверки источников.":favoritesOnly?"Сохраняй фильмы и сериалы из карточки — они останутся под рукой.":"Попробуй другую страницу, запрос или сбрось фильтры.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);
            if(!liveLoading){var retry=ActionButton("Сбросить фильтры","IconRefresh",()=>ChangeCatalogFilter(()=>{favoritesOnly=false;ResetCatalogFilters();prefs.HidePoorQuality=false;prefs.CatalogQualityHeight=0;ResetCatalogQualityChecks();prefs.Save();}));retry.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(retry);}
            content.Children.Add(empty);
        }
        var footer=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(5,18,8,18)};
        var numbers=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
        void PageButton(string label,int page,bool enabled)
        {
            var button=Button(label,()=>GoCatalogPage(page));button.Style=(Style)FindResource("PillButton");button.IsEnabled=enabled&&!liveLoading;button.MinWidth=36;button.Padding=new(10,7,10,7);button.Margin=new(2);
            if(page==livePage&&int.TryParse(label,out _)){button.SetResourceReference(Control.BackgroundProperty,"Selected");button.FontWeight=FontWeights.SemiBold;}
            System.Windows.Automation.AutomationProperties.SetName(button,int.TryParse(label,out _)?"Страница "+page:label);numbers.Children.Add(button);
        }
        PageButton("Назад",livePage-1,livePage>1);
        if(CatalogPaging.Numbers(livePage,catalogLastPage)[0]>1){PageButton("1",1,true);numbers.Children.Add(Text("…",12,true));}
        foreach(var number in CatalogPaging.Numbers(livePage,catalogLastPage))PageButton(number.ToString(),number,true);
        PageButton("Далее",livePage+1,catalogHasNext&&livePage<CatalogPaging.Limit);
        footer.Children.Add(numbers);
        if(liveError.Length>0&&!favoritesOnly){var retry=Button("Повторить загрузку",()=>{onlineIndex.RetryNow();catalogPages.Clear();ResetDiscoveryData();catalogRefreshRequested=true;liveKey="";Render();});retry.Style=(Style)FindResource("QuietButton");footer.Children.Add(retry);}
        content.Children.Add(footer);
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,Margin=new(-6,-6,-6,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        UpdateFilterRail();
    }
    // The filter row lives in the page flow on the catalog (after the banner) and is returned to the header elsewhere.
    Border TakeFiltersPanel()
    {
        (FiltersPanel.Parent as Panel)?.Children.Remove(FiltersPanel);FiltersPanel.Margin=new(0,0,0,24);return FiltersPanel;
    }
    void ReturnFiltersPanel()
    {
        if(ReferenceEquals(FiltersPanel.Parent,HeaderArea))return;
        (FiltersPanel.Parent as Panel)?.Children.Remove(FiltersPanel);HeaderArea.Children.Add(FiltersPanel);FiltersPanel.Margin=new(0,12,0,0);
    }
    void AddCatalogFilters(Panel target,Panel? sortTarget=null)
    {
        void Choice(string label,IEnumerable<CatalogChoice> choices,string selected,Action<string> changed)
        {
            var items=choices.ToArray();
            var current=items.FirstOrDefault(x=>x.Key==selected)??items.First();
            var button=Button("",()=>{});button.Style=(Style)FindResource("PillButton");
            var full=label=="Порядок"?current.Label:selected==items[0].Key?label:current.Label;
            var compact=selected==items[0].Key?label switch{"Рейтинг от"=>"Рейтинг","Год выхода"=>"Год","Порядок"=>"Новые",_=>full}:label switch
            {
                "Рейтинг от"=>selected+"+",
                "Подборка"=>current.Label.Replace(" · Zona",""),
                "Порядок"=>current.Label switch{"Сначала новые"=>"Новые","По популярности"=>"Популярные","По рейтингу"=>"Рейтинг",_=>current.Label},
                _=>full
            };
            var sorting=label=="Порядок";
            button.Content=ChipContent(full,!sorting,sorting?"IconFilter":null);
            catalogFilterCaptions[button]=new(full,compact);
            StyleChip(button,!sorting&&selected!=items[0].Key);
            button.ToolTip=label+": "+current.Label;System.Windows.Automation.AutomationProperties.SetName(button,label);
            var menu=new ToggleContextMenu{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,MaxHeight=360};
            menu.SetResourceReference(Control.BackgroundProperty,"Raised");menu.SetResourceReference(Control.ForegroundProperty,"Text");
            foreach(var item in items)
            {
                var option=new MenuItem{Header=item.Label,Tag=item.Key,IsCheckable=true,IsChecked=item.Key==selected};
                option.Click+=(_,_)=>{menu.IsOpen=false;if(item.Key!=selected)ChangeCatalogFilter(()=>changed(item.Key));};menu.Items.Add(option);
            }
            AttachMenuToggle(button,menu);(sorting?sortTarget??target:target).Children.Add(button);
        }
        if(!favoritesOnly&&!SearchActive)Choice("Подборка",[new("all","Весь каталог"),new("popular","Популярное · Zona"),new("rated","Высокий рейтинг · Zona"),new("foreign","Иностранные"),new("native","Отечественные")],catalogRegion.Length>0?catalogRegion:catalogCollection,v=>
        {
            catalogRegion=v is "foreign" or "native"?v:"";catalogCollection=catalogRegion.Length>0?"all":v;
            if(catalogRegion.Length>0)catalogCountry="";
        });
        Choice("Жанр",new[]{new CatalogChoice("","Любой жанр")}.Concat(catalogGenres),catalogGenre,v=>catalogGenre=v);
        Choice("Страна",new[]{new CatalogChoice("","Любая страна")}.Concat(catalogCountries),catalogCountry,v=>{catalogCountry=v;catalogRegion="";});
        Choice("Рейтинг от",new[]{new CatalogChoice("0","Любой рейтинг")}.Concat(Enumerable.Range(1,9).Reverse().Select(x=>new CatalogChoice(x.ToString(),x+" и выше"))),catalogRating.ToString(),v=>catalogRating=int.Parse(v));
        Choice("Год выхода",new[]{new CatalogChoice("","Любой год")}.Concat(Enumerable.Range(2010,DateTime.UtcNow.Year-2009).Reverse().Select(x=>new CatalogChoice(x.ToString(),x.ToString()))),catalogYear?.ToString()??"",v=>catalogYear=int.TryParse(v,out var y)?y:null);
        var effectiveOrder=catalogCollection=="popular"?"По популярности":catalogCollection=="rated"?"По рейтингу":catalogOrder;
        Choice("Порядок",favoritesOnly?[new("Сначала новые","Сначала новые"),new("По рейтингу","По рейтингу")]:[new("Сначала новые","Сначала новые"),new("По популярности","По популярности"),new("По рейтингу","По рейтингу")],effectiveOrder,v=>{catalogOrder=v;catalogCollection="all";});
    }
    void RenderCatalogKeepingPosition()
    {
        // Render retains the visible title through CatalogNavigation. Replaying
        // the old pixel offset would undo its anchor when a row is inserted.
        Render();
    }
    async Task FetchCatalog(string key,string category,string query,int page)
    {
        liveRequest?.Cancel();liveRequest?.Dispose();liveRequest=new();var token=liveRequest.Token;liveLoading=true;liveError="";
        var request=liveRequest;var selection=CatalogSelection;var forceRefresh=catalogRefreshRequested;catalogRefreshRequested=false;
        bool IsCurrent()=>ReferenceEquals(liveRequest,request)&&!token.IsCancellationRequested&&liveKey==key&&submittedQuery==query;
        liveItems=[];catalogHasNext=false;
        var cacheKey=category+"|"+selection.Filter+"|"+page+(selection.Region.Length>0?"|region:"+selection.Region+"|home:"+CatalogRegions.HomeCountry(selection.HomeCountry):"");
        await Task.Yield();
        try
        {
            IReadOnlyList<MediaItem> items;
            if(string.IsNullOrWhiteSpace(query))
            {
                CatalogPage result;
                if(catalogPages.TryGetValue(cacheKey,out var cached))result=cached;
                else
                {
                    try
                    {
                        var shared=selection.IsDefault?await sharedCatalog.PageAsync(category,page,token,forceRefresh):null;
                        result=shared??(selection.Region.Length>0
                            ?await new RegionalCatalog(sourceClient).BrowsePage(category,page,selection.Region,selection.HomeCountry,token,selection,forceRefresh)
                            :await new LiveCatalog(sourceClient).BrowsePage(category,page,selection,token,forceRefresh));
                    }
                    catch(Exception) when(!token.IsCancellationRequested&&selection.IsDefault)
                    {
                        try
                        {
                            result=await onlineIndex.BrowsePage(category,page,token);
                            if(result.Items.Length==0)result=BundledCatalog.Page(category,page);
                        }
                        catch(Exception) when(!token.IsCancellationRequested){result=BundledCatalog.Page(category,page);}
                        liveError="Сохранённый каталог · источник временно недоступен";
                    }
                    if(catalogPages.Count>=80)catalogPages.Clear();catalogPages[cacheKey]=result;
                }
                if(!IsCurrent())return;
                items=result.Items;catalogHasNext=result.HasNext;
                if(!result.HasNext)catalogLastPage=page;
                if(result.Genres.Length>0)catalogGenres=result.Genres;if(result.Countries.Length>0)catalogCountries=result.Countries;
            }
            else
            {
                var result=await UnifiedSearch.FindAsync(query,
                    (kind,t)=>searchProvider!=null?searchProvider(kind,query,t):CatalogBatches.ReadPage(c=>onlineIndex.Browse(kind,query,1,c),c=>new LiveCatalog(sourceClient).Browse(kind,query,1,c),false,t),
                    kind=>catalogIndex.Search(kind,query).Concat(sharedCatalog.Search(kind,query)).Concat(BundledCatalog.Search(kind,query)).ToArray(),token);
                items=result.Items;if(result.Offline)liveError="Часть источников недоступна · добавлены результаты из сохранённого каталога";
            }
            try{await catalogIndex.AddAsync(items,token);}catch(IOException){}catch(UnauthorizedAccessException){}
            if(IsCurrent()){liveItems=items;if(items.Count==0)liveError="Ничего не найдено. Измени запрос или фильтры.";else if(!SearchActive&&prefs.CatalogQualityHeight==0)_ = CheckCatalogAvailability(items,key,category,token,forceRefresh);}
        }
        catch(OperationCanceledException){}
        catch(Exception){if(IsCurrent())liveError="Источник временно недоступен. Повтори загрузку или выбери другую страницу.";}
        finally{if(IsCurrent()){liveLoading=false;if(!closed&&current==null&&activePerson==null&&section==category&&!favoritesOnly)Render();}}
    }
}
