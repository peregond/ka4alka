using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    public async Task CinemaSmokeTest(string output)
    {
        Directory.CreateDirectory(output);await Task.Delay(300);
        liveRequest?.Cancel();searchDelay.Stop();
        var person=new CinemaPerson("Участник UI-проверки","Актёры","");
        var original=new MediaItem(-70001,"Первый проверочный фильм","Фильмы","Драма",2020,"8.2","7.8","#526B69"){PageUrl=LiveCatalog.Base+"/movies/cinema-smoke-first",Description="Описание фильма",People=[person,new("Участник второй","Актёры",""),new("Участник третий","Актёры","")]};
        var next=original with{Id=-70002,Title="Второй проверочный фильм",PageUrl=LiveCatalog.Base+"/movies/cinema-smoke-second",Year=2022,Kinopoisk="9.1"};
        var biography="Биография участника для проверки интерфейса. "+new string('а',400);
        var folder=Path.Combine(Preferences.DataDir,"people");Directory.CreateDirectory(folder);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(person.Name+"|"+person.Role)));
        await File.WriteAllTextAsync(Path.Combine(folder,key+".json"),JsonSerializer.Serialize(new PersonProfile(person,biography,[original,next])));
        var portraitUrl="https://upload.wikimedia.org/wikipedia/commons/cinema-smoke.png";
        var portraitKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(person.Name)));
        await File.WriteAllTextAsync(Path.Combine(folder,portraitKey+".portrait.json"),JsonSerializer.Serialize(portraitUrl));
        var portraitDir=Path.Combine(Preferences.DataDir,"portraits");Directory.CreateDirectory(portraitDir);
        var portrait=new RenderTargetBitmap(20,30,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(Brushes.Teal,null,new Rect(0,0,20,30));portrait.Render(drawing);
        var portraitEncoder=new PngBitmapEncoder();portraitEncoder.Frames.Add(BitmapFrame.Create(portrait));using(var stream=File.Create(Path.Combine(portraitDir,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(portraitUrl)))+".img")))portraitEncoder.Save(stream);
        requestedDetails.Add(original.Id);requestedDetails.Add(next.Id);liveReleases[original.Id]=[];liveReleases[next.Id]=[];
        section="Фильмы";current=original;Render();UpdateLayout();
        var open=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Открыть карточку: "+person.Name+", "+person.Role)??throw new Exception("Person action missing.");
        open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var limit=DateTime.UtcNow.AddSeconds(15);
        while((FindVisual<TextBlock>(Body,x=>x.Text==biography)==null||FindVisual<Image>(Body,x=>x.Source!=null)==null)&&DateTime.UtcNow<limit)await Task.Delay(100);
        if(FindVisual<TextBlock>(Body,x=>x.Text==biography)==null)throw new Exception("Person page did not load cached biography.");
        if(Application.Current.Windows.OfType<Window>().Any(x=>x.Owner==this&&x.Title==person.Name))throw new Exception("Person navigation opened a popup.");
        void Shot(string name)
        {
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
        }
        if(FindVisual<Image>(Body,x=>x.Source!=null)==null)throw new Exception("Cached person photograph missing.");
        designFixedViewport=true;MaxWidth=2000;MaxHeight=1200;MinWidth=1440;Width=1440;Height=900;await Task.Delay(150);Shot("person-wide");MinWidth=760;Width=760;Height=720;await Task.Delay(150);Shot("person-narrow");
        var film=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Открыть "+next.Title)??throw new Exception("Filmography action missing.");
        film.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(current?.Id!=next.Id||activePerson!=null)throw new Exception("Film → person → film navigation failed.");
        CinemaBack();if(activePerson!=person)throw new Exception("Movie back did not restore person page.");
        var back=FindVisual<Button>(PageHeader,x=>AutomationProperties.GetName(x).Contains("Назад к фильму")||FindVisual<TextBlock>(x,t=>t.Text.StartsWith("Назад к фильму"))!=null)??throw new Exception("Person back action missing.");
        back.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(activePerson!=null||current?.Id!=original.Id)throw new Exception("Person back did not restore original movie.");
        MinWidth=1600;Width=1600;await Task.Delay(150);UpdateLayout();Shot("film-wide");
        var cards=FindVisual<Grid>(Body,x=>x.Name=="CinemaCards")??throw new Exception("Film cards missing.");
        var participants=FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants")??throw new Exception("Participants card missing.");
        var crew=FindVisual<System.Windows.Controls.Primitives.UniformGrid>(participants,x=>x.Name=="CinemaPeopleGrid")??throw new Exception("Crew portrait grid missing.");
        if(crew.Columns!=3)throw new Exception("Wide participant card does not arrange three portraits per row.");
        var title=FindVisual<TextBlock>(Body,x=>x.Name=="DetailTitle")!;var favorite=FindVisual<Button>(Body,x=>x.Name=="DetailFavorite")!;
        if(favorite.TransformToAncestor(Body).Transform(new Point()).Y<title.TransformToAncestor(Body).Transform(new Point()).Y+title.ActualHeight)throw new Exception("Save action must follow the movie title.");
        var filmCard=FindVisual<Border>(Body,x=>x.Name=="CinemaFilm")!;var hero=detailHero!;
                var ratings=FindVisual<WrapPanel>(filmCard,x=>x.Name=="DetailRatings")!;var year=FindVisual<TextBlock>(filmCard,x=>x.Name=="DetailYear")!;
        if(Grid.GetRow(ratings)!=0||Grid.GetColumn(ratings)!=1)throw new Exception("Wide ratings should share the type/year row.");
        if(filmCard.ActualHeight-hero.ActualHeight>44)throw new Exception("Movie card leaves excess blank space below its content.");
        if(Grid.GetColumn(participants)!=1)throw new Exception($"Wide layout did not place participants next to the movie: window={ActualWidth}, cards={cards.ActualWidth}.");
        MinWidth=760;Width=760;await Task.Delay(150);UpdateLayout();Shot("film-narrow");
        if(Grid.GetRow(participants)!=1||Grid.GetColumn(participants)!=0)throw new Exception("Narrow layout did not stack participants.");
        MinWidth=620;Width=620;await Task.Delay(150);UpdateLayout();Shot("film-minimum");
        foreach(var element in VisualElements<FrameworkElement>(Body).Where(x=>x.IsVisible&&(x.Name is "DetailTitle" or "DetailRatings" or "DetailFavorite" or "DetailSynopsis")))
        {
            var bounds=element.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),element.RenderSize));if(bounds.Right>Body.ActualWidth+1||bounds.Left<-.5)throw new Exception("Minimum-width detail element overflows: "+element.Name);
        }
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{PersonCard=true,Biography=true,Filmography=true,WideAndNarrow=true,Navigation=true}));Close();
    }
}
