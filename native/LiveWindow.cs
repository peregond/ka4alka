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
    CatalogBatch browseBatch=CatalogBatch.Empty;
    string browseBatchKey="";
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
                if(!string.IsNullOrWhiteSpace(indexed.Description))return indexed;
                try
                {
                    var direct=await new LiveCatalog(sourceClient).Detail(indexed,CancellationToken.None);
                    return direct with
                    {
                        Description=string.IsNullOrWhiteSpace(direct.Description)?"Описание временно недоступно.":direct.Description,
                        Kinopoisk=direct.Kinopoisk=="—"?indexed.Kinopoisk:direct.Kinopoisk,
                        Imdb=direct.Imdb=="—"?indexed.Imdb:direct.Imdb,
                        OriginalTitle=direct.OriginalTitle??indexed.OriginalTitle
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
    void RenderLiveCatalog()
    {
        var key=section+"|"+Search.Text+"|"+livePage;
        if(!favoritesOnly&&liveKey!=key){liveKey=key;_ = FetchCatalog(key,section,Search.Text,livePage);}
        var headingRow=new DockPanel{Margin=new(5,0,8,5)};
        var count=Text(liveLoading?"Обновляем…":$"{(favoritesOnly?prefs.LiveFavorites.Count(x=>x.Section==section):liveItems.Count)} в подборке",11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(14,0,0,0);DockPanel.SetDock(count,Dock.Right);headingRow.Children.Add(count);
        var heading=Text(favoritesOnly?"Сохранённое":Search.Text.Length>0?(section=="Фильмы"?"Поиск фильмов":"Поиск сериалов"):section=="Фильмы"?"Новые фильмы":"Новые сериалы",compactHeight?25:32);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0);headingRow.Children.Add(heading);PageHeader.Children.Add(headingRow);
        var subtitle=Text(favoritesOnly?"Кино, к которому хочется вернуться.":liveError.Length>0?liveError:Search.Text.Length>0?$"Результаты для «{Search.Text}».":"Выбирай историю на сегодня.",13,true);subtitle.Tag="CatalogSubtitle";subtitle.Margin=new(5,3,0,18);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
        var toolbar=new DockPanel{Margin=new(5,compactHeight?6:0,8,12)};
        var paging=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};
        if(!favoritesOnly)
        {
            var refresh=ActionButton("","IconRefresh",()=>{onlineIndex.RetryNow();livePage=1;liveKey="";Render();});refresh.ToolTip="Обновить каталог";System.Windows.Automation.AutomationProperties.SetName(refresh,"Обновить каталог");refresh.Padding=new(10);refresh.Margin=new(0);paging.Children.Add(refresh);
        }
        DockPanel.SetDock(paging,Dock.Right);toolbar.Children.Add(paging);
        var tabs=new WrapPanel();
        topAll=Button("Все",()=>{favoritesOnly=false;Render();});topAll.Style=(Style)FindResource("PillButton");topAll.SetResourceReference(Control.BackgroundProperty,favoritesOnly?"Panel":"Selected");tabs.Children.Add(topAll);
        topSaved=ActionButton("Сохранённое","IconHeart",()=>{favoritesOnly=true;Render();},"PillButton");topSaved.SetResourceReference(Control.BackgroundProperty,favoritesOnly?"Selected":"Panel");tabs.Children.Add(topSaved);
        inlineFilterButton=ActionButton(catalogYear.HasValue?"Фильтры · 1":"Фильтры","IconFilter",()=>{inlineFiltersOpen=!inlineFiltersOpen;UpdateFilterRail();},"PillButton");tabs.Children.Add(inlineFilterButton);
        toolbar.Children.Add(tabs);PageHeader.Children.Add(toolbar);
        inlineCatalogFilters=new WrapPanel{Margin=new(5,0,0,10)};AddCatalogFilters(inlineCatalogFilters,true);PageHeader.Children.Add(inlineCatalogFilters);
        RenderCatalogFilterRail();UpdateFilterRail();
        IEnumerable<MediaItem> shown=favoritesOnly?prefs.LiveFavorites.Where(x=>x.Section==section&&x.Title.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).DistinctBy(x=>x.Id):liveItems;
        if(catalogYear.HasValue)shown=shown.Where(x=>x.Year==catalogYear.Value);
        if(catalogOrder=="По названию")shown=shown.OrderBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase);
        var cards=shown.ToArray();ShowCatalog(cards);
        if(!favoritesOnly&&string.IsNullOrWhiteSpace(Search.Text)&&cards.Length>0)
        {
            Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Body.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var footer=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,10,0,0)};Grid.SetRow(footer,1);Body.Children.Add(footer);
            if(browseBatch.HasMore)
            {
                var more=ActionButton(liveLoading?"Загружаем…":browseBatch.Failed?"Повторить загрузку":"Показать ещё · 100","IconPlus",()=>{onlineIndex.RetryNow();livePage++;RenderCatalogKeepingPosition();});
                more.IsEnabled=!liveLoading;more.Margin=new(0);more.ToolTip="Добавить к подборке до 100 новых карточек";System.Windows.Automation.AutomationProperties.SetName(more,"Показать ещё фильмы и сериалы");footer.Children.Add(more);
            }
            else footer.Children.Add(Text("Вся доступная подборка загружена",11,true));
        }
        if(cards.Length==0&&!liveLoading)
        {
            Body.Children.Clear();catalogList=null;
            var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=370,Margin=new(24)};
            var label=Text(favoritesOnly?"Здесь будет твоё кино":liveError.Length>0?"Каталог пока недоступен":"Ничего не найдено",23);label.FontWeight=FontWeights.SemiBold;label.TextAlignment=TextAlignment.Center;empty.Children.Add(label);
            var hint=Text(favoritesOnly?"Сохраняй фильмы и сериалы из карточки — они останутся под рукой.":catalogYear.HasValue?"Попробуй другой год или сбрось фильтры.":"Попробуй другой запрос или обнови каталог.",13,true);hint.TextAlignment=TextAlignment.Center;hint.Margin=new(0,4,0,18);empty.Children.Add(hint);
            var retry=ActionButton(catalogYear.HasValue?"Сбросить фильтры":"Обновить каталог","IconRefresh",()=>{catalogYear=null;onlineIndex.RetryNow();liveKey="";Render();});retry.HorizontalAlignment=HorizontalAlignment.Center;if(!favoritesOnly)empty.Children.Add(retry);
            Body.Children.Add(new Border{Child=empty,Background=(Brush)FindResource("Panel"),CornerRadius=new(20),Margin=new(5,0,8,8)});
        }
    }
    void AddCatalogFilters(Panel target,bool inline)
    {
        StackPanel Field(string label)
        {
            var field=new StackPanel{Width=inline?165:double.NaN,Margin=inline?new(0,0,10,0):new(0,0,0,10)};var caption=Text(label,11,true);caption.Margin=new(0,0,0,6);field.Children.Add(caption);target.Children.Add(field);return field;
        }
        var available=favoritesOnly?prefs.LiveFavorites.Where(x=>x.Section==section):liveItems;
        var years=new[]{"Любой год"}.Concat(available.Select(x=>x.Year).Append(catalogYear??0).Where(x=>x>0).Distinct().OrderDescending().Select(x=>x.ToString())).ToArray();
        var year=new ComboBox{ItemsSource=years,SelectedItem=catalogYear?.ToString()??"Любой год",HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0,Margin=new(0,0,0,6)};
        System.Windows.Automation.AutomationProperties.SetName(year,"Год выхода");
        year.SelectionChanged+=(_,_)=>{var next=int.TryParse(year.SelectedItem?.ToString(),out var number)?number:(int?)null;if(next==catalogYear)return;catalogYear=next;Render();};Field("Год выхода").Children.Add(year);
        var order=new ComboBox{ItemsSource=new[]{"Сначала новые","По названию"},SelectedItem=catalogOrder,HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0,Margin=new(0,0,0,6)};
        System.Windows.Automation.AutomationProperties.SetName(order,"Порядок фильмов");
        order.SelectionChanged+=(_,_)=>{var next=order.SelectedItem?.ToString()??"Сначала новые";if(next==catalogOrder)return;catalogOrder=next;Render();};Field("Порядок").Children.Add(order);
        if(catalogYear.HasValue||catalogOrder!="Сначала новые"){var reset=Button("Сбросить",()=>{catalogYear=null;catalogOrder="Сначала новые";Render();});reset.Style=(Style)FindResource("QuietButton");reset.HorizontalAlignment=HorizontalAlignment.Left;reset.Margin=inline?new(0,19,0,0):new(0);target.Children.Add(reset);}
    }
    void RenderCatalogFilterRail()
    {
        FilterControls.Children.Clear();
        var title=Text("Настроить подборку",16);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,23);FilterControls.Children.Add(title);
        AddCatalogFilters(FilterControls,false);
        FilterControls.Children.Add(new Border{Height=1,Margin=new(0,12,0,18),Background=(Brush)FindResource("Edge")});
        FilterControls.Children.Add(Text("Год и порядок применяются к загруженной подборке.",11,true));
        var tip=Text("Качество и озвучка —\nв карточке фильма.",12,true);tip.Margin=new(0,12,0,0);FilterControls.Children.Add(tip);
    }
    void RenderCatalogKeepingPosition()
    {
        var offset=FindVisual<ScrollViewer>(Body,_=>true)?.VerticalOffset??0;
        Render();UpdateLayout();FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToVerticalOffset(offset);
    }
    async Task FetchCatalog(string key,string category,string query,int page)
    {
        liveRequest?.Cancel();liveRequest?.Dispose();liveRequest=new();var token=liveRequest.Token;liveLoading=true;liveError="";
        var request=liveRequest;
        bool IsCurrent()=>ReferenceEquals(liveRequest,request)&&!token.IsCancellationRequested&&liveKey==key&&Search.Text==query;
        var batchKey=category+"|"+query;
        var append=string.IsNullOrWhiteSpace(query)&&page>1&&browseBatchKey==batchKey;
        var indexed=string.IsNullOrWhiteSpace(query)?catalogIndex.Recent(category,100):catalogIndex.Search(category,query);
        if(!append){liveItems=indexed;browseBatch=CatalogBatch.Empty;browseBatchKey=batchKey;}
        // Yield before progressive results can redraw the page that initiated this request.
        await Task.Yield();
        try
        {
            async Task<IReadOnlyList<MediaItem>> ReadPage(int sourcePage,CancellationToken ct)
            {
                return await CatalogBatches.ReadPage(t=>onlineIndex.Browse(category,query,sourcePage,t),
                    t=>new LiveCatalog(sourceClient).Browse(category,query,sourcePage,t),string.IsNullOrWhiteSpace(query),ct);
            }
            IReadOnlyList<MediaItem> items;
            if(string.IsNullOrWhiteSpace(query))
            {
                var settled=false;
                var progress=new Progress<CatalogBatch>(batch=>
                {
                    if(!IsCurrent()||settled)return;
                    browseBatch=batch;liveItems=CatalogBatches.Visible(batch,indexed,append,true);
                    if(!closed&&current==null&&section==category)RenderCatalogKeepingPosition();
                });
                var batch=await CatalogBatches.Load(append?browseBatch:CatalogBatch.Empty,ReadPage,progress,token);settled=true;
                if(!IsCurrent())return;
                browseBatch=batch;items=batch.Items;
                if(batch.Failed)liveError="Не удалось загрузить всю подборку. Уже найденные карточки сохранены; можно повторить загрузку.";
            }
            else items=await ReadPage(1,token);
            try{await catalogIndex.AddAsync(items,token);}catch(IOException){}catch(UnauthorizedAccessException){}
            if(IsCurrent())
            {
                liveItems=string.IsNullOrWhiteSpace(query)?CatalogBatches.Visible(browseBatch,indexed,append,false):items.Concat(indexed).DistinctBy(x=>x.Id).Take(80).ToArray();
                if(liveItems.Count==0&&liveError.Length==0)liveError="Ничего не найдено. Измени запрос или обнови каталог.";
                else if(items.Count==0)liveError="Показаны результаты из локального индекса.";
            }
        }
        catch(OperationCanceledException){if(IsCurrent())liveError="Каталог не ответил вовремя. Нажми «Обновить».";}
        catch(Exception){if(IsCurrent())liveError=indexed.Count>0?"Показаны результаты из локального индекса; источник временно недоступен.":"Каталог временно недоступен. Нажми «Обновить».";}
        finally{if(IsCurrent()){liveLoading=false;if(!closed&&current==null&&section==category){if(string.IsNullOrWhiteSpace(query))RenderCatalogKeepingPosition();else Render();}}}
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
        var meta=Text(string.Join("  ·  ",new[]{section,item.Year>0?item.Year.ToString():null,item.Genre}.Where(x=>!string.IsNullOrWhiteSpace(x))),12,true);meta.Margin=new(0,0,0,9);info.Children.Add(meta);
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
