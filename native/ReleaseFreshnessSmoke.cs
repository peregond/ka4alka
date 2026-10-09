using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    public async Task ReleaseFreshnessSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        liveRequest?.Cancel();liveLoading=false;searchDelay.Stop();Search.Text="";submittedQuery="";
        var originalLight=prefs.Light;var checks=new List<object>();
        var now=DateTime.UtcNow;
        var movie=new MediaItem(-88038,"Данные раздач","Фильмы","драма",2026,"8.1","7.9","#526B69")
            {PageUrl="https://example.invalid/freshness-film",Description="Возраст данных помогает выбрать вариант. Получение ответа источника само по себе не подтверждает подключение к участникам раздачи."};
        var direct=new SourceEntry("freshness-ui-direct","Данные раздач (2026) WEB-DL 1080p RUS DUB","RuTor","https://example.invalid/private/path?passkey=source-secret","magnet:?xt=urn:btih:"+new string('a',40),null,4_000_000_000,342)
            {DataReceivedUtc=now.AddMinutes(-7),DataProvider="RuTor",Leechers=12};
        var indexed=direct with{Id="freshness-ui-indexed",Source="The Pirate Bay",Title="Freshness Film 1080p ENG subs RUS",TorrentUrl="magnet:?xt=urn:btih:"+new string('b',40),DataReceivedUtc=now,DataProvider="Онлайн-индекс"};
        var legacy=direct with{Id="freshness-ui-legacy",Source="NNM-Club",Title="Данные раздач (2026) WEB-DL 1080p",TorrentUrl="magnet:?xt=urn:btih:"+new string('c',40),DataReceivedUtc=null,DataProvider=null,Seeds=null,Leechers=null};
        requestedDetails.Add(movie.Id);cardMetadata[movie.Id]=Task.FromResult(movie);
        liveReleases[movie.Id]=[direct,indexed,legacy];
        releaseViews[movie.Id]=new(){Saved=true,ReceivedUtc=now,Sources=[
            new("RuTor",SourceState.Ready,1,direct.DataReceivedUtc,direct.DataReceivedUtc),
            new("The Pirate Bay",SourceState.Unavailable,CheckedUtc:now,LastSuccessUtc:now.AddHours(-3)),
            new("Онлайн-индекс",SourceState.Ready,1,now,now),
            new("NNM-Club",SourceState.Saved,1)
        ]};
        current=movie;activePerson=null;section="Фильмы";favoritesOnly=false;
        MaxWidth=1800;MaxHeight=1000;MinHeight=760;
        async Task Settle(){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(70);UpdateLayout();}
        void Check(bool good,string message){if(!good)throw new Exception(message);}
        void Shot(string name)
        {
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var image=File.Create(Path.Combine(output,name+".png"));encoder.Save(image);
        }
        try
        {
            foreach(var (light,width,name) in new[]{(false,1500d,"release-freshness-dark"),(true,900d,"release-freshness-light-compact")})
            {
                prefs.Light=light;ApplyTheme();MinWidth=width;Width=width;Height=760;Render();await Settle();
                var texts=VisualElements<TextBlock>(Body).ToArray();var misses=new List<string>();
                void Expect(bool good,string message){if(!good)misses.Add(message);}
                Expect(texts.Any(x=>x.Text==ReleaseFreshness.Caption(direct,DateTime.UtcNow))||(texts.Any(x=>x.Text==ReleaseFreshness.Age(direct.DataReceivedUtc,DateTime.UtcNow))&&texts.Any(x=>x.Text==ReleaseFreshness.SourceName(direct))),name+": direct source receipt age is missing.");
                Expect(texts.Any(x=>x.Text==ReleaseFreshness.Caption(indexed,DateTime.UtcNow))||(texts.Any(x=>x.Text==ReleaseFreshness.Age(indexed.DataReceivedUtc,DateTime.UtcNow))&&texts.Any(x=>x.Text==ReleaseFreshness.SourceName(indexed)+" · индекс")),name+": index provenance is missing.");
                Expect(texts.Any(x=>x.Text.EndsWith("время неизвестно",StringComparison.Ordinal)),name+": legacy cache date was invented.");
                Expect(texts.Any(x=>x.Text=="Русская · дубляж"||x.Text=="Озвучка: Русская · дубляж"),name+": Russian audio is hidden.");
                Expect(VisualElements<FrameworkElement>(Body).Any(x=>x.ToolTip is string tip&&tip.Contains(ReleaseFreshness.ConnectionNote)),name+": advertised participants are presented as confirmed peers.");
                Expect(!texts.Any(x=>x.Text.Contains("source-secret")||x.Text.Contains("passkey=")),name+": source URL leaked into displayed status.");
                if(misses.Count>0)Shot(name+"-initial");Check(misses.Count==0,string.Join(" | ",misses));
                var toggle=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Показать состояние источников")!;
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
                var sourcePanel=FindVisual<StackPanel>(Body,x=>x.Name=="ReleaseSourceDetails")!;
                Check(sourcePanel.Visibility==Visibility.Visible&&VisualElements<TextBlock>(sourcePanel).Any(x=>x.Text.Contains("успешный ответ 3 ч назад")),name+": failed refresh lost its earlier successful reply age.");
                var toolbarCheck=CheckReleaseToolbar(name,false,true);
                var first=FindVisual<Button>(Body,x=>x.Tag is SourceEntry entry&&entry.Id==direct.Id)!;first.BringIntoView();await Settle();Shot(name);
                checks.Add(new{Stage=name,DirectReceiptAge=true,IndexProvenance=true,LegacyDateUnknown=true,RussianAudioVisible=true,ParticipantsUnconfirmed=true,FailedRefreshKeepsPriorReceipt=true,Toolbar=toolbarCheck});
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
                Check(sourcePanel.Visibility==Visibility.Collapsed,name+": repeated click did not collapse the source details.");
            }
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());throw;}
        finally{prefs.Light=originalLight;ApplyTheme();Close();}
    }
}
