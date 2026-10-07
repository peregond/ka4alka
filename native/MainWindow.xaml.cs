using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Kachalka;
public partial class MainWindow:Window
{
    readonly Preferences prefs=Preferences.Load();
    readonly DownloadService downloads;
    readonly SharedCatalog sharedCatalog;
    readonly DispatcherTimer catalogRefreshTimer=new(){Interval=TimeSpan.FromMinutes(5)};
    readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromSeconds(2)};
    readonly DispatcherTimer searchDelay=new(){Interval=TimeSpan.FromMilliseconds(350)};
    string section="Фильмы",genre="Все",sort="По популярности";
    string lastCatalogSection="Фильмы";
    bool ready,closed;
    MediaItem? current;
    public MainWindow()
    {
        InitializeComponent();
        onlineIndex=new(sourceClient);sharedCatalog=new(sourceClient);
        catalogRefreshTimer.Tick+=(_,_)=>
        {
            if(closed)return;
            var expired=ExpireCatalogPages();
            if(current==null&&(section is "Фильмы" or "Сериалы")&&!SearchActive&&!favoritesOnly&&(expired||CatalogSelection.IsDefault&&sharedCatalog.RefreshDue))
            {
                catalogPages.Clear();catalogLastPage=null;liveKey="";Render();
            }
        };
        catalogRefreshTimer.Start();
        EnableAdaptiveLayout();
        EnableShortcuts();
        try{downloads=new(null,prefs.MaxDownloadKbps,prefs.MaxUploadKbps);}catch(Exception e){downloads=newEmpty(prefs.MaxDownloadKbps,prefs.MaxUploadKbps);Status.Text="Не удалось прочитать очередь: "+e.Message;}
        foreach(var name in new[]{"Фильмы","Сериалы"}) { var n=name;var b=ActionButton(n,n=="Фильмы"?"IconMovies":"IconSeries",()=>ShowCatalogSection(n),"NavButton");b.Tag=n;b.ToolTip=n;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new(0,0,0,5);Navigation.Children.Add(b); }
        refresh.Tick+=(_,_)=>{downloads.Update();RefreshDownloadView();SyncTimer();};
        ContentRendered+=async(_,_)=>{if(!prefs.AutoResumeDownloads||downloads.PendingResumeCount==0)return;Status.Text="Продолжаем загрузки…";var resumed=await downloads.ResumePendingAsync();Status.Text=resumed>0?$"Продолжено загрузок: {resumed}":"Не удалось продолжить загрузки. Проверь очередь.";if(!closed){SyncTimer();if(section=="Загрузки")Render();}};
        StateChanged+=(_,_)=>SyncTimer();searchDelay.Tick+=(_,_)=>{searchDelay.Stop();if(section is "Фильмы" or "Сериалы"){current=null;Render();}};
        Closing+=OnClosing;ready=true;ApplyTheme();ApplyCompactLayout();Render();
    }
    static DownloadService newEmpty(int down,int up){var path=Path.Combine(Preferences.DataDir,"queue.json");if(File.Exists(path))File.Move(path,path+".unreadable-"+DateTime.Now.Ticks);return new(null,down,up);}
    static TextBlock Text(string value,double size=13,bool muted=false){var t=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};t.SetResourceReference(TextBlock.ForegroundProperty,muted?"Muted":"Text");return t;}
    static Button Button(string label,Action action){var b=new Button{Content=label};b.Click+=(_,_)=>action();return b;}
    static Button AsyncButton(string label,Func<Task> action){var b=new Button{Content=label};b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(Exception e){MessageBox.Show(e.Message,"Качалка");}finally{b.IsEnabled=true;}};return b;}
    void SyncTimer(){if(!closed&&(downloads.EngineCreated||section=="Загрузки"&&WindowState!=WindowState.Minimized))refresh.Start();else refresh.Stop();}
    void SearchChanged(object sender,TextChangedEventArgs e){if(!ready)return;ClearSearchButton.Visibility=Search.Text.Length>0?Visibility.Visible:Visibility.Collapsed;searchDelay.Stop();}
    void ShowCatalogSection(string name)
    {
        var restore=SearchActive&&name==lastCatalogSection&&section is not ("Фильмы" or "Сериалы");
        searchDelay.Stop();section=name;lastCatalogSection=name;current=null;
        if(!restore){liveRequest?.Cancel();liveLoading=false;genre="Все";livePage=1;favoritesOnly=false;submittedQuery="";searchCategory="";ResetCatalogFilters();Search.Text="";liveKey="";}
        else Search.Text=submittedQuery;
        Render();
    }
    void ShowDownloads(object sender,RoutedEventArgs e){searchDelay.Stop();section="Загрузки";current=null;Render();}
    void PosterResized(object sender,SizeChangedEventArgs e){if(sender is Border poster){if(e.WidthChanged&&e.NewSize.Width>0){var height=e.NewSize.Width*1.5;if(double.IsNaN(poster.Height)||Math.Abs(poster.Height-height)>1)poster.Height=height;}ClipPoster(poster);}}
    void Render()
    {
        if(!ready)return;try{catalogList=null;detailHero=null;detailPoster=null;detailSynopsis=null;detailTitle=null;detailDescription=null;descriptionToggle=null;inlineCatalogFilters=null;inlineCatalogFilterScroll=null;PageHeader.Children.Clear();Body.Children.Clear();Body.RowDefinitions.Clear();SyncTimer();UpdateFilterRail();
        SearchBar.Visibility=current==null&&(section is "Фильмы" or "Сериалы")?Visibility.Visible:Visibility.Collapsed;
        ContextLabel.Visibility=SearchBar.Visibility==Visibility.Visible||section=="Загрузки"?Visibility.Collapsed:Visibility.Visible;ContextLabel.Text=section;
        downloadView=null;downloadList=null;
        foreach(Button b in Navigation.Children){var selected=!SearchActive&&b.Tag?.ToString()==section;b.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty,selected?"Selected":"Sidebar");b.SetResourceReference(Control.ForegroundProperty,selected?"Text":"Muted");b.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;}
        DownloadsButton.SetResourceReference(Control.BackgroundProperty,section=="Загрузки"?"Selected":"Sidebar");
        SettingsButton.SetResourceReference(Control.BackgroundProperty,section=="Настройки"?"Selected":"Sidebar");
        if(section=="Настройки"){RenderSettings();return;}
        if(section=="Источники"){RenderSources();return;}
        if(current?.IsLive==true){RenderLiveDetail(current);return;}
        if(current!=null){Detail(current);return;}
        if(!demoCatalog&&section is "Фильмы" or "Сериалы"){RenderLiveCatalog();return;}
        if(!demoCatalog&&section is "Музыка" or "Игры" or "Программы"){RenderArchive();return;}
        if(!demoCatalog&&section is "ТВ-каналы" or "Радио" or "Спорт"){RenderBroadcast();return;}
        if(section=="Загрузки"){RenderDownloads();return;}
        PageHeader.Children.Add(Text(section,25));
        PageHeader.Children.Add(Text("Демо-каталог · названия, раздачи и оценки Кинопоиска / IMDb вымышлены",11,true));
        var filters=new WrapPanel();if(section is "Фильмы" or "Сериалы")foreach(var g in new[]{"Все","Фантастика","Триллер","Драма","Приключения"}){var value=g;var b=Button(g,()=>{genre=value;Render();});b.Opacity=g==genre?1:.6;filters.Children.Add(b);}
        var order=new ComboBox{ItemsSource=new[]{"По популярности","По году","По названию"},SelectedItem=sort};order.SelectionChanged+=(_,_)=>{sort=order.SelectedItem.ToString()!;Render();};filters.Children.Add(order);PageHeader.Children.Add(filters);
        IEnumerable<MediaItem> result=Catalog.Items.Concat(prefs.LiveFavorites).Where(x=>section=="Избранное"?prefs.Favorites.Contains(x.Id):x.Section==section).Where(x=>x.Title.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).Where(x=>genre=="Все"||x.Genre==genre);
        result=sort=="По году"?result.OrderByDescending(x=>x.Year):sort=="По названию"?result.OrderBy(x=>x.Title):result;
        var cards=result.ToArray();ShowCatalog(cards);if(cards.Length==0)PageHeader.Children.Add(Text("Ничего не найдено. Измени поиск или фильтры.",14,true));
        }finally{RefreshLoadingIndicator();}
    }
    void OpenCard(object sender,RoutedEventArgs e){current=(MediaItem)((Button)sender).Tag;Render();}
    void Detail(MediaItem item)
    {
        var panel=new StackPanel();Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        PageHeader.Children.Add(Button("← "+section,()=>{current=null;Render();}));
        var intro=new Grid{Margin=new(0,8,0,20)};intro.ColumnDefinitions.Add(new(){Width=new GridLength(180)});intro.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var cover=new Border{Background=item.Cover,CornerRadius=new(18),Height=225,Margin=new(0,0,20,0)};intro.Children.Add(cover);
        var info=new StackPanel();Grid.SetColumn(info,1);intro.Children.Add(info);info.Children.Add(Text(item.Title,28));info.Children.Add(Text(item.Subtitle,12,true));
        if(item.Cinema){info.Children.Add(Text($"Кинопоиск  {item.Kinopoisk}     IMDb  {item.Imdb}",16));info.Children.Add(Text("Демонстрационные оценки",11,true));}
        info.Children.Add(Text("Здесь появятся описание и информация из подключённого каталога.",13,true));info.Children.Add(Button(prefs.Favorites.Contains(item.Id)?"♥ В избранном":"♡ В избранное",()=>{if(!prefs.Favorites.Add(item.Id))prefs.Favorites.Remove(item.Id);prefs.Save();Render();}));panel.Children.Add(intro);
        if(!item.Cinema){panel.Children.Add(Text("Источники для этого раздела пока не подключены.",14,true));return;}
        panel.Children.Add(Text("Выбрать раздачу",22));panel.Children.Add(Text("Примеры вариантов. Для реальной загрузки используй «Добавить торрент».",12,true));
        var filters=new WrapPanel();panel.Children.Add(filters);var results=new StackPanel();var selects=new Dictionary<string,ComboBox>();
        var definitions=new (string Key,string Name,string[] Values)[]{("Quality","Качество",["Все","2160p","1080p","720p"]),("Type","Тип",["Все","WEB-DL","BluRay"]),("Voice","Озвучка",["Все","Дубляж","Многоголосая","Оригинал"]),("Subs","Субтитры",["Все","RU + EN","RU","EN","Нет"]),("Format","Контейнер",["Все","MKV","MP4"]),("Codec","Кодек",["Все","H.264","H.265"]),("Hdr","Диапазон",["Все","SDR","HDR10","Dolby Vision"]),("Source","Источник",["Все","Open Collection","Media Archive","Community Library"]),("Sort","Сортировка",["Больше сидов","Меньше размер","Больше размер","Выше качество"])};
        foreach(var d in definitions){var field=new StackPanel();field.Children.Add(Text(d.Name,11,true));var combo=new ComboBox{ItemsSource=d.Values,SelectedIndex=0};field.Children.Add(combo);selects[d.Key]=combo;filters.Children.Add(field);}
        void Update(){results.Children.Clear();IEnumerable<Release> matches=Catalog.Releases;foreach(var d in definitions.Where(x=>x.Key!="Sort")){var v=selects[d.Key].SelectedItem?.ToString();if(v!="Все")matches=matches.Where(r=>typeof(Release).GetProperty(d.Key)!.GetValue(r)?.ToString()==v);}
            matches=selects["Sort"].SelectedIndex switch{1=>matches.OrderBy(r=>r.Size),2=>matches.OrderByDescending(r=>r.Size),3=>matches.OrderByDescending(r=>int.Parse(r.Quality.TrimEnd('p'))),_=>matches.OrderByDescending(r=>r.Seeds)};
            var list=matches.ToArray();results.Children.Add(Text($"Найдено {list.Length} из {Catalog.Releases.Length} · сортировка внутри типа",12,true));
            foreach(var group in list.GroupBy(r=>r.Type)){results.Children.Add(Text(group.Key,18));foreach(var r in group){var p=new StackPanel();p.Children.Add(Text($"{r.Quality} · {r.Format} · {r.Codec} · {r.Hdr}",15));p.Children.Add(Text($"{r.Voice} · субтитры: {r.Subs}\n{r.Source} · {r.Size} ГБ · {r.Seeds} сидов",12,true));results.Children.Add(new Border{Child=p,BorderBrush=(Brush)FindResource("Edge"),BorderThickness=new(0,0,0,1),Padding=new(0,12,0,8)});}}
            if(list.Length==0)results.Children.Add(Text("Нет вариантов с таким сочетанием. Сбрось фильтры.",14));}
        foreach(var c in selects.Values)c.SelectionChanged+=(_,_)=>Update();panel.Children.Add(Button("Сбросить фильтры",()=>{foreach(var c in selects.Values)c.SelectedIndex=0;}));panel.Children.Add(results);Update();
    }
    void AddTorrent(object sender,RoutedEventArgs e)
    {
        if(!EnsureDownloadFolder()){Status.Text="Папка для загрузок не выбрана. Её можно выбрать в настройках.";return;}
        var dialog=new Window{Title="Добавить торрент",Owner=this,Width=Math.Min(560,Math.Max(320,ActualWidth-32)),MaxHeight=Math.Max(280,MaxHeight-80),SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var p=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var title=Text("Добавить загрузку",23);title.FontWeight=FontWeights.SemiBold;p.Children.Add(title);p.Children.Add(Text("Вставь magnet-ссылку или выбери файл .torrent.",13,true));var input=new TextBox{Margin=new(0,8,0,18)};p.Children.Add(input);dialog.ContentRendered+=(_,_)=>input.Focus();
        p.Children.Add(Text("Папка загрузки: "+prefs.Folder+". Изменить её можно в настройках.",12,true));
        var error=Text("",12);var actions=new WrapPanel();
        async Task Add(string source){try{await downloads.Add(source.Trim(),prefs.Folder);dialog.Close();section="Загрузки";current=null;Render();Status.Text="Раздача добавлена. Ищем участников.";}catch(Exception ex){error.Text=ex.Message;}}
        var start=AsyncButton("Начать загрузку",()=>Add(input.Text));start.Style=(Style)FindResource("PrimaryButton");start.IsDefault=true;actions.Children.Add(start);actions.Children.Add(AsyncButton("Выбрать .torrent",async()=>{var f=new OpenFileDialog{Filter="BitTorrent (*.torrent)|*.torrent"};if(f.ShowDialog(dialog)==true)await Add(f.FileName);}));p.Children.Add(actions);p.Children.Add(error);p.Children.Add(Text("После скачивания клиент продолжает раздачу. Нажми «Пауза», чтобы остановить её.",12,true));dialog.ShowDialog();
    }
    async void ToggleDownload(object sender,RoutedEventArgs e){try{await downloads.Toggle((DownloadItem)((Button)sender).Tag);}catch(Exception ex){Status.Text=ex.Message;}finally{SyncTimer();}}
    async void RemoveDownload(object sender,RoutedEventArgs e)
    {
        var item=(DownloadItem)((FrameworkElement)sender).Tag;
        if(item.Busy){Status.Text="Подожди завершения текущей операции с загрузкой.";return;}
        try{await downloads.Remove(item);if(downloads.Items.Contains(item)){Status.Text="Загрузка ещё занята. Повтори удаление после завершения операции.";return;}Render();Status.Text="Загрузка удалена из очереди. Файлы сохранены.";}catch(Exception ex){Status.Text=ex.Message;}finally{SyncTimer();}
    }
    void OpenFolder(object sender,RoutedEventArgs e){var d=(DownloadItem)((Button)sender).Tag;if(Directory.Exists(d.Folder))Process.Start(new ProcessStartInfo(d.Folder){UseShellExecute=true});}
    void ApplyTheme()
    {
        var values=prefs.Light?new[]{"#F0F4FA","#FFFFFF","#101C32","#43536E","#9CAEC6","#D1E1FA","#1456C5","#FFFFFF","#DCE6F4","#E0EAFB","#134AB0","#FFFFFF","#0A398F","#E0EAFF"}:new[]{"#0B1220","#18243A","#F7FAFF","#BCCAE0","#566C90","#293F65","#9EC6FF","#0C2142","#101B2E","#223655","#8CB7FF","#102340","#B2D1FF","#243B60"};var keys=new[]{"Bg","Panel","Text","Muted","Edge","Selected","Accent","AccentInk","Sidebar","Hover","Primary","PrimaryInk","PrimaryHover","AccentSoft"};for(int i=0;i<keys.Length;i++){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));brush.Freeze();Application.Current.Resources[keys[i]]=brush;}
        Application.Current.Resources["Danger"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(prefs.Light?"#B42335":"#FF9AA5"));
        SidePanel.SetResourceReference(Border.BackgroundProperty,"Sidebar");
    }
    bool closing;
    async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closed)return;e.Cancel=true;if(closing)return;closing=true;
        DiagnosticLog.Write("closing",new{QueueCount=downloads.Items.Count});Status.Text="Сохраняем загрузки и закрываем приложение…";
        IsEnabled=false;updateCancellation.Cancel();catalogRefreshTimer.Stop();refresh.Stop();searchDelay.Stop();liveRequest?.Cancel();sourceRequest?.Cancel();archiveRequest?.Cancel();broadcastRequest?.Cancel();
        foreach(var view in releaseViews.Values)view.Request?.Cancel();
        bool saved=false;
        try{prefs.Save();await downloads.Close();saved=true;DiagnosticLog.Write("closed",new{QueueSaved=true});}
        catch(Exception ex){ErrorLog.Write(ex);MessageBox.Show(ex.Message,"Не удалось сохранить очередь");}
        finally
        {
            closed=true;
            if(saved)try{LaunchPreparedUpdate();}catch(Exception ex){MessageBox.Show(ex.Message,"Обновление не установлено");}
            sourceClient.Dispose();updateClient.Dispose();_ = Dispatcher.BeginInvoke(Close);
        }
    }
}
