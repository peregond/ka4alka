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
    Button? inlineFilterButton;
    bool inlineFiltersOpen;
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
    readonly SemaphoreSlim metadataSlots=new(2);
    readonly OnlineIndexClient onlineIndex;
    Task<MediaItem> Metadata(MediaItem item)
    {
        if(cardMetadata.TryGetValue(item.Id,out var task))return task;
        async Task<MediaItem> Load()
        {
            await metadataSlots.WaitAsync();
            try
            {
                MediaItem indexed;
                try{indexed=await onlineIndex.Detail(item,CancellationToken.None);}
                catch{return await new LiveCatalog(sourceClient).Detail(item,CancellationToken.None);}
                if(!string.IsNullOrWhiteSpace(indexed.Description)&&indexed.GenreKeys.Length>0&&indexed.CountryKeys.Length>0)return indexed;
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
            }
            finally{metadataSlots.Release();}
        }
        task=Load();if(cardMetadata.Count>=100)cardMetadata.Remove(cardMetadata.Keys.First());cardMetadata[item.Id]=task;return task;
    }
    async Task UpdateCardRatings(MediaItem item)
    {
        try{var data=await Metadata(item);if(!closed)item.SetScores(data.Kinopoisk,data.Imdb);}catch{cardMetadata.Remove(item.Id); /* A missing rating stays unavailable. */ }
    }
    string catalogGenre="",catalogCountry="",catalogCollection="all";
    int catalogRating;
    bool catalogHasNext=true;
    int? catalogLastPage;
    CatalogChoice[] catalogGenres=CatalogChoices.Genres,catalogCountries=CatalogChoices.Countries;
    readonly Dictionary<string,CatalogPage> catalogPages=[];
    string CurrentCatalogKey=>section+"|"+Search.Text+"|"+livePage+"|"+CatalogSelection.Filter+"|"+CatalogSelection.Collection;
    CatalogSelection CatalogSelection=>new(catalogGenre,catalogCountry,catalogYear,catalogRating,catalogOrder switch{"По рейтингу"=>"rating","По популярности"=>"popular",_=>"date"},catalogCollection);
    void ResetCatalogFilters(){catalogYear=null;catalogGenre="";catalogCountry="";catalogRating=0;catalogOrder="Сначала новые";catalogCollection="all";catalogLastPage=null;}
    void ChangeCatalogFilter(Action change)
    {
        if(Search.Text.Length>0){Search.Text="";searchDelay.Stop();}
        change();livePage=1;catalogLastPage=null;liveKey="";Render();
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
        if(!favoritesOnly&&liveKey!=key){liveKey=key;_ = FetchCatalog(key,section,Search.Text,livePage);}
        var headingRow=new DockPanel{Margin=new(5,0,8,5)};
        var count=Text(liveLoading?"Загружаем…":$"Страница {livePage}",11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(14,0,0,0);DockPanel.SetDock(count,Dock.Right);headingRow.Children.Add(count);
        var title=favoritesOnly?"Сохранённое":Search.Text.Length>0?(section=="Фильмы"?"Поиск фильмов":"Поиск сериалов"):catalogCollection=="popular"?"Популярное сегодня":catalogCollection=="rated"?"Кино с высоким рейтингом":section=="Фильмы"?"Все фильмы":"Все сериалы";
        var heading=Text(title,compactHeight?25:32);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0);headingRow.Children.Add(heading);PageHeader.Children.Add(headingRow);
        var subtitle=Text(liveError.Length>0?liveError:favoritesOnly?"Кино, к которому хочется вернуться.":Search.Text.Length>0?$"Результаты для «{Search.Text}».":catalogCollection!="all"?"Подборка Zona · обновляется из общего каталога.":"Выбирай историю на сегодня.",13,true);subtitle.Tag="CatalogSubtitle";subtitle.Margin=new(5,3,0,18);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
        var toolbar=new DockPanel{Margin=new(5,compactHeight?6:0,8,12)};
        if(!favoritesOnly)
        {
            var refresh=ActionButton("","IconRefresh",()=>{onlineIndex.RetryNow();catalogPages.Clear();liveKey="";Render();});refresh.ToolTip="Обновить каталог";System.Windows.Automation.AutomationProperties.SetName(refresh,"Обновить каталог");refresh.Padding=new(10);refresh.Margin=new(0);DockPanel.SetDock(refresh,Dock.Right);toolbar.Children.Add(refresh);
        }
        var tabs=new WrapPanel();
        topAll=Button("Все",()=>{favoritesOnly=false;livePage=1;Render();});topAll.Style=(Style)FindResource("PillButton");topAll.SetResourceReference(Control.BackgroundProperty,favoritesOnly?"Panel":"Selected");tabs.Children.Add(topAll);
        topSaved=ActionButton("Сохранённое","IconHeart",()=>{liveRequest?.Cancel();liveLoading=false;liveKey="";favoritesOnly=true;catalogCollection="all";livePage=1;Render();},"PillButton");topSaved.SetResourceReference(Control.BackgroundProperty,favoritesOnly?"Selected":"Panel");tabs.Children.Add(topSaved);
        var filters=new[]{catalogGenre.Length>0,catalogCountry.Length>0,catalogYear.HasValue,catalogRating>0,catalogOrder!="Сначала новые",catalogCollection!="all"}.Count(x=>x);
        inlineFilterButton=ActionButton(filters>0?"Фильтры · "+filters:"Фильтры","IconFilter",()=>{inlineFiltersOpen=!inlineFiltersOpen;UpdateFilterRail();},"PillButton");tabs.Children.Add(inlineFilterButton);
        toolbar.Children.Add(tabs);PageHeader.Children.Add(toolbar);
        inlineCatalogFilters=new WrapPanel{Margin=new(5,0,0,10)};AddCatalogFilters(inlineCatalogFilters,true);PageHeader.Children.Add(inlineCatalogFilters);
        RenderCatalogFilterRail();UpdateFilterRail();
        IEnumerable<MediaItem> shown=favoritesOnly?prefs.LiveFavorites.Where(x=>x.Section==section&&x.Title.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).DistinctBy(x=>x.Id).Where(selection.Matches):liveItems;
        var local=favoritesOnly||Search.Text.Length>0;
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
            if(!liveLoading){var retry=ActionButton("Сбросить фильтры","IconRefresh",()=>ChangeCatalogFilter(ResetCatalogFilters));retry.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(retry);}
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
        if(liveError.Length>0&&!favoritesOnly){var retry=Button("Повторить загрузку",()=>{onlineIndex.RetryNow();catalogPages.Clear();liveKey="";Render();});retry.Style=(Style)FindResource("QuietButton");footer.Children.Add(retry);}
        content.Children.Add(footer);
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
    }
    void AddCatalogFilters(Panel target,bool inline)
    {
        void Choice(string label,IEnumerable<CatalogChoice> choices,string selected,Action<string> changed)
        {
            var field=new StackPanel{Width=inline?165:double.NaN,Margin=inline?new(0,0,10,0):new(0,0,0,10)};var caption=Text(label,11,true);caption.Margin=new(0,0,0,6);field.Children.Add(caption);target.Children.Add(field);
            var items=choices.ToArray();var combo=new ComboBox{ItemsSource=items,SelectedItem=items.FirstOrDefault(x=>x.Key==selected)??items.FirstOrDefault(),HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0,Margin=new(0,0,0,6)};
            System.Windows.Automation.AutomationProperties.SetName(combo,label);
            combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is CatalogChoice next&&next.Key!=selected)ChangeCatalogFilter(()=>changed(next.Key));};field.Children.Add(combo);
        }
        if(!favoritesOnly)Choice("Подборка",[new("all","Весь каталог"),new("popular","Популярное · Zona"),new("rated","Высокий рейтинг · Zona")],catalogCollection,v=>catalogCollection=v);
        Choice("Жанр",new[]{new CatalogChoice("","Любой жанр")}.Concat(catalogGenres),catalogGenre,v=>catalogGenre=v);
        Choice("Страна",new[]{new CatalogChoice("","Любая страна")}.Concat(catalogCountries),catalogCountry,v=>catalogCountry=v);
        Choice("Рейтинг от",new[]{new CatalogChoice("0","Любой рейтинг")}.Concat(Enumerable.Range(1,9).Reverse().Select(x=>new CatalogChoice(x.ToString(),x+" и выше"))),catalogRating.ToString(),v=>catalogRating=int.Parse(v));
        Choice("Год выхода",new[]{new CatalogChoice("","Любой год")}.Concat(Enumerable.Range(2010,DateTime.UtcNow.Year-2009).Reverse().Select(x=>new CatalogChoice(x.ToString(),x.ToString()))),catalogYear?.ToString()??"",v=>catalogYear=int.TryParse(v,out var y)?y:null);
        Choice("Порядок",[new("Сначала новые","Сначала новые"),new("По популярности","По популярности"),new("По рейтингу","По рейтингу")],catalogOrder,v=>{catalogOrder=v;catalogCollection="all";});
        if(!CatalogSelection.IsDefault){var reset=Button("Сбросить",()=>ChangeCatalogFilter(ResetCatalogFilters));reset.Style=(Style)FindResource("QuietButton");reset.HorizontalAlignment=HorizontalAlignment.Left;reset.Margin=inline?new(0,19,0,0):new(0);target.Children.Add(reset);}
    }
    void RenderCatalogFilterRail()
    {
        FilterControls.Children.Clear();var title=Text("Настроить подборку",16);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,23);FilterControls.Children.Add(title);AddCatalogFilters(FilterControls,false);
        FilterControls.Children.Add(new Border{Height=1,Margin=new(0,12,0,18),Background=(Brush)FindResource("Edge")});
        FilterControls.Children.Add(Text(favoritesOnly?"Фильтры по сохранённым данным карточек.":"Подборки и фильтры — из каталога Zona. При выборе фильтра поиск очищается.",11,true));
        var source=Button("Открыть источник",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LiveCatalog.Base+(section=="Сериалы"?"/tvseries":"/movies")){UseShellExecute=true}));source.Style=(Style)FindResource("QuietButton");FilterControls.Children.Add(source);
    }
    void RenderCatalogKeepingPosition()
    {
        var offset=FindVisual<ScrollViewer>(Body,_=>true)?.VerticalOffset??0;
        Render();UpdateLayout();FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToVerticalOffset(offset);
    }
    async Task FetchCatalog(string key,string category,string query,int page)
    {
        liveRequest?.Cancel();liveRequest?.Dispose();liveRequest=new();var token=liveRequest.Token;liveLoading=true;liveError="";
        var request=liveRequest;var selection=CatalogSelection;
        bool IsCurrent()=>ReferenceEquals(liveRequest,request)&&!token.IsCancellationRequested&&liveKey==key&&Search.Text==query;
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
                    try{result=await new LiveCatalog(sourceClient).BrowsePage(category,page,selection,token);}
                    catch(Exception) when(!token.IsCancellationRequested&&selection.IsDefault)
                    {
                        var indexed=await onlineIndex.Browse(category,"",page,token);
                        result=new(indexed.Take(CatalogPaging.Size).ToArray(),indexed.Count>=CatalogPaging.Size&&page<CatalogPaging.Limit,[],[]);
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
                var indexed=catalogIndex.Search(category,query);
                items=await CatalogBatches.ReadPage(t=>onlineIndex.Browse(category,query,1,t),t=>new LiveCatalog(sourceClient).Browse(category,query,1,t),false,token);
                items=items.Concat(indexed).DistinctBy(x=>x.Id).Take(80).ToArray();
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
        var meta=Text(string.Join("  ·  ",new[]{section,item.Year>0?item.Year.ToString():null,item.Genre,item.Country}.Where(x=>!string.IsNullOrWhiteSpace(x))),12,true);meta.Margin=new(0,0,0,9);info.Children.Add(meta);
        detailTitle=Text(item.Title,32);detailTitle.Name="DetailTitle";detailTitle.FontWeight=FontWeights.SemiBold;detailTitle.Margin=new(0,0,0,14);info.Children.Add(detailTitle);
        if(!string.IsNullOrWhiteSpace(item.OriginalTitle)&&!item.OriginalTitle.Equals(item.Title,StringComparison.OrdinalIgnoreCase)){var original=Text(item.OriginalTitle,13,true);original.Margin=new(0,0,0,12);info.Children.Add(original);}
        var scores=new WrapPanel{Margin=new(0,0,0,10)};
        Border Rating(string name,string value){var label=new TextBlock{Text=name+"  ",Foreground=(Brush)FindResource("Muted"),FontSize=11,VerticalAlignment=VerticalAlignment.Center};var rating=new TextBlock{Text=value,FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("Text")};var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(label);row.Children.Add(rating);return new Border{Child=row,Padding=new(10,6,10,6),Background=(Brush)FindResource("Selected"),CornerRadius=new(8),Margin=new(0,0,8,7)};}
        scores.Children.Add(Rating("Кинопоиск",item.Kinopoisk));scores.Children.Add(Rating("IMDb",item.Imdb));info.Children.Add(scores);
        var favorite=ActionButton(prefs.Favorites.Contains(item.Id)?"Сохранено":"Сохранить","IconHeart",()=>{if(prefs.Favorites.Add(item.Id)){prefs.LiveFavorites.RemoveAll(x=>x.Id==item.Id);prefs.LiveFavorites.Add(item);}else{prefs.Favorites.Remove(item.Id);prefs.LiveFavorites.RemoveAll(x=>x.Id==item.Id);}prefs.Save();Render();},"PillButton");favorite.HorizontalAlignment=HorizontalAlignment.Left;favorite.Margin=new(0,0,0,14);info.Children.Add(favorite);
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
