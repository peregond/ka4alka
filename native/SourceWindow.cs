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
    sealed class CoverRequest
    {
        public DownloadItem? Subject;
        public EventHandler<PropertyChangedEventArgs>? Changed;
        public object? Context;
        public string? Url;
        public CancellationTokenSource? Request;
        public DateTime RetryAfterUtc;
    }
    int savedCovers;
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
        var button=(Button)sender;var item=(SourceEntry)button.Tag;var media=current?.Cinema==true?current:null;
        if(media!=null)media=DownloadMetadata.EnrichMedia(media,prefs.LiveFavorites.Concat(liveItems).Concat(catalogIndex.Recent(media.Section,200)).Append(media));
        button.IsEnabled=false;
        try{if(!EnsureDownloadFolder()){Status.Text="Папка для загрузок не выбрана. Её можно выбрать в настройках.";return;}
            Status.Text="Проверяем раздачу…";using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var source=await sourceClient.TorrentFile(item,timeout.Token);
            if(media!=null&&cardMetadata.TryGetValue(media.Id,out var metadata)&&metadata.IsCompletedSuccessfully)media=DownloadMetadata.EnrichMedia(media,[metadata.Result]);
            await downloads.Add(source,prefs.Folder,media,item.ImageUrl,item);section="Загрузки";current=null;Render();Status.Text=item.Seeds==0?"Раздача добавлена, но источник показывает 0 сидов. Ждём участников.":"Раздача добавлена. Ищем участников.";
        }catch(Exception error){var message=error is HttpRequestException?"Не удалось получить раздачу из источника.":error.Message;Status.Text=message;MessageBox.Show(this,message,"Не удалось начать загрузку",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{button.IsEnabled=true;}
    }
    void SourcePage(object sender,RoutedEventArgs e){var item=(SourceEntry)((Button)sender).Tag;if(string.IsNullOrEmpty(item.PageUrl))return;System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SourceClient.WebUri(item.PageUrl).AbsoluteUri){UseShellExecute=true});}
    async void SourceCover(object sender,RoutedEventArgs e)
    {
        var image=(Image)sender;var state=ObserveDownloadCover(image);var item=image.DataContext;var url=CoverUrl(item);
        if(item is MediaItem card&&card.IsLive)_=UpdateCardRatings(card);
        if(string.IsNullOrWhiteSpace(url))
        {
            CancelCover(state);state.Context=item;state.Url=null;state.RetryAfterUtc=default;image.Source=null;return;
        }
        var same=ReferenceEquals(state.Context,item)&&state.Url==url;
        if(same&&(state.Request!=null||image.Source!=null||DateTime.UtcNow<state.RetryAfterUtc))return;
        CancelCover(state);state.Context=item;state.Url=url;state.RetryAfterUtc=default;
        if(!same)image.Source=null;
        if(coverCache.TryGetValue(url,out var cached)){image.Source=cached;return;}
        var sourceToken=item is MediaItem or DownloadItem?CancellationToken.None:sourceRequest?.Token??CancellationToken.None;
        var request=state.Request=CancellationTokenSource.CreateLinkedTokenSource(sourceToken);request.CancelAfter(TimeSpan.FromSeconds(30));var token=request.Token;
        try
        {
            await coverSlots.WaitAsync(token);
            try
            {
                if(!image.IsLoaded||!ReferenceEquals(image.DataContext,item)||CoverUrl(image.DataContext)!=url)return;
                var cachePath=item is MediaItem or DownloadItem?Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+".img"):null;
                var (bitmap,downloaded)=await CoverCache.Load(cachePath,1024*1024,
                    ct=>sourceClient.Read(SourceClient.WebUri(url),1024*1024,ct),
                    bytes=>{using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=item is MediaItem or DownloadItem?280:100;result.StreamSource=stream;result.EndInit();result.Freeze();return result;},token);
                if(coverCache.Count>=48)coverCache.Remove(coverCache.Keys.First());coverCache[url]=bitmap;
                if(image.IsLoaded&&ReferenceEquals(image.DataContext,item)&&CoverUrl(image.DataContext)==url&&ReferenceEquals(state.Request,request))image.Source=bitmap;
                if(downloaded&&cachePath!=null)try{if(++savedCovers%20==0)foreach(var old in new DirectoryInfo(Path.GetDirectoryName(cachePath)!).GetFiles("*.img").OrderByDescending(x=>x.LastWriteTimeUtc).Skip(200))old.Delete();}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            finally{coverSlots.Release();}
        }
        catch{if(ReferenceEquals(state.Request,request))state.RetryAfterUtc=DateTime.UtcNow.AddMinutes(2);}
        finally{if(ReferenceEquals(state.Request,request))state.Request=null;request.Dispose();}
    }
    static string? CoverUrl(object? item)=>item switch{SourceEntry source=>source.ImageUrl,MediaItem media=>media.ImageUrl,DownloadItem download=>download.ImageUrl,_=>null};
    static void CancelCover(CoverRequest state){var request=state.Request;state.Request=null;request?.Cancel();}
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
                created.Subject=null;CancelCover(created);
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
        var image=(Image)sender;var state=ObserveDownloadCover(image);CancelCover(state);state.Context=null;state.Url=null;state.RetryAfterUtc=default;image.Source=null;
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
