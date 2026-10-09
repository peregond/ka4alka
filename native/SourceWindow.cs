using System.IO;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;
public partial class MainWindow
{
    readonly SourceClient sourceClient=new();
    readonly List<SourceConfig> sourceConfigs=[];
    CancellationTokenSource? sourceRequest;
    readonly SemaphoreSlim coverSlots=new(2);
    readonly Dictionary<string,BitmapImage> coverCache=[];
    readonly ConditionalWeakTable<Image,CoverRequest> coverRequests=new();
    readonly Dictionary<Window,CoverViewport> coverViewports=[];
    CoverViewport? coverViewport;
    sealed class CoverRequest
    {
        public DownloadItem? Subject;
        public EventHandler<PropertyChangedEventArgs>? Changed;
        public object? Context;
        public string? Url;
        public CancellationTokenSource? Request;
        public CancellationTokenSource? FeatureRequest;
        public bool FeatureAttempted;
        public object? RatingsContext;
        public DateTime RetryAfterUtc;
        public DateTime NextCacheTouchUtc;
        public Exception? LastFailure;
    }
    IReadOnlyList<SourceEntry> sourceResults=[];
    string sourceQuery="",sourceCategory="Фильмы";
    int sourceIndex,sourcePage=1;
    bool searching;
    void RenderSources()
    {
        PageHeader.Children.Add(Text("Источники",25));
        PageHeader.Children.Add(Text("Живой поиск · Internet Archive и подключаемые Torznab-индексаторы",12,true));
        var controls=new WrapPanel();var sources=new ComboBox{ItemsSource=new[]{"Internet Archive"}.Concat(sourceConfigs.Select(x=>x.Name)).ToArray(),SelectedIndex=sourceIndex,MinWidth=190};
        sources.SelectionChanged+=(_,_)=>{sourceIndex=sources.SelectedIndex;sourceResults=[];sourcePage=1;Render();};controls.Children.Add(sources);
        var categories=new ComboBox{ItemsSource=new[]{"Фильмы","Музыка","Игры","Программы"},SelectedItem=sourceCategory,IsEnabled=sourceIndex==0};categories.SelectionChanged+=(_,_)=>{sourceCategory=categories.SelectedItem.ToString()!;sourcePage=1;};controls.Children.Add(categories);
        controls.Children.Add(Button("Добавить индексатор",AddIndexer));PageHeader.Children.Add(controls);
        var query=new TextBox{Text=sourceQuery,Margin=new(0,0,10,12),MinWidth=240,MaxWidth=450};query.TextChanged+=(_,_)=>sourceQuery=query.Text;
        var row=new WrapPanel();row.Children.Add(query);var searchButton=AsyncButton(searching?"Поиск…":"Найти",async()=>{sourcePage=1;await FindSources();});searchButton.IsEnabled=!searching;row.Children.Add(searchButton);
        if(searching)row.Children.Add(Button("Отменить",()=>sourceRequest?.Cancel()));PageHeader.Children.Add(row);
        query.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&!searching){sourcePage=1;await FindSources();}};
        PageHeader.Children.Add(Text("Показано: "+sourceResults.Count+(sourceIndex==0?" · страница "+sourcePage:""),12,true));
        var list=new ListBox();foreach(var result in sourceResults)list.Items.Add(result);
        list.ItemTemplate=(DataTemplate)FindResource("SourceRow");Body.Children.Add(list);
        if(sourceResults.Count>0&&sourceIndex==0){var paging=new WrapPanel();if(sourcePage>1)paging.Children.Add(AsyncButton("← Назад",async()=>{sourcePage--;await FindSources();}));if(sourceResults.Count==24)paging.Children.Add(AsyncButton("Далее →",async()=>{sourcePage++;await FindSources();}));PageHeader.Children.Add(paging);}
        if(!searching&&sourceResults.Count==0)PageHeader.Children.Add(Text("Введи название и нажми «Найти». Наличие torrent-файла проверяется при выборе записи.",13,true));
    }
    async Task FindSources()
    {
        sourceRequest?.Cancel();sourceRequest?.Dispose();sourceRequest=new();var token=sourceRequest.Token;searching=true;Render();
        try{var items=sourceIndex==0?await sourceClient.SearchArchive(sourceQuery,sourceCategory,sourcePage,token):await sourceClient.SearchTorznab(sourceConfigs[sourceIndex-1],sourceQuery,token);if(!token.IsCancellationRequested){sourceResults=items;Status.Text=$"Источник ответил: {items.Count} записей";}}
        catch(OperationCanceledException){Status.Text="Поиск отменён или источник не ответил вовремя.";}
        catch(Exception){Status.Text="Не удалось получить ответ. Проверь соединение, адрес источника и API-ключ.";}
        finally{searching=false;if(!closed&&section=="Источники")Render();}
    }
    async void SourceDownload(object sender,RoutedEventArgs e)
    {
        if(closing||closed)return;
        var button=(Button)sender;var item=(SourceEntry)button.Tag;var media=current?.Cinema==true?current:null;
        if(media!=null)media=DownloadMetadata.EnrichMedia(media,prefs.LiveFavorites.Concat(liveItems).Concat(catalogIndex.Recent(media.Section,200)).Append(media));
        button.IsEnabled=false;
        try{if(!EnsureDownloadFolder()){Status.Text="Папка для загрузок не выбрана. Её можно выбрать в настройках.";return;}
            Status.Text="Проверяем раздачу…";using var timeout=CancellationTokenSource.CreateLinkedTokenSource(reliabilityCancellation.Token);timeout.CancelAfter(TimeSpan.FromSeconds(30));var source=await sourceClient.TorrentFile(item,timeout.Token);
            if(closing||closed)return;
            if(media!=null&&cardMetadata.TryGetValue(media.Id,out var metadata)&&metadata.IsCompletedSuccessfully)media=DownloadMetadata.EnrichMedia(media,[metadata.Result]);
            var added=await downloads.Add(source,prefs.Folder,media,item.ImageUrl,item);if(closing||closed)return;section="Загрузки";current=null;Render();Status.Text=added.LowSpacePaused?added.SpacePauseMessage:item.Seeds==0?"Раздача добавлена, но источник показывает: отдают 0. Ждём участников.":"Раздача добавлена. Ищем участников.";
        }catch(Exception)when(closing||closed){}
        catch(Exception error){var message=error is HttpRequestException?"Не удалось получить раздачу из источника.":error.Message;Status.Text=message;MessageBox.Show(this,message,"Не удалось начать загрузку",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{if(!closing&&!closed)button.IsEnabled=true;}
    }
    void SourcePage(object sender,RoutedEventArgs e){var item=(SourceEntry)((Button)sender).Tag;if(string.IsNullOrEmpty(item.PageUrl))return;System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SourceClient.WebUri(item.PageUrl).AbsoluteUri){UseShellExecute=true});}
    void SourceCover(object sender,RoutedEventArgs e)
    {
        if(closing||closed)return;
        var image=(Image)sender;var state=ObserveDownloadCover(image);
        CoverViewportFor(image,true)?.Track(image,position=>CoverVisibilityChanged(image,state,position));
    }
    void CoverVisibilityChanged(Image image,CoverRequest state,CoverViewport.Position position)
    {
        if(!position.Near||closing||closed){CancelCover(state);CancelFeatureCover(state);return;}
        var item=image.DataContext;var url=CoverUrl(item);
        if(!ReferenceEquals(state.Context,item)||state.Url!=url)
        {
            CancelCover(state);CancelFeatureCover(state);state.Context=item;state.Url=url;state.RetryAfterUtc=default;state.NextCacheTouchUtc=default;state.FeatureAttempted=false;image.Source=null;
        }
        if(item is MediaItem card&&card.IsLive&&!ReferenceEquals(state.RatingsContext,item)){state.RatingsContext=item;_=UpdateCardRatings(card);}
        _=LoadCover(image,state,item,url);
        if(image.Tag?.ToString()=="FeaturePoster"&&item is MediaItem feature&&!state.FeatureAttempted&&state.FeatureRequest==null)
            _=LoadFeatureCover(image,state,feature);
    }
    async Task LoadFeatureCover(Image image,CoverRequest state,MediaItem item)
    {
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(15));state.FeatureRequest=request;state.FeatureAttempted=true;
        try{await ImproveFeaturePoster(image,item,request.Token);}
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch{ /* A normal poster remains available when the larger banner is unavailable. */ }
        finally{if(ReferenceEquals(state.FeatureRequest,request))state.FeatureRequest=null;}
    }
    async Task LoadCover(Image image,CoverRequest state,object? item,string? url)
    {
        if(string.IsNullOrWhiteSpace(url))
        {
            CancelCover(state);state.Context=item;state.Url=null;state.RetryAfterUtc=default;return;
        }
        if(state.Request!=null||image.Source!=null||DateTime.UtcNow<state.RetryAfterUtc)return;
        if(coverCache.TryGetValue(url,out var cached))
        {
            image.Source=cached;
            if(item is MediaItem or DownloadItem&&DateTime.UtcNow>=state.NextCacheTouchUtc)
            {
                state.NextCacheTouchUtc=DateTime.UtcNow.AddMinutes(10);var path=CoverPath(url);
                _=Task.Run(()=>CacheFiles.Touch(path));
            }
            return;
        }
        var sourceToken=item is MediaItem or DownloadItem?CancellationToken.None:sourceRequest?.Token??CancellationToken.None;
        state.LastFailure=null;
        var request=state.Request=CancellationTokenSource.CreateLinkedTokenSource(sourceToken);request.CancelAfter(TimeSpan.FromSeconds(30));var token=request.Token;
        try
        {
            await coverSlots.WaitAsync(token);
            try
            {
                if(closing||closed||!image.IsLoaded||!ReferenceEquals(image.DataContext,item)||CoverUrl(image.DataContext)!=url||CoverViewportFor(image)?.Measure(image).Near!=true)return;
                var cachePath=item is MediaItem or DownloadItem?CoverPath(url):null;
                var (bitmap,downloaded)=await CoverCache.Load(cachePath,1024*1024,
                    ct=>sourceClient.Read(SourceClient.WebUri(url),1024*1024,ct),
                    bytes=>{using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=item is MediaItem or DownloadItem?280:100;result.StreamSource=stream;result.EndInit();result.Freeze();return result;},token);
                token.ThrowIfCancellationRequested();
                if(coverCache.Count>=48)coverCache.Remove(coverCache.Keys.First());coverCache[url]=bitmap;
                if(!closing&&!closed&&image.IsLoaded&&ReferenceEquals(image.DataContext,item)&&CoverUrl(image.DataContext)==url&&ReferenceEquals(state.Request,request)&&CoverViewportFor(image)?.Measure(image).Near==true)image.Source=bitmap;
            }
            finally{coverSlots.Release();}
        }
        catch(Exception error){if(ReferenceEquals(state.Request,request)){state.LastFailure=error;state.RetryAfterUtc=DateTime.UtcNow.AddMinutes(2);}}
        finally{if(ReferenceEquals(state.Request,request))state.Request=null;request.Dispose();}
    }
    static string? CoverUrl(object? item)=>item switch{SourceEntry source=>source.ImageUrl,MediaItem media=>media.ImageUrl,DownloadItem download=>download.ImageUrl,_=>null};
    static string CoverPath(string url)=>Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+".img");
    static void CancelCover(CoverRequest state){var request=state.Request;state.Request=null;request?.Cancel();}
    static void CancelFeatureCover(CoverRequest state){var request=state.FeatureRequest;state.FeatureRequest=null;if(request!=null){state.FeatureAttempted=false;request.Cancel();}}
    CoverViewport? CoverViewportFor(Image image,bool create=false)
    {
        var host=Window.GetWindow(image);if(host==null)return null;
        if(coverViewports.TryGetValue(host,out var viewport))return viewport;
        if(!create||closing||closed)return null;
        viewport=new(host);coverViewports.Add(host,viewport);host.Closed+=CoverHostClosed;
        if(host==this)coverViewport=viewport;
        return viewport;
    }
    void CoverHostClosed(object? sender,EventArgs args)
    {
        if(sender is not Window host)return;host.Closed-=CoverHostClosed;
        if(coverViewports.Remove(host,out var viewport))viewport.Dispose();
        if(host==this)coverViewport=null;
    }
    void StopCoverViewport()
    {
        foreach(var pair in coverViewports.ToArray()){pair.Key.Closed-=CoverHostClosed;pair.Value.Dispose();}
        coverViewports.Clear();coverViewport=null;
    }
    CoverRequest ObserveDownloadCover(Image image)
    {
        var state=coverRequests.GetValue(image,key=>
        {
            var created=new CoverRequest();
            created.Changed=(sender,args)=>
            {
                if((args.PropertyName is null or "" or nameof(DownloadItem.ImageUrl))&&key.IsLoaded&&ReferenceEquals(key.DataContext,sender))SourceCover(key,new RoutedEventArgs());
            };
            key.Unloaded+=(_,_)=>
            {
                if(created.Subject!=null)PropertyChangedEventManager.RemoveHandler(created.Subject,created.Changed!,string.Empty);
                created.Subject=null;CancelCover(created);CancelFeatureCover(created);
            };
            return created;
        });
        var subject=image.DataContext as DownloadItem;
        if(!ReferenceEquals(state.Subject,subject))
        {
            if(state.Subject!=null)PropertyChangedEventManager.RemoveHandler(state.Subject,state.Changed!,string.Empty);
            state.Subject=subject;
            if(subject!=null)PropertyChangedEventManager.AddHandler(subject,state.Changed!,string.Empty);
        }
        return state;
    }
    void SourceCoverChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        var image=(Image)sender;var state=ObserveDownloadCover(image);CancelCover(state);CancelFeatureCover(state);state.Context=null;state.Url=null;state.RetryAfterUtc=default;state.NextCacheTouchUtc=default;state.FeatureAttempted=false;state.RatingsContext=null;image.Source=null;
        if(image.IsLoaded)SourceCover(image,new RoutedEventArgs());
    }
    void AddIndexer()
    {
        var w=new Window{Title="Torznab-источник",Owner=this,Width=560,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new(24)};w.Content=p;
        p.Children.Add(Text("Подключить индексатор",22));p.Children.Add(Text("Вставь Torznab URL из Jackett, Prowlarr или другого совместимого сервиса. Ключ хранится только до закрытия приложения.",12,true));
        TextBox Field(string label){p.Children.Add(Text(label,12));var box=new TextBox{Margin=new(0,0,0,12)};p.Children.Add(box);return box;}
        var name=Field("Название");var endpoint=Field("Torznab URL");p.Children.Add(Text("API-ключ",12));var key=new PasswordBox{Margin=new(0,0,0,15),Padding=new(10)};p.Children.Add(key);var error=Text("",12);
        p.Children.Add(Button("Подключить",()=>{try{SourceClient.WebUri(endpoint.Text.Trim());if(string.IsNullOrWhiteSpace(name.Text))throw new FormatException("Введи название источника.");sourceConfigs.Add(new(name.Text.Trim(),endpoint.Text.Trim(),key.Password));sourceIndex=sourceConfigs.Count;sourceResults=[];w.Close();Render();}catch(Exception ex){error.Text=ex.Message;}}));p.Children.Add(error);w.ShowDialog();
    }
}
