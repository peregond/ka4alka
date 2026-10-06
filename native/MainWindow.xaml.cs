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
    readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromSeconds(2)};
    readonly DispatcherTimer searchDelay=new(){Interval=TimeSpan.FromMilliseconds(350)};
    string section="Фильмы",genre="Все",sort="По популярности";
    readonly Dictionary<string,string> catalogQueries=[];
    string lastCatalogSection="Фильмы";
    bool ready,closed;
    MediaItem? current;
    public MainWindow()
    {
        InitializeComponent();
        onlineIndex=new(sourceClient);
        EnableAdaptiveLayout();
        EnableShortcuts();
        try{downloads=new();}catch(Exception e){downloads=newEmpty();Status.Text="Не удалось прочитать очередь: "+e.Message;}
        foreach(var name in new[]{"Фильмы","Сериалы"}) { var n=name;var b=ActionButton(n,n=="Фильмы"?"IconMovies":"IconSeries",()=>ShowCatalogSection(n),"NavButton");b.Tag=n;b.ToolTip=n;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new(0,0,0,5);Navigation.Children.Add(b); }
        refresh.Tick+=(_,_)=>{downloads.Update();SyncTimer();};
        ContentRendered+=async(_,_)=>{if(!prefs.AutoResumeDownloads||downloads.PendingResumeCount==0)return;Status.Text="Продолжаем загрузки…";var resumed=await downloads.ResumePendingAsync();Status.Text=resumed>0?$"Продолжено загрузок: {resumed}":"Не удалось продолжить загрузки. Проверь очередь.";if(!closed){SyncTimer();if(section=="Загрузки")Render();}};
        StateChanged+=(_,_)=>SyncTimer();searchDelay.Tick+=(_,_)=>{searchDelay.Stop();if(section is "Фильмы" or "Сериалы"){current=null;Render();}};
        Closing+=OnClosing;ready=true;ApplyTheme();ApplyCompactLayout();Render();
    }
    static DownloadService newEmpty(){var path=Path.Combine(Preferences.DataDir,"queue.json");if(File.Exists(path))File.Move(path,path+".unreadable-"+DateTime.Now.Ticks);return new();}
    static TextBlock Text(string value,double size=13,bool muted=false){var t=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};t.SetResourceReference(TextBlock.ForegroundProperty,muted?"Muted":"Text");return t;}
    static Button Button(string label,Action action){var b=new Button{Content=label};b.Click+=(_,_)=>action();return b;}
    static Button AsyncButton(string label,Func<Task> action){var b=new Button{Content=label};b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(Exception e){MessageBox.Show(e.Message,"Качалка");}finally{b.IsEnabled=true;}};return b;}
    void SyncTimer(){if(!closed&&(downloads.EngineCreated||section=="Загрузки"&&WindowState!=WindowState.Minimized))refresh.Start();else refresh.Stop();}
    void SearchChanged(object sender,TextChangedEventArgs e){if(!ready)return;ClearSearchButton.Visibility=Search.Text.Length>0?Visibility.Visible:Visibility.Collapsed;searchDelay.Stop();if(section is "Фильмы" or "Сериалы"){catalogQueries[section]=Search.Text;lastCatalogSection=section;livePage=1;ResetCatalogFilters();searchDelay.Start();}}
    void ShowCatalogSection(string name){searchDelay.Stop();section=name;lastCatalogSection=name;current=null;genre="Все";livePage=1;favoritesOnly=false;ResetCatalogFilters();Search.Text=catalogQueries.GetValueOrDefault(name,"");searchDelay.Stop();Render();}
    void ShowDownloads(object sender,RoutedEventArgs e){searchDelay.Stop();section="Загрузки";current=null;Render();}
    void PosterResized(object sender,SizeChangedEventArgs e){if(sender is Border poster){if(e.WidthChanged&&e.NewSize.Width>0){var height=e.NewSize.Width*1.5;if(double.IsNaN(poster.Height)||Math.Abs(poster.Height-height)>1)poster.Height=height;}ClipPoster(poster);}}
    void Render()
    {
        if(!ready)return;catalogList=null;detailHero=null;detailPoster=null;detailSynopsis=null;detailTitle=null;detailDescription=null;descriptionToggle=null;inlineCatalogFilters=null;inlineCatalogFilterScroll=null;inlineFilterButton=null;PageHeader.Children.Clear();Body.Children.Clear();Body.RowDefinitions.Clear();SyncTimer();UpdateFilterRail();
        SearchBar.Visibility=current==null&&(section is "Фильмы" or "Сериалы")?Visibility.Visible:Visibility.Collapsed;
        ContextLabel.Visibility=SearchBar.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;ContextLabel.Text=section;
        foreach(Button b in Navigation.Children){var selected=b.Tag?.ToString()==section;b.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty,selected?"Selected":"Sidebar");b.SetResourceReference(Control.ForegroundProperty,selected?"Text":"Muted");b.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;}
        DownloadsButton.SetResourceReference(Control.BackgroundProperty,section=="Загрузки"?"Selected":"Sidebar");
        SettingsButton.SetResourceReference(Control.BackgroundProperty,section=="Настройки"?"Selected":"Sidebar");
        if(section=="Настройки"){RenderSettings();return;}
        if(section=="Источники"){RenderSources();return;}
        if(current?.IsLive==true){RenderLiveDetail(current);return;}
        if(current!=null){Detail(current);return;}
        if(!demoCatalog&&section is "Фильмы" or "Сериалы"){RenderLiveCatalog();return;}
        if(!demoCatalog&&section is "Музыка" or "Игры" or "Программы"){RenderArchive();return;}
        if(!demoCatalog&&section is "ТВ-каналы" or "Радио" or "Спорт"){RenderBroadcast();return;}
        PageHeader.Children.Add(Text(section,25));
        if(section=="Загрузки"){
            downloads.Update();var description=Text("Скорость показывает передачу файлов. Активные загрузки продолжаются после перезапуска.",12,true);description.Margin=new(0,0,0,20);PageHeader.Children.Add(description);
            if(downloads.Items.Count==0){var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=440};var symbol=Text("↓",38);symbol.HorizontalAlignment=HorizontalAlignment.Center;symbol.SetResourceReference(TextBlock.ForegroundProperty,"Accent");empty.Children.Add(symbol);var title=Text("Загрузок пока нет",22);title.FontWeight=FontWeights.SemiBold;title.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(title);var hint=Text("Выбери раздачу в карточке фильма или добавь magnet-ссылку и .torrent-файл.",13,true);hint.TextAlignment=TextAlignment.Center;hint.Margin=new(0,0,0,20);empty.Children.Add(hint);var add=Button("＋ Добавить торрент",()=>AddTorrent(this,new RoutedEventArgs()));add.Style=(Style)FindResource("PrimaryButton");add.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(add);Body.Children.Add(new Border{Child=empty,Background=(Brush)FindResource("Panel"),BorderBrush=(Brush)FindResource("Edge"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Margin=new Thickness(0,0,0,12)});}
            else Body.Children.Add(new ListBox{ItemsSource=downloads.Items,ItemTemplate=(DataTemplate)FindResource("DownloadRow")});
            return;
        }
        PageHeader.Children.Add(Text("Демо-каталог · названия, раздачи и оценки Кинопоиска / IMDb вымышлены",11,true));
        var filters=new WrapPanel();if(section is "Фильмы" or "Сериалы")foreach(var g in new[]{"Все","Фантастика","Триллер","Драма","Приключения"}){var value=g;var b=Button(g,()=>{genre=value;Render();});b.Opacity=g==genre?1:.6;filters.Children.Add(b);}
        var order=new ComboBox{ItemsSource=new[]{"По популярности","По году","По названию"},SelectedItem=sort};order.SelectionChanged+=(_,_)=>{sort=order.SelectedItem.ToString()!;Render();};filters.Children.Add(order);PageHeader.Children.Add(filters);
        IEnumerable<MediaItem> result=Catalog.Items.Concat(prefs.LiveFavorites).Where(x=>section=="Избранное"?prefs.Favorites.Contains(x.Id):x.Section==section).Where(x=>x.Title.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).Where(x=>genre=="Все"||x.Genre==genre);
        result=sort=="По году"?result.OrderByDescending(x=>x.Year):sort=="По названию"?result.OrderBy(x=>x.Title):result;
        var cards=result.ToArray();ShowCatalog(cards);if(cards.Length==0)PageHeader.Children.Add(Text("Ничего не найдено. Измени поиск или фильтры.",14,true));
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
    async void RemoveDownload(object sender,RoutedEventArgs e){try{await downloads.Remove((DownloadItem)((Button)sender).Tag);Render();}catch(Exception ex){Status.Text=ex.Message;}}
    void OpenFolder(object sender,RoutedEventArgs e){var d=(DownloadItem)((Button)sender).Tag;if(Directory.Exists(d.Folder))Process.Start(new ProcessStartInfo(d.Folder){UseShellExecute=true});}
    void ApplyTheme()
    {
        var values=prefs.Light?new[]{"#FAFBF8","#FFFFFF","#202522","#70786F","#E2E7DE","#E8EDE5","#287D59","#FFFFFF","#EFF2EB","#F0F3ED","#202522","#FFFFFF","#39423B","#E8F2EB"}:new[]{"#151917","#1D2420","#EDF2ED","#A0ADA3","#354138","#303D33","#93CDB0","#163522","#1A211C","#29352D","#EDF2ED","#182019","#FFFFFF","#293F32"};var keys=new[]{"Bg","Panel","Text","Muted","Edge","Selected","Accent","AccentInk","Sidebar","Hover","Primary","PrimaryInk","PrimaryHover","AccentSoft"};for(int i=0;i<keys.Length;i++){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));brush.Freeze();Application.Current.Resources[keys[i]]=brush;}
        SidePanel.SetResourceReference(Border.BackgroundProperty,"Sidebar");
    }
    bool closing;
    async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closed)return;e.Cancel=true;if(closing)return;closing=true;
        IsEnabled=false;updateCancellation.Cancel();refresh.Stop();searchDelay.Stop();liveRequest?.Cancel();sourceRequest?.Cancel();archiveRequest?.Cancel();broadcastRequest?.Cancel();
        foreach(var view in releaseViews.Values)view.Request?.Cancel();
        bool saved=false;
        try{prefs.Save();await downloads.Close();saved=true;}
        catch(Exception ex){MessageBox.Show(ex.Message,"Не удалось сохранить очередь");}
        finally
        {
            closed=true;
            if(saved)try{LaunchPreparedUpdate();}catch(Exception ex){MessageBox.Show(ex.Message,"Обновление не установлено");}
            sourceClient.Dispose();updateClient.Dispose();_ = Dispatcher.BeginInvoke(Close);
        }
    }
}
