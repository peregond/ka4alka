using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    Button? topAll,topSaved;
    WrapPanel? inlineCatalogFilters;
    ScrollViewer? inlineCatalogFilterScroll;
    Grid? detailHero;
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
    Task<MediaItem> Metadata(MediaItem item,bool priority=false)
    {
        if(cardMetadata.TryGetValue(item.Id,out var task)&&(!priority||task.IsCompletedSuccessfully&&!string.IsNullOrWhiteSpace(task.Result.OriginalTitle)||priorityMetadata.Contains(item.Id)))return task;
        async Task<MediaItem> Load()
        {
            if(priority)priorityMetadata.Add(item.Id);
            try
            {
                return await metadataScheduler.Run(priority,async ()=>
                {
                    MediaItem indexed;
                    try{indexed=await onlineIndex.Detail(item,CancellationToken.None);}
                    catch{return await new LiveCatalog(sourceClient).Detail(item,CancellationToken.None);}
                    if(!string.IsNullOrWhiteSpace(indexed.Description)&&indexed.GenreKeys.Length>0&&indexed.CountryKeys.Length>0&&(!priority||!string.IsNullOrWhiteSpace(indexed.OriginalTitle)))return indexed;
                    try
                    {
                        var direct=await new LiveCatalog(sourceClient).Detail(indexed,CancellationToken.None);
                        return direct with
                        {
                            Description=string.IsNullOrWhiteSpace(direct.Description)?"Описание временно недоступно.":direct.Description,
                            Kinopoisk=direct.Kinopoisk=="—"?indexed.Kinopoisk:direct.Kinopoisk,
                            Imdb=direct.Imdb=="—"?indexed.Imdb:direct.Imdb,
                            OriginalTitle=direct.OriginalTitle??indexed.OriginalTitle,
                            Genre=direct.Genre,Country=direct.Country,GenreKeys=direct.GenreKeys,CountryKeys=direct.CountryKeys
                        };
                    }
                    catch{return indexed with{Description="Описание временно недоступно."};}
                });
            }
            finally{if(priority)priorityMetadata.Remove(item.Id);}
        }
        task=Load();if(cardMetadata.Count>=100)cardMetadata.Remove(cardMetadata.Keys.First());cardMetadata[item.Id]=task;return task;
    }
    async Task UpdateCardRatings(MediaItem item)
    {
        var task=Metadata(item);
        try{var data=await task;if(!closed)item.SetScores(data.Kinopoisk,data.Imdb);}
        catch{if(cardMetadata.TryGetValue(item.Id,out var latest)&&ReferenceEquals(task,latest))cardMetadata.Remove(item.Id); /* A missing rating stays unavailable. */ }
    }
    string catalogGenre="",catalogCountry="",catalogCollection="all";
    int catalogRating;
    bool catalogHasNext=true,catalogRefreshRequested;
    int? catalogLastPage;
    CatalogChoice[] catalogGenres=CatalogChoices.Genres,catalogCountries=CatalogChoices.Countries;
    readonly Dictionary<string,CatalogPage> catalogPages=[];
    string CurrentCatalogKey=>SearchActive?"Поиск|"+submittedQuery:section+"|"+livePage+"|"+CatalogSelection.Filter+"|"+CatalogSelection.Collection;
    CatalogSelection CatalogSelection=>new(catalogGenre,catalogCountry,catalogYear,catalogRating,catalogOrder switch{"По рейтингу"=>"rating","По популярности"=>"popular",_=>"date"},catalogCollection);
    void ResetCatalogFilters(){catalogYear=null;catalogGenre="";catalogCountry="";catalogRating=0;catalogOrder="Сначала новые";catalogCollection="all";catalogLastPage=null;}
    void ChangeCatalogFilter(Action change)
    {
        if(!SearchActive&&Search.Text.Length>0){Search.Text="";searchDelay.Stop();}
        change();livePage=1;catalogLastPage=null;if(!SearchActive)liveKey="";Render();
    }
    void GoCatalogPage(int page)
    {
        if(liveLoading||page==livePage)return;
        livePage=Math.Clamp(page,1,catalogLastPage??CatalogPaging.Limit);Render();UpdateLayout();
        FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToTop();
    }
    void RenderLiveCatalog()
    {
        var selection=CatalogSelection;
        var key=CurrentCatalogKey;
        if(!favoritesOnly&&liveKey!=key){liveKey=key;_ = FetchCatalog(key,section,submittedQuery,livePage);}
        var headingRow=new DockPanel{Margin=new(5,0,8,5)};
        var count=Text(liveLoading?"Загружаем…":$"Страница {livePage}",11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(14,0,0,0);DockPanel.SetDock(count,Dock.Right);headingRow.Children.Add(count);
        var title=favoritesOnly?"Сохранённое":SearchActive?"Результаты поиска":catalogCollection=="popular"?"Популярное":catalogCollection=="rated"?"Кино с высоким рейтингом":section=="Фильмы"?"Все фильмы":"Все сериалы";
        var heading=Text(title,compactHeight?25:32);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0);headingRow.Children.Add(heading);PageHeader.Children.Add(headingRow);
        var subtitle=Text(liveError.Length>0?liveError:favoritesOnly?"Кино, к которому хочется вернуться.":SearchActive?$"Результаты для «{submittedQuery}».":catalogCollection!="all"?"Подборка Zona · обновляется из общего каталога.":"Выбирай историю на сегодня.",13,true);subtitle.Tag="CatalogSubtitle";subtitle.Margin=new(5,3,0,18);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
        var toolbar=new DockPanel{Margin=new(5,compactHeight?6:0,8,12)};
        if(!favoritesOnly)
        {
            var refresh=ActionButton("","IconRefresh",()=>{onlineIndex.RetryNow();catalogPages.Clear();catalogRefreshRequested=true;liveKey="";Render();});refresh.ToolTip="Обновить каталог";System.Windows.Automation.AutomationProperties.SetName(refresh,"Обновить каталог");refresh.Padding=new(10);refresh.Margin=new(0);DockPanel.SetDock(refresh,Dock.Right);toolbar.Children.Add(refresh);
        }
        var tabs=new WrapPanel();
        if(SearchActive)SearchTabs(tabs);
        else {
        topAll=Button("Все",()=>{favoritesOnly=false;catalogLastPage=null;livePage=1;Render();});topAll.Style=(Style)FindResource("PillButton");topAll.SetResourceReference(Control.BackgroundProperty,favoritesOnly?"Panel":"Selected");tabs.Children.Add(topAll);
        topSaved=ActionButton("Сохранённое",favoritesOnly?"IconHeartFilled":"IconHeart",()=>{liveRequest?.Cancel();liveLoading=false;liveError="";liveKey="";favoritesOnly=true;catalogCollection="all";if(catalogOrder=="По популярности")catalogOrder="Сначала новые";catalogLastPage=null;livePage=1;Render();},"PillButton");topSaved.SetResourceReference(Control.BackgroundProperty,favoritesOnly?"Selected":"Panel");tabs.Children.Add(topSaved);
        }
        AddCatalogFilters(tabs);
        inlineCatalogFilters=tabs;
        inlineCatalogFilterScroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=tabs,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        toolbar.Children.Add(inlineCatalogFilterScroll);PageHeader.Children.Add(toolbar);
        UpdateFilterRail();
        IEnumerable<MediaItem> shown=favoritesOnly?prefs.LiveFavorites.Where(x=>x.Section==section&&x.Title.Contains(submittedQuery,StringComparison.CurrentCultureIgnoreCase)).DistinctBy(x=>x.Id).Where(selection.Matches):SearchActive?UnifiedSearch.Filter(liveItems,searchCategory).Where(selection.Matches):liveItems;
        if(favoritesOnly)shown=catalogOrder=="По рейтингу"?shown.OrderByDescending(CatalogPaging.Rating):shown.OrderByDescending(x=>x.Year);
        var local=favoritesOnly||SearchActive;
        var all=shown.ToArray();
        if(local){catalogLastPage=Math.Max(1,(all.Length+CatalogPaging.Size-1)/CatalogPaging.Size);livePage=Math.Min(livePage,catalogLastPage.Value);catalogHasNext=livePage<catalogLastPage;}
        var cards=local?all.Skip((livePage-1)*CatalogPaging.Size).Take(CatalogPaging.Size).ToArray():all;
        ShowCatalog(cards);
        var list=catalogList!;
        Body.Children.Remove(list);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);WheelScroll.SetIsEnabled(list,false);
        var content=new StackPanel();content.Children.Add(list);
        if(cards.Length==0)
        {
            var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=370,Margin=new(24)};
            var label=Text(liveLoading?"Загружаем подборку…":liveError.Length>0?"Каталог пока недоступен":"Ничего не найдено",23);label.FontWeight=FontWeights.SemiBold;label.TextAlignment=TextAlignment.Center;empty.Children.Add(label);
            var hint=Text(liveLoading?"Это займёт несколько секунд.":favoritesOnly?"Сохраняй фильмы и сериалы из карточки — они останутся под рукой.":"Попробуй другую страницу, запрос или сбрось фильтры.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);
            if(!liveLoading){var retry=ActionButton("Сбросить фильтры","IconRefresh",()=>ChangeCatalogFilter(()=>{favoritesOnly=false;ResetCatalogFilters();}));retry.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(retry);}
            content.Children.Add(empty);
        }
        var footer=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(5,18,8,18)};
        var numbers=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
        void PageButton(string label,int page,bool enabled)
        {
            var button=Button(label,()=>GoCatalogPage(page));button.Style=(Style)FindResource("PillButton");button.IsEnabled=enabled&&!liveLoading;button.MinWidth=36;button.Padding=new(10,7,10,7);button.Margin=new(2);
            if(page==livePage&&int.TryParse(label,out _)){button.SetResourceReference(Control.BackgroundProperty,"Selected");button.FontWeight=FontWeights.Bold;}
            System.Windows.Automation.AutomationProperties.SetName(button,int.TryParse(label,out _)?"Страница "+page:label);numbers.Children.Add(button);
        }
        PageButton("Назад",livePage-1,livePage>1);
        if(CatalogPaging.Numbers(livePage,catalogLastPage)[0]>1){PageButton("1",1,true);numbers.Children.Add(Text("…",12,true));}
        foreach(var number in CatalogPaging.Numbers(livePage,catalogLastPage))PageButton(number.ToString(),number,true);
        PageButton("Далее",livePage+1,catalogHasNext&&livePage<CatalogPaging.Limit);
        footer.Children.Add(numbers);
        if(liveError.Length>0&&!favoritesOnly){var retry=Button("Повторить загрузку",()=>{onlineIndex.RetryNow();catalogPages.Clear();catalogRefreshRequested=true;liveKey="";Render();});retry.Style=(Style)FindResource("QuietButton");footer.Children.Add(retry);}
        content.Children.Add(footer);
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
    }
    void AddCatalogFilters(Panel target)
    {
        void Choice(string label,IEnumerable<CatalogChoice> choices,string selected,Action<string> changed)
        {
            var items=choices.ToArray();
            var current=items.FirstOrDefault(x=>x.Key==selected)??items.First();
            var button=Button((label=="Порядок"?current.Label:selected==items[0].Key?label:current.Label)+" ▾",()=>{});button.Style=(Style)FindResource("PillButton");
            button.SetResourceReference(Control.BackgroundProperty,selected==items[0].Key?"Panel":"Selected");
            button.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(button,label);
            var menu=new ContextMenu{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,MaxHeight=360};
            menu.SetResourceReference(Control.BackgroundProperty,"Panel");menu.SetResourceReference(Control.ForegroundProperty,"Text");
            foreach(var item in items)
            {
                var option=new MenuItem{Header=item.Label,Tag=item.Key,IsCheckable=true,IsChecked=item.Key==selected};
                option.Click+=(_,_)=>{menu.IsOpen=false;if(item.Key!=selected)ChangeCatalogFilter(()=>changed(item.Key));};menu.Items.Add(option);
            }
            button.ContextMenu=menu;button.Click+=(_,_)=>menu.IsOpen=true;target.Children.Add(button);
        }
        if(!favoritesOnly&&!SearchActive)Choice("Подборка",[new("all","Весь каталог"),new("popular","Популярное · Zona"),new("rated","Высокий рейтинг · Zona")],catalogCollection,v=>catalogCollection=v);
        Choice("Жанр",new[]{new CatalogChoice("","Любой жанр")}.Concat(catalogGenres),catalogGenre,v=>catalogGenre=v);
        Choice("Страна",new[]{new CatalogChoice("","Любая страна")}.Concat(catalogCountries),catalogCountry,v=>catalogCountry=v);
        Choice("Рейтинг от",new[]{new CatalogChoice("0","Любой рейтинг")}.Concat(Enumerable.Range(1,9).Reverse().Select(x=>new CatalogChoice(x.ToString(),x+" и выше"))),catalogRating.ToString(),v=>catalogRating=int.Parse(v));
        Choice("Год выхода",new[]{new CatalogChoice("","Любой год")}.Concat(Enumerable.Range(2010,DateTime.UtcNow.Year-2009).Reverse().Select(x=>new CatalogChoice(x.ToString(),x.ToString()))),catalogYear?.ToString()??"",v=>catalogYear=int.TryParse(v,out var y)?y:null);
        var effectiveOrder=catalogCollection=="popular"?"По популярности":catalogCollection=="rated"?"По рейтингу":catalogOrder;
        Choice("Порядок",favoritesOnly?[new("Сначала новые","Сначала новые"),new("По рейтингу","По рейтингу")]:[new("Сначала новые","Сначала новые"),new("По популярности","По популярности"),new("По рейтингу","По рейтингу")],effectiveOrder,v=>{catalogOrder=v;catalogCollection="all";});
        if(!CatalogSelection.IsDefault){var reset=Button("Сбросить",()=>ChangeCatalogFilter(ResetCatalogFilters));reset.Style=(Style)FindResource("QuietButton");reset.HorizontalAlignment=HorizontalAlignment.Left;reset.Margin=new(0);target.Children.Add(reset);}
    }
    void RenderCatalogKeepingPosition()
    {
        var offset=FindVisual<ScrollViewer>(Body,_=>true)?.VerticalOffset??0;
        Render();UpdateLayout();FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToVerticalOffset(offset);
    }
    async Task FetchCatalog(string key,string category,string query,int page)
    {
        liveRequest?.Cancel();liveRequest?.Dispose();liveRequest=new();var token=liveRequest.Token;liveLoading=true;liveError="";
        var request=liveRequest;var selection=CatalogSelection;var forceRefresh=catalogRefreshRequested;catalogRefreshRequested=false;
        bool IsCurrent()=>ReferenceEquals(liveRequest,request)&&!token.IsCancellationRequested&&liveKey==key&&submittedQuery==query;
        liveItems=[];catalogHasNext=false;
        var cacheKey=category+"|"+selection.Filter+"|"+page;
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
                    try{result=await new LiveCatalog(sourceClient).BrowsePage(category,page,selection,token,forceRefresh);}
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
                    kind=>catalogIndex.Search(kind,query).Concat(BundledCatalog.Search(kind,query)).ToArray(),token);
                items=result.Items;if(result.Offline)liveError="Часть источников недоступна · добавлены результаты из сохранённого каталога";
            }
            try{await catalogIndex.AddAsync(items,token);}catch(IOException){}catch(UnauthorizedAccessException){}
            if(IsCurrent()){liveItems=items;if(items.Count==0)liveError="Ничего не найдено. Измени запрос или фильтры.";}
        }
        catch(OperationCanceledException){}
        catch(Exception){if(IsCurrent())liveError="Источник временно недоступен. Повтори загрузку или выбери другую страницу.";}
        finally{if(IsCurrent()){liveLoading=false;if(!closed&&current==null&&section==category&&!favoritesOnly)Render();}}
    }
    void RenderLiveDetail(MediaItem item)
    {
        if(requestedDetails.Add(item.Id))
        {
            var cached=catalogIndex.CachedReleaseSnapshot(item);
            if(cached is {Items.Length:>0}&&!liveReleases.ContainsKey(item.Id))
            {
                liveReleases[item.Id]=cached.Items;cachedReleaseViews.Add(item.Id);
                releaseViews[item.Id]=new(){Saved=true,SavedUtc=cached.SavedUtc,Sources=cached.Sources??[]};
            }
            _ = FetchDetails(item);
        }
        if(descriptionItemId!=item.Id){descriptionItemId=item.Id;descriptionExpanded=false;}
        var back=ActionButton(section,"IconBack",()=>{current=null;Render();});back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new(0,0,0,12);PageHeader.Children.Add(back);
        var panel=new StackPanel{Margin=new(0,0,10,0)};Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        detailHero=new Grid();detailHero.ColumnDefinitions.Add(new(){Width=new GridLength(174)});detailHero.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});detailHero.RowDefinitions.Add(new(){Height=GridLength.Auto});detailHero.RowDefinitions.Add(new(){Height=GridLength.Auto});
        detailPoster=new Border{Width=150,Height=225,CornerRadius=new(14),ClipToBounds=true,Background=item.Cover,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,0,24,0)};
        detailPoster.SizeChanged+=(sender,_)=>ClipPoster((Border)sender);
        var posterGrid=new Grid();posterGrid.Children.Add(new TextBlock{Text="Постер\nнедоступен",Foreground=Brushes.White,Opacity=.75,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=11});
        var image=new Image{DataContext=item,Stretch=Stretch.Uniform};image.Loaded+=SourceCover;posterGrid.Children.Add(image);detailPoster.Child=posterGrid;Grid.SetRowSpan(detailPoster,2);detailHero.Children.Add(detailPoster);
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(info,1);detailHero.Children.Add(info);
        var meta=Text(string.Join("  ·  ",new[]{item.Section,item.Year>0?item.Year.ToString():null,item.Genre,item.Country}.Where(x=>!string.IsNullOrWhiteSpace(x))),12,true);meta.Margin=new(0,0,0,9);info.Children.Add(meta);
        detailTitle=Text(item.Title,32);detailTitle.Name="DetailTitle";detailTitle.FontWeight=FontWeights.SemiBold;detailTitle.Margin=new(0,0,0,14);info.Children.Add(detailTitle);
        if(!string.IsNullOrWhiteSpace(item.OriginalTitle)&&!item.OriginalTitle.Equals(item.Title,StringComparison.OrdinalIgnoreCase)){var original=Text(item.OriginalTitle,13,true);original.Margin=new(0,0,0,12);info.Children.Add(original);}
        var scores=new WrapPanel{Margin=new(0,0,0,10)};
        Border Rating(string name,string value){var label=new TextBlock{Text=name+"  ",Foreground=(Brush)FindResource("Muted"),FontSize=11,VerticalAlignment=VerticalAlignment.Center};var rating=new TextBlock{Text=value,FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("Text")};var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(label);row.Children.Add(rating);return new Border{Child=row,Padding=new(10,6,10,6),Background=(Brush)FindResource("Selected"),CornerRadius=new(8),Margin=new(0,0,8,7)};}
        scores.Children.Add(Rating("Кинопоиск",item.Kinopoisk));scores.Children.Add(Rating("IMDb",item.Imdb));info.Children.Add(scores);
        var favorite=ActionButton(prefs.Favorites.Contains(item.Id)?"Сохранено":"Сохранить",prefs.Favorites.Contains(item.Id)?"IconHeartFilled":"IconHeart",()=>{if(prefs.Favorites.Add(item.Id)){prefs.LiveFavorites.RemoveAll(x=>x.Id==item.Id);prefs.LiveFavorites.Add(item);}else{prefs.Favorites.Remove(item.Id);prefs.LiveFavorites.RemoveAll(x=>x.Id==item.Id);}prefs.Save();Render();},"PillButton");favorite.HorizontalAlignment=HorizontalAlignment.Left;favorite.Margin=new(0,0,0,14);info.Children.Add(favorite);
        detailDescription=new StackPanel();Grid.SetColumn(detailDescription,1);Grid.SetRow(detailDescription,1);detailHero.Children.Add(detailDescription);
        detailSynopsis=Text(item.Description??"Загружаем описание…",13,true);detailSynopsis.Name="DetailSynopsis";detailSynopsis.LineHeight=20;detailSynopsis.MaxHeight=descriptionExpanded?double.PositiveInfinity:60;detailSynopsis.TextTrimming=TextTrimming.CharacterEllipsis;detailSynopsis.Margin=new(0);detailDescription.Children.Add(detailSynopsis);
        descriptionToggle=Button(descriptionExpanded?"Свернуть описание":"Читать дальше",()=>{descriptionExpanded=!descriptionExpanded;Render();});descriptionToggle.Name="DescriptionToggle";descriptionToggle.Style=(Style)FindResource("QuietButton");descriptionToggle.HorizontalAlignment=HorizontalAlignment.Left;descriptionToggle.Padding=new(0,5,0,5);descriptionToggle.Margin=new(0);descriptionToggle.MinHeight=26;descriptionToggle.Visibility=Visibility.Collapsed;detailDescription.Children.Add(descriptionToggle);
        detailSynopsis.SizeChanged+=(sender,_)=>{if(ReferenceEquals(sender,detailSynopsis))UpdateDescriptionToggle();};
        var heroFrame=new Border{Child=detailHero,Background=(Brush)FindResource("Panel"),BorderBrush=(Brush)FindResource("Edge"),BorderThickness=new(1),CornerRadius=new(22),Padding=new(20),Margin=new(0,0,0,24)};panel.Children.Add(heroFrame);UpdateDetailLayout();
        var releasesTitle=Text("Варианты загрузки",23);releasesTitle.FontWeight=FontWeights.SemiBold;releasesTitle.Margin=new(0,0,0,13);panel.Children.Add(releasesTitle);
        RenderSourceStatus(panel,item);
        if(!liveReleases.TryGetValue(item.Id,out var releases)){panel.Children.Add(Text("Ищем доступные раздачи…",13,true));return;}
        if(releases.Count==0)
        {
            var checking=releaseViews.TryGetValue(item.Id,out var scan)&&scan.Checking;
            var empty=new StackPanel{Margin=new(22)};var noReleases=Text(checking?"Ищем доступные раздачи…":"Подходящих раздач пока нет",17);noReleases.FontWeight=FontWeights.SemiBold;empty.Children.Add(noReleases);empty.Children.Add(Text(checking?"Варианты появятся по мере ответа источников.":"Можно повторить поиск позже или выбрать другую историю.",13,true));
            if(!checking){var retry=ActionButton("Повторить поиск","IconRefresh",()=>RetryReleases(item));retry.HorizontalAlignment=HorizontalAlignment.Left;retry.Margin=new(0,6,0,0);empty.Children.Add(retry);}
            panel.Children.Add(new Border{Child=empty,Background=(Brush)FindResource("Panel"),CornerRadius=new(18)});return;
        }
        RenderReleasePicker(panel,releases);
    }
    void UpdateDetailLayout()
    {
        if(detailHero==null||detailPoster==null||detailTitle==null||detailDescription==null)return;
        var narrow=Body.ActualWidth>0&&Body.ActualWidth<600;
        var posterWidth=narrow?94d:150d;
        detailPoster.Width=posterWidth;detailPoster.Height=posterWidth*1.5;
        detailHero.ColumnDefinitions[0].Width=new GridLength(posterWidth+(narrow?18:24));
        Grid.SetRowSpan(detailPoster,narrow?1:2);detailPoster.Margin=new(0,0,narrow?18:24,0);
        Grid.SetColumn(detailDescription,narrow?0:1);Grid.SetColumnSpan(detailDescription,narrow?2:1);detailDescription.Margin=narrow?new(0,12,0,0):new(0);
        detailTitle.FontSize=narrow?23:32;
    }
    void UpdateDescriptionToggle()
    {
        if(detailSynopsis==null||descriptionToggle==null||detailSynopsis.ActualWidth<=0)return;
        var formatted=new FormattedText(detailSynopsis.Text,System.Globalization.CultureInfo.CurrentCulture,detailSynopsis.FlowDirection,new Typeface(detailSynopsis.FontFamily,detailSynopsis.FontStyle,detailSynopsis.FontWeight,detailSynopsis.FontStretch),detailSynopsis.FontSize,detailSynopsis.Foreground,VisualTreeHelper.GetDpi(detailSynopsis).PixelsPerDip){MaxTextWidth=detailSynopsis.ActualWidth,LineHeight=20};
        descriptionToggle.Visibility=descriptionExpanded||formatted.Height>60.5?Visibility.Visible:Visibility.Collapsed;
    }
}
