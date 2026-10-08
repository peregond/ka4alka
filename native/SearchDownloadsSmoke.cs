using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] struct DownloadSmokePoint {public int X,Y;}
    [DllImport("user32.dll",SetLastError=true)] static extern bool GetCursorPos(out DownloadSmokePoint point);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetCursorPos(int x,int y);
    async Task SearchDownloadsSmoke(string output,Action<bool,string> check)
    {
        current=null;section="Фильмы";favoritesOnly=false;
        catalogMemoryBoundary=SharedCatalog.BoundaryUtc(DateTime.UtcNow).AddDays(-1);liveKey="expired-page";
        check(ExpireCatalogPages()&&catalogPages.Count==0&&liveKey==""&&!ExpireCatalogPages(),"crossing the daily refresh boundary expires in-memory catalog pages once");
        var films=Enumerable.Range(1,45).Select(id=>new MediaItem(1300+id,"Поисковая искра · фильм "+id,"Фильмы","драма",2024,"8.0","8.0","#526B69")).ToArray();
        var series=Enumerable.Range(1,5).Select(id=>new MediaItem(2300+id,"Поисковая искра · сериал "+id,"Сериалы","драма",2024,"8.0","8.0","#526B69")).ToArray();
        foreach(var item in films.Concat(series))cardMetadata[item.Id]=Task.FromResult(item);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
        searchProvider=async(kind,query,token)=>{calls++;await release.Task.WaitAsync(token);return kind=="Фильмы"?films:series;};
        Search.Text="Поисковая искра";check(!SearchActive,"typing does not submit a query before Search or Enter");
        SubmitSearch(Search,new RoutedEventArgs());UpdateLayout();
        check(SearchActive&&searchCategory==""&&LoadingIndicator.Visibility==Visibility.Visible,"submitted query defaults to both types with visible animated loading indicator");
        await Task.Delay(100);check(calls==2,"search dispatches film and series requests together");release.SetResult();
        var end=DateTime.UtcNow.AddSeconds(15);while(liveLoading&&DateTime.UtcNow<end)await Task.Delay(30);UpdateLayout();
        check(!liveLoading&&LoadingIndicator.Visibility==Visibility.Collapsed&&liveItems.Count==50&&catalogDisplay.Any(x=>x.Section=="Сериалы")&&catalogDisplay.Any(x=>x.Section=="Фильмы"),"combined results include both types and hide completed loading indicator");
        Button Filter(string type)=>FindVisual<Button>(FilterControls,b=>AutomationProperties.GetName(b)=="Результаты: "+type)??throw new Exception("Missing search type "+type);
        CheckCatalogFilterLine(check,"unified-search");
        check(new[]{"Все","Фильмы","Сериалы"}.All(type=>Filter(type) is {IsVisible:true,ActualWidth:>0,ActualHeight:>0}),"unified search type tabs remain visible in the single filter line below search");
        Filter("Сериалы").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
        check(catalogDisplay.Count==5&&catalogDisplay.All(x=>x.Section=="Сериалы")&&calls==2,"series tab filters existing results without another network request");
        Filter("Фильмы").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();check(catalogDisplay.Count==40&&catalogHasNext,"film results keep paged navigation");
        ShowDownloads(this,new RoutedEventArgs());ShowCatalogSection("Фильмы");UpdateLayout();
        check(SearchActive&&Search.Text=="Поисковая искра"&&searchCategory=="Фильмы"&&calls==2,"returning from downloads restores submitted query and selected search type");
        ShowCatalogSection("Фильмы");await Task.Delay(150);UpdateLayout();
        check(!SearchActive&&Search.Text==""&&CatalogSelection.IsDefault,"repeated active catalog navigation clears query and filters");
        var contexts=new List<string>();
        searchProvider=(kind,query,token)=>{contexts.Add(kind);return Task.FromResult<IReadOnlyList<MediaItem>>(kind=="Фильмы"?films:series);};
        async Task WaitSearch()
        {
            var deadline=DateTime.UtcNow.AddSeconds(5);while(liveLoading&&DateTime.UtcNow<deadline)await Task.Delay(25);UpdateLayout();
            check(!liveLoading&&liveItems.Count==50&&catalogDisplay.Any(x=>x.Section=="Фильмы")&&catalogDisplay.Any(x=>x.Section=="Сериалы"),"search from a persistent header returns both media types");
        }
        foreach(var card in new[]{films[0],series[0]})
        {
            section=card.Section;current=card;Render();UpdateLayout();
            check(SearchBar.IsVisible&&Search.ActualWidth>0,"search stays visible in the "+card.Section+" detail");
            Search.Text="Поиск из карточки";FocusCatalogSearch();
            check(current==card&&Search.IsKeyboardFocusWithin,"search shortcut focuses the field without leaving the open card");
            ClearSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
            check(current==card&&Search.Text=="","clearing the persistent search retains the open card");
            Search.Text="Поиск из "+card.Section;SearchSubmitButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await WaitSearch();
            check(current==null&&searchCategory==""&&SearchActive&&section==card.Section,"submitting from a card opens unified search results");
        }
        check(contexts.Count==4&&contexts.Count(x=>x=="Фильмы")==2&&contexts.Count(x=>x=="Сериалы")==2,"each card search requests both catalogs");
        searchProvider=null;submittedQuery="";searchCategory="";Search.Text="";current=null;section="Фильмы";lastCatalogSection="Фильмы";

        var releaseMovie=new MediaItem(99023,"Карточка с поиском раздач","Фильмы","драма",2025,"8.0","8.0","#526B69"){PageUrl=LiveCatalog.Base+"/movies/loading-fixture",Description="Описание уже загружено."};
        requestedDetails.Add(releaseMovie.Id);cardMetadata[releaseMovie.Id]=Task.FromResult(releaseMovie);
        liveReleases[releaseMovie.Id]=[new("loading-release","Карточка с поиском раздач (2025) 1080p","Fixture","https://example.test/release","magnet:?xt=urn:btih:"+new string('a',40),null,1024,5)];
        releaseViews[releaseMovie.Id]=new(){Checking=true,Sources=[new("RuTor",SourceState.Searching)]};current=releaseMovie;Render();await Task.Delay(150);UpdateLayout();
        var releaseLoading=FindVisual<Border>(Body,b=>AutomationProperties.GetName(b)=="Поиск раздач")??throw new Exception("Localized release loading indicator is missing.");
        check(releaseLoading.Visibility==Visibility.Visible&&FindVisual<TextBlock>(releaseLoading,t=>t.Text=="Ищем раздачи…")!=null&&FindVisual<ProgressBar>(releaseLoading,p=>p.IsIndeterminate)!=null&&LoadingIndicator.Visibility==Visibility.Collapsed,"release search shows its animated indicator inside the releases section");
        var qualityFilter=FindVisual<ComboBox>(Body,b=>AutomationProperties.GetName(b)=="Раздачи: Качество")??throw new Exception("Release loading hid the quality filter.");qualityFilter.IsDropDownOpen=true;await Task.Delay(100);
        releaseViews[releaseMovie.Id].Checking=false;RefreshDetail(releaseMovie.Id);UpdateLayout();
        check(releaseLoading.Visibility==Visibility.Collapsed&&qualityFilter.IsDropDownOpen,"finished release search hides its local indicator while preserving an open filter");qualityFilter.IsDropDownOpen=false;await Task.Delay(100);current=null;Render();UpdateLayout();

        var folder=Path.Combine(Preferences.DataDir,"details-fixture");Directory.CreateDirectory(folder);
        var posterUrl="https://example.test/downloads-cached-poster.png";
        void CachePoster(string url,Color color)
        {
            var cover=new RenderTargetBitmap(56,84,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();
            using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(new SolidColorBrush(color),null,new Rect(0,0,56,84));drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(197,176,123)),null,new Point(28,32),18,18);}
            cover.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(cover));
            var path=Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+".img");Directory.CreateDirectory(Path.GetDirectoryName(path)!);using(var file=File.Create(path))encoder.Save(file);
        }
        var repairedPosterUrl="https://example.test/downloads-repaired-poster.png";CachePoster(posterUrl,Color.FromRgb(54,100,118));CachePoster(repairedPosterUrl,Color.FromRgb(89,119,77));
        downloads.Items.Clear();var added=DateTime.UtcNow.AddHours(-1);
        var fixtures=Enumerable.Range(0,6).Select(i=>new DownloadItem{Id="redesign-"+i,Name="Series.Season."+(i+1)+".FullHD.WEB-DL.mkv",MediaTitle="Сериал · сезон "+(i+1),MediaSection="Сериалы",ImageUrl=i==0?null:posterUrl,AddedUtc=added.AddMinutes(i),Folder=folder,Progress=20+i*10,Paused=true,DownloadRate=i*1024,TotalBytes=(6-i)*1024L*1024*1024,Stats="50% · 1.0 ГБ из 2.0 ГБ · ↓ 0 КБ/с",Files=[new("Сезон 1/Серия 01.mkv",Path.Combine(folder,"episode1.mkv"),Path.Combine(folder,"episode1.mkv"),1024*1024,100),new("Сезон 1/Серия 02.mkv",Path.Combine(folder,"episode2.mkv"),Path.Combine(folder,"episode2.mkv"),1024*1024,35)]}).ToArray();
        foreach(var fixture in fixtures)downloads.Items.Add(fixture);
        var originalLight=prefs.Light;var originalSort=prefs.DownloadSort;var originalMinWidth=MinWidth;
        async Task SettleDownloads(){await Task.Delay(150);UpdateLayout();}
        ListBox Queue()=>FindVisual<ListBox>(Body,_=>true)??throw new Exception("Download queue is missing.");
        ListBoxItem FirstRow()=>Queue().ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem??throw new Exception("First download row is not realized.");
        Button Toolbar(string name)=>FindVisual<Button>(PageHeader,b=>AutomationProperties.GetName(b)==name)??throw new Exception("Missing download toolbar "+name);
        MenuItem Option(ContextMenu menu,string label)=>menu.Items.OfType<MenuItem>().Single(x=>AutomationProperties.GetName(x)==label||x.Header?.ToString()==label);
        void Shot(FrameworkElement visual,string name)
        {
            visual.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)visual.ActualWidth),Math.Max(1,(int)visual.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))png.Save(file);
        }
        void SelectSort(string key)
        {
            var button=Toolbar("Сортировка загрузок");button.ContextMenu!.Items.OfType<MenuItem>().Single(x=>x.Tag?.ToString()==key).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        async Task ProbeDialog(Func<Window,bool> match,Action show,Func<Window,Task> probe)
        {
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var deadline=DateTime.UtcNow.AddSeconds(12);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(70)};
            timer.Tick+=async(_,_)=>
            {
                var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Owner==this&&match(w));
                if(dialog==null)
                {
                    if(DateTime.UtcNow<deadline)return;timer.Stop();completion.TrySetException(new Exception("Expected download dialog did not open."));foreach(var owned in Application.Current.Windows.OfType<Window>().Where(w=>w.Owner==this).ToArray())owned.Close();return;
                }
                timer.Stop();try{dialog.UpdateLayout();await probe(dialog);completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{if(dialog.IsVisible)dialog.Close();}
            };
            timer.Start();try{show();await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));}finally{timer.Stop();}
        }
        try
        {
            prefs.DownloadSort="newest";downloadSort="newest";MinWidth=1280;Width=1280;Height=800;ShowDownloads(this,new RoutedEventArgs());await SettleDownloads();
            check(Queue().Items.Cast<DownloadItem>().Select(x=>x.Id).SequenceEqual(fixtures.Reverse().Select(x=>x.Id)),"download queue defaults to newest-added first");
            check(VisualElements<TextBlock>(PageHeader).Count(t=>t.Text=="Загрузки"||t.Text=="Очередь")==1&&ContextLabel.Visibility!=Visibility.Visible,"downloads page has one heading without a duplicate top label");
            var management=Toolbar("Управление загрузками");management.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await SettleDownloads();
            check(management.ContextMenu is{IsOpen:true,ActualHeight:>0},"download management opens as a visible toolbar submenu");
            foreach(var label in new[]{"Начать загрузки","Остановить загрузки","Запустить раздачи","Остановить раздачи"})check(Option(management.ContextMenu!,label)!=null,"download management menu includes "+label);
            check(!VisualElements<TextBox>(PageHeader).Any(),"speed limit fields stay out of the compact queue header");management.ContextMenu!.IsOpen=false;
            await ProbeDialog(w=>FindVisual<TextBox>(w,b=>AutomationProperties.GetName(b)=="Загрузка, КБ/с")!=null,()=>Option(management.ContextMenu!,"Лимиты скорости…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)),async dialog=>
            {
                FindVisual<TextBox>(dialog,b=>AutomationProperties.GetName(b)=="Загрузка, КБ/с")!.Text="512";FindVisual<TextBox>(dialog,b=>AutomationProperties.GetName(b)=="Отдача, КБ/с")!.Text="128";
                FindVisual<Button>(dialog,b=>AutomationProperties.GetName(b)=="Применить лимиты")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Task.Delay(150);
                check(downloads.DownloadLimitKbps==512&&downloads.UploadLimitKbps==128&&Preferences.Load().MaxUploadKbps==128,"speed-limit dialog applies and persists both rates");
            });
            await SettleDownloads();var first=FirstRow();check(first.ActualHeight<=120&&first.ActualHeight>0,"download rows fit within 120 pixels with both media and release titles");
            var poster=FindVisual<Image>(first,i=>i.DataContext is DownloadItem);check(poster?.Source!=null&&poster.ActualWidth>0&&poster.ActualWidth<90,"download row displays its cached media poster on the left");
            var originalPoster=poster!.Source;fixtures[^1].ImageUrl=repairedPosterUrl;fixtures[^1].Refresh();await SettleDownloads();
            check(poster.Source!=null&&!ReferenceEquals(poster.Source,originalPoster)&&ReferenceEquals(FirstRow(),first),"metadata repair repaints the existing poster without rebuilding its download row");
            fixtures[^1].ImageUrl=null;fixtures[^1].Refresh();await SettleDownloads();check(poster.Source==null,"clearing a repaired poster removes the stale image from its existing row");
            fixtures[^1].ImageUrl=posterUrl;fixtures[^1].Refresh();await SettleDownloads();check(poster.Source!=null,"existing download row restores its poster after a later metadata update");
            var title=FindVisual<TextBlock>(first,t=>t.Text==fixtures[^1].MediaTitle);check(title!=null&&poster!.TransformToAncestor(first).Transform(new Point()).X<title.TransformToAncestor(first).Transform(new Point()).X,"associated media title follows its poster");
            var releaseTitle=FindVisual<TextBlock>(first,t=>t.Text==fixtures[^1].Name);check(releaseTitle is{IsVisible:true}&&releaseTitle.FontSize<title!.FontSize&&releaseTitle.TransformToAncestor(first).Transform(new Point()).Y>title.TransformToAncestor(first).Transform(new Point()).Y,"download row displays its raw release name as a smaller secondary line below the Russian media title");
            var toggle=FindVisual<Button>(first,b=>AutomationProperties.GetName(b)==fixtures[^1].Action);check(toggle!=null&&FindVisual<TextBlock>(toggle,t=>t.Text==fixtures[^1].Action)!=null,"pause or continue action displays a readable text label");
            foreach(var label in new[]{"Подробнее","Открыть папку","Удалить из загрузок","Удалить файлы"})
            {
                var action=FindVisual<Button>(first,b=>AutomationProperties.GetName(b)==label);check(action!=null&&VisualElements<System.Windows.Shapes.Path>(action).Any(),"download action has an accessible label and recognizable icon: "+label);
            }
            var infoButton=FindVisual<Button>(first,b=>AutomationProperties.GetName(b)=="Подробнее")!;
            var infoPath=VisualElements<System.Windows.Shapes.Path>(infoButton).Single();
            var glyphBox=infoPath.TransformToAncestor(infoButton).TransformBounds(new Rect(infoPath.RenderSize));
            check(glyphBox.Top>=infoButton.BorderThickness.Top+infoButton.Padding.Top-1&&glyphBox.Bottom<=infoButton.ActualHeight-infoButton.BorderThickness.Bottom-infoButton.Padding.Bottom+1,"information icon fits inside its button padding without clipping its lower edge");
            check(!VisualElements<Button>(first).Any(b=>AutomationProperties.GetName(b)=="Действия загрузки"),"download removal actions are directly available without an ellipsis submenu");
            var keep=FindVisual<Button>(first,b=>AutomationProperties.GetName(b)=="Удалить из загрузок")!;var delete=FindVisual<Button>(first,b=>AutomationProperties.GetName(b)=="Удалить файлы")!;
            check(keep.Foreground is SolidColorBrush neutral&&Math.Abs(neutral.Color.R-neutral.Color.G)<35&&delete.Foreground is SolidColorBrush danger&&danger.Color.R>danger.Color.G+20,"keep-files removal is neutral gray and permanent file removal is red");
            check(keep.ToolTip?.ToString()?.Contains("файл",StringComparison.OrdinalIgnoreCase)==true&&delete.ToolTip?.ToString()?.Contains("файл",StringComparison.OrdinalIgnoreCase)==true,"both removal actions explain their effect on downloaded files");
            var actionItem=(DownloadItem)Queue().Items[0];
            try
            {
                actionItem.Busy=true;actionItem.Refresh();await SettleDownloads();
                check(!keep.IsEnabled&&!delete.IsEnabled&&!toggle!.IsEnabled,"direct row actions disable removal and pause controls while their download is busy");
            }
            finally{actionItem.Busy=false;actionItem.Refresh();await SettleDownloads();}
            check(keep.IsEnabled&&delete.IsEnabled&&toggle!.IsEnabled,"direct row actions re-enable their controls when the download is ready");
            var sort=Toolbar("Сортировка загрузок");sort.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await SettleDownloads();check(sort.ContextMenu is{IsOpen:true,ActualHeight:>0},"download sorting is discoverable through a toolbar menu");
            check(sort.ContextMenu!.Items.OfType<MenuItem>().Select(x=>x.Tag?.ToString()).Order().SequenceEqual(new[]{"newest","name","speed","size","progress"}.Order()),"queue sorting offers addition date, title, speed, size and progress");sort.ContextMenu.IsOpen=false;
            foreach(var order in new[]{("name",DownloadSort.Name),("speed",DownloadSort.Speed),("size",DownloadSort.Size),("progress",DownloadSort.Progress),("newest",DownloadSort.Newest)})
            {
                SelectSort(order.Item1);await SettleDownloads();check(Queue().Items.Cast<DownloadItem>().Select(x=>x.Id).SequenceEqual(DownloadOrdering.Sort(fixtures,order.Item2).Select(x=>x.Id)),"download sort menu applies "+order.Item1);
            }
            SelectSort("speed");await SettleDownloads();
            string SortGuard()=>"mouse="+Queue().IsMouseOver+", keyboard="+Queue().IsKeyboardFocusWithin+", menu="+downloadMenus.Any(menu=>menu.IsOpen)+", timer="+refresh.IsEnabled+", state="+WindowState+", first="+((DownloadItem)Queue().Items[0]).Id;
            File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO: speed sort before moving cursor: "+SortGuard()+Environment.NewLine);
            if(!GetCursorPos(out var cursor))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot preserve the smoke-test cursor position.");
            var refreshTicks=0;EventHandler countRefresh=(_,_)=>refreshTicks++;refresh.Tick+=countRefresh;
            try
            {
                var toolbar=Toolbar("Сортировка загрузок");var target=toolbar.PointToScreen(new Point(toolbar.ActualWidth/2,toolbar.ActualHeight/2));
                if(!SetCursorPos((int)Math.Round(target.X),(int)Math.Round(target.Y)))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot move the smoke-test cursor outside the queue.");
                Activate();toolbar.Focus();System.Windows.Input.Mouse.Synchronize();await SettleDownloads();
                check(!Queue().IsMouseOver&&!Queue().IsKeyboardFocusWithin&&!downloadMenus.Any(menu=>menu.IsOpen)&&refresh.IsEnabled,"speed sort can refresh while the pointer and focus are on the toolbar: "+SortGuard());
                fixtures[0].DownloadRate=9*1024*1024;fixtures[0].Refresh();var until=DateTime.UtcNow.AddSeconds(6);
                while((refreshTicks==0||Queue().Items[0]!=fixtures[0])&&DateTime.UtcNow<until)await Task.Delay(50);UpdateLayout();
                check(refreshTicks>0&&Queue().Items[0]==fixtures[0],"speed sorting updates through the running refresh timer: ticks="+refreshTicks+", "+SortGuard());
            }
            finally{refresh.Tick-=countRefresh;SetCursorPos(cursor.X,cursor.Y);System.Windows.Input.Mouse.Synchronize();}
            check(FindVisual<Image>(FirstRow(),i=>i.DataContext is DownloadItem)?.Source==null&&VisualElements<System.Windows.Shapes.Path>(FirstRow()).Any(),"torrent without a known poster keeps a visible native placeholder");
            SelectSort("newest");await SettleDownloads();prefs.Light=false;ApplyTheme();Render();await SettleDownloads();Shot(this,"downloads-redesign-dark");Shot(this,"downloads");
            var expectedInk=((SolidColorBrush)FindResource("Text")).Color;check(FindVisual<TextBlock>(FirstRow(),t=>t.Text==fixtures[^1].MediaTitle)?.Foreground is SolidColorBrush ink&&ink.Color==expectedInk,"download media title follows the dark theme");
            prefs.Light=true;ApplyTheme();Render();await SettleDownloads();Shot(this,"downloads-redesign-light");
            foreach(var light in new[]{true,false})
            {
                prefs.Light=light;ApplyTheme();UpdateLayout();
                double Brightness(string key){var c=((SolidColorBrush)FindResource(key)).Color;double Linear(byte v){var s=v/255d;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4);}return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);}
                double Ratio(string foreground,string background){var a=Brightness(foreground);var b=Brightness(background);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);}
                check(new[]{"Bg","Panel","Sidebar","Selected","Hover"}.All(surface=>Ratio("Text",surface)>=7&&Ratio("Muted",surface)>=4.5),$"{(light?"light":"dark")} theme keeps primary and secondary text legible on every main surface");
                check(Ratio("Accent","AccentSoft")>=4.5&&Ratio("PrimaryInk","Primary")>=4.5,$"{(light?"light":"dark")} theme keeps accent actions and primary buttons readable");
            }
            prefs.Light=true;ApplyTheme();
            MinWidth=360;Width=510;Height=600;await SettleDownloads();Shot(this,"downloads-redesign-narrow");
            foreach(var button in VisualElements<Button>(FirstRow()).Where(b=>b.IsVisible))
            {
                var origin=button.TransformToAncestor(Body).Transform(new Point());check(origin.X>=-1&&origin.X+button.ActualWidth<=Body.ActualWidth+1,"download action stays inside narrow queue: "+AutomationProperties.GetName(button));
            }
            Height=360;await SettleDownloads();check(Body.ActualHeight>80&&Queue().ActualHeight>80,"download toolbar leaves room for the queue in a short window");Shot(this,"downloads-redesign-short");
            MinWidth=1280;Width=1280;Height=800;prefs.Light=false;ApplyTheme();Render();await SettleDownloads();
            await ProbeDialog(w=>w.Title.StartsWith("Файлы · "),()=>FindVisual<Button>(FirstRow(),b=>AutomationProperties.GetName(b)=="Подробнее")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)),async dialog=>
            {
                var files=FindVisual<ListBox>(dialog,b=>AutomationProperties.GetName(b)=="Файлы загрузки")!;
                check(files.Items.Count==2&&VisualElements<TextBlock>(dialog).Any(t=>t.Text.Contains("Готово файлов: 1 из 2")),"details show episode names, individual progress and completed-file count");
                var fileSearch=FindVisual<TextBox>(dialog,b=>AutomationProperties.GetName(b)=="Найти файл в раздаче")!;fileSearch.Text="02";dialog.UpdateLayout();check(files.Items.Count==1,"file details can filter a specific episode");fileSearch.Clear();dialog.UpdateLayout();check(files.Items.Count==2,"clearing the episode filter restores the full file list");
                files.SelectedIndex=0;var selected=files.SelectedItem;await Task.Delay(2300);dialog.UpdateLayout();check(ReferenceEquals(files.SelectedItem,selected),"file details retain selection across live progress refreshes");
                var episode=FindVisual<TextBlock>(files,t=>t.Text=="Серия 01.mkv")??throw new Exception("Realized episode filename is missing.");
                DependencyObject? ancestor=episode;while(ancestor!=null&&ancestor is not Border)ancestor=VisualTreeHelper.GetParent(ancestor);
                var fileFrame=ancestor as Border??throw new Exception("Episode row background is missing.");
                var fileInk=((SolidColorBrush)FindResource("Text")).Color;check(episode.Foreground is SolidColorBrush filenameInk&&filenameInk.Color==fileInk,"dark file details use the readable theme color for episode names");
                var amount=FindVisual<TextBlock>(fileFrame,t=>t.Text.Contains(" из "))??throw new Exception("Downloaded episode byte count is missing.");
                double Luminance(Color color){double Linear(byte value){var s=value/255d;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4);}return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);}
                double Contrast(Brush foreground,Brush background){var first=Luminance(((SolidColorBrush)foreground).Color);var second=Luminance(((SolidColorBrush)background).Color);return (Math.Max(first,second)+.05)/(Math.Min(first,second)+.05);}
                var nameContrast=Contrast(episode.Foreground,fileFrame.Background);var amountContrast=Contrast(amount.Foreground,fileFrame.Background);
                check(nameContrast>=4.5&&amountContrast>=4.5,$"dark episode filename and downloaded byte count contrast clearly with their row background: {nameContrast:F1}:1 and {amountContrast:F1}:1");
                check(VisualElements<System.Windows.Shapes.Path>(fileFrame).Any(icon=>icon.Data is{Bounds.IsEmpty:false}),"file details resolve the file icon geometry inside the owned dialog");Shot(dialog,"downloads-details-dark");
                prefs.Light=true;ApplyTheme();dialog.UpdateLayout();Shot(dialog,"downloads-details-light");dialog.Width=360;dialog.Height=300;dialog.UpdateLayout();Shot(dialog,"downloads-details-minimum");check(files.ActualHeight>30,"file details remain usable in a minimum-size window");
            });
            var font=new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
            check(FontFamily.Source.Contains("Fonts/#Inter")&&font.TryGetGlyphTypeface(out var glyph)&&glyph.FamilyNames.Values.Any(name=>name=="Inter")&&glyph.CharacterToGlyphMap.ContainsKey('М'),"the packaged Inter font is used by the window and contains Cyrillic glyphs");
            check(Icon is BitmapSource&&BrandLogo.Source is BitmapSource,"application icon and transparent kettlebell logo load from packaged resources");
            Width=1280;Height=800;section="Загрузки";current=null;Render();await SettleDownloads();
            fixtures[^1].MediaPageUrl="https://w6.zona.plus/tvseries/download-fixture";fixtures[^1].Refresh();await SettleDownloads();
            var linked=DownloadMetadata.Card(fixtures[^1])!;requestedDetails.Add(linked.Id);
            foreach(var label in new[]{"Открыть карточку по постеру","Открыть карточку по названию"})
            {
                var link=FindVisual<Button>(FirstRow(),b=>AutomationProperties.GetName(b)==label)!;
                check(link.IsEnabled&&link.Cursor==(label=="Открыть карточку по названию"?Cursors.Arrow:Cursors.Hand),"download card remains clickable with a plain title: "+label);
                if(label=="Открыть карточку по названию")
                {
                    var text=FindVisual<TextBlock>(link,t=>t.Text==fixtures[^1].DisplayName)!;
                    var expected=((SolidColorBrush)FindResource("Text")).Color;
                    GetCursorPos(out var previous);
                    try
                    {
                        Activate();var target=link.PointToScreen(new Point(link.ActualWidth/2,link.ActualHeight/2));SetCursorPos((int)target.X,(int)target.Y);Mouse.Synchronize();await SettleDownloads();
                        var frame=FindVisual<Border>(link,b=>b.Name=="Frame")!;
                        check(link.IsMouseOver&&text.Foreground is SolidColorBrush titleInk&&titleInk.Color==expected&&(text.TextDecorations==null||text.TextDecorations.Count==0)&&frame.Background is SolidColorBrush background&&background.Color.A==0,"hovering a download title keeps plain text colour and transparent background without an underline");
                        Shot(FirstRow(),"downloads-plain-title-hover");
                    }
                    finally{SetCursorPos(previous.X,previous.Y);}
                }
                link.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await SettleDownloads();
                check(current?.Id==linked.Id&&detailTitle?.Text==fixtures[^1].MediaTitle&&section=="Загрузки","download poster/title opens its associated internal card while retaining the queue as the return destination");
                FindVisual<Button>(PageHeader,b=>AutomationProperties.GetName(b)=="Загрузки")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await SettleDownloads();
                check(current==null&&Queue().Items.Count==fixtures.Length&&fixtures.All(x=>x.Paused),"returning from the internal card preserves the queue and its paused transfers");
            }
            searchProvider=(kind,query,token)=>Task.FromResult<IReadOnlyList<MediaItem>>(kind=="Фильмы"?films:series);
            Search.Text="Поиск из загрузок";FocusCatalogSearch();UpdateLayout();
            check(section=="Загрузки"&&current==null&&SearchBar.IsVisible&&Search.IsKeyboardFocusWithin,"download search stays visible and focuses without leaving the queue");
            ClearSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
            check(section=="Загрузки"&&Queue().Items.Count==fixtures.Length&&Search.Text=="","clearing download search preserves the queue");
            Search.Text="Поиск из загрузок";SearchSubmitButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await WaitSearch();
            check(section is "Фильмы" or "Сериалы"&&current==null&&searchCategory==""&&downloads.Items.Count==fixtures.Length&&fixtures.All(x=>x.Paused),"submitting download search opens both-type results without changing queued tasks");
            searchProvider=null;submittedQuery="";searchCategory="";Search.Text="";section="Загрузки";Render();await SettleDownloads();
            check(!downloads.EngineCreated,"download design and menu fixtures do not start a torrent engine");
        }
        finally{downloads.Items.Clear();prefs.Light=originalLight;prefs.DownloadSort=originalSort;downloadSort=originalSort;ApplyTheme();MinWidth=originalMinWidth;Width=1280;Height=800;}
        check(SidebarUpdateButton.Visibility==Visibility.Collapsed,"sidebar update prompt stays hidden until a package is ready");
        preparedUpdateJob="fixture";RefreshSidebarUpdate();UpdateLayout();
        check(SidebarUpdateButton.Visibility==Visibility.Visible&&SidebarUpdateButton.IsEnabled&&SidebarUpdateButton.TransformToAncestor(SidebarFooter).Transform(new Point()).Y<DownloadsButton.TransformToAncestor(SidebarFooter).Transform(new Point()).Y,"ready update appears above Downloads in the sidebar");
        check(!SystemParameters.ClientAreaAnimation||SidebarUpdateButton.HasAnimatedProperties,"ready update softly animates when system animations are enabled");
        Width=680;await Task.Delay(100);UpdateLayout();check(SidebarUpdateButton.Visibility==Visibility.Visible&&SidebarUpdateButton.ActualWidth>0,"sidebar update remains available in narrow layout");
        preparedUpdateJob=null;RefreshSidebarUpdate();Width=1280;
    }
}
