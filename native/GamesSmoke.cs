using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
namespace Kachalka;
public partial class MainWindow
{
    public async Task GamesSmokeTest(string output)
    {
        Directory.CreateDirectory(output);metadataLifetime.Cancel();liveRequest?.Cancel();catalogRefreshTimer.Stop();searchDelay.Stop();
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        async Task Settle(){await Task.Delay(100);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
        var pending=new TaskCompletionSource<GameCatalogPage>(TaskCreationOptions.RunContinuationsAsynchronously);var metadata=new TaskCompletionSource<GameDetails>(TaskCreationOptions.RunContinuationsAsynchronously);var releases=new TaskCompletionSource<IReadOnlyList<SourceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var game=GameCatalog.Item(620,"Portal 2");
        var queries=new List<string>();gameCatalogProvider=(query,page,newest,ct)=>{queries.Add(query);return pending.Task.WaitAsync(ct);};gameDetailProvider=(item,ct)=>metadata.Task.WaitAsync(ct);
        gameSourcesProvider=_=>[new("Fixture PC",ct=>releases.Task.WaitAsync(ct))];
        var nav=Navigation.Children.OfType<Button>().Single(button=>Equals(button.Tag,"Игры"));nav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(section=="Игры"&&SearchBar.IsVisible&&gameLoading&&!IsEnabled.Equals(false),"Games navigation opens its own searchable Windows catalog while the page is pending");
        var beat=0;var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>beat++;timer.Start();await Task.Delay(150);timer.Stop();Check(beat>=3&&Search.IsEnabled,"pending game catalog keeps the WPF input dispatcher responsive");
        pending.SetResult(new([game],false));await Settle();Check(catalogDisplay.Single().Id==game.Id&&catalogList?.ItemTemplate==FindResource("GameRow"),"real catalog list renders the game's landscape card template");
        var button=VisualElements<Button>(Body).First(card=>card.Tag is MediaItem item&&item.Id==game.Id);button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(current?.Section=="Игры"&&gameDescription?.Text.Contains("Загружаем")==true&&gameReleasesLoading,"a game opens its own detail page while descriptions and releases are still pending");
        metadata.SetResult(new(game with{Year=2011,Genre="Приключения",Description="Проверенное описание игры"},"Windows · 2 GB RAM","Valve","Valve"));await Settle();
        Check(gameDescription?.Text=="Проверенное описание игры"&&gameRequirements?.Text.Contains("2 GB RAM")==true&&gameReleasesLoading,"metadata and requirements appear without waiting for torrent sources");
        releases.SetResult([new("pc","Portal 2 (2011) PC","Fixture","","magnet:?xt=urn:btih:"+new string('a',40),null)]);await Settle();Check(gameReleases.Length==1&&!gameReleasesLoading&&VisualElements<Button>(Body).Any(b=>Equals(b.Content,"Скачать")),"matched Windows releases expose the shared download action");
        var old=prefs.LiveFavorites.ToList();var oldIds=new HashSet<int>(prefs.Favorites);
        try{ToggleSaved(current!);Check(SavedItems().Any(item=>item.Section=="Игры"&&item.Id==game.Id),"saved collection includes a game alongside films and series");}
        finally{prefs.LiveFavorites=old;prefs.Favorites=oldIds;prefs.Save();}
        GameBack();await Settle();Search.Text="Portal";SubmitSearch(SearchSubmitButton,new RoutedEventArgs());await Settle();Check(gameQuery=="Portal"&&queries.Last()=="Portal"&&section=="Игры", "game search stays in Games and does not start a film/series search");
        ClearSearch(ClearSearchButton,new RoutedEventArgs());await Settle();Check(gameQuery==""&&Search.Text=="", "clearing game search restores the game's default catalog");
        foreach(var width in new[]{510d,1280d})
        {
            MinWidth=width;Width=width;await Settle();Check(catalogList?.Items.Count>0&&VisualElements<Button>(catalogList).Where(b=>b.Tag is MediaItem).All(b=>b.ActualWidth>0),"game cards remain visible at window width "+width);
        }
        var blocked=new TaskCompletionSource<GameCatalogPage>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken gameToken=default;
        gameCatalogProvider=(_,_,_,ct)=>{gameToken=ct;return blocked.Task.WaitAsync(ct);};gameKey="";Render();await Settle();ShowCatalogSection("Фильмы");await Settle();Check(gameToken.IsCancellationRequested,"leaving Games cancels its unfinished catalog request");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,WindowsGames=true,SeparateSearch=true,ResponsiveInput=true,DownloadAction=true,SavedGames=true}));Close();
    }
}
