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
        var original=new MediaItem(-70001,"Первый проверочный фильм","Фильмы","Драма",2020,"8.2","7.8","#526B69"){PageUrl=LiveCatalog.Base+"/movies/cinema-smoke-first",Description="Описание фильма",People=[person]};
        var next=original with{Id=-70002,Title="Второй проверочный фильм",PageUrl=LiveCatalog.Base+"/movies/cinema-smoke-second",Year=2022,Kinopoisk="9.1"};
        var biography="Биография участника для проверки интерфейса. "+new string('а',400);
        var folder=Path.Combine(Preferences.DataDir,"people");Directory.CreateDirectory(folder);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(person.Name+"|"+person.Role)));
        await File.WriteAllTextAsync(Path.Combine(folder,key+".json"),JsonSerializer.Serialize(new PersonProfile(person,biography,[original,next])));
        requestedDetails.Add(original.Id);requestedDetails.Add(next.Id);liveReleases[original.Id]=[];liveReleases[next.Id]=[];
        section="Фильмы";current=original;Render();UpdateLayout();
        var open=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Открыть карточку: "+person.Name+", "+person.Role)??throw new Exception("Person action missing.");
        Exception? failure=null;var checkedDialog=false;var limit=DateTime.UtcNow.AddSeconds(15);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>
        {
            var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(x=>x.Owner==this&&x.Title==person.Name);
            try
            {
                if(DateTime.UtcNow>limit)throw new Exception("Person dialog did not load cached biography.");
                if(dialog==null||FindVisual<TextBlock>(dialog,x=>x.Text==biography)==null)return;
                void Shot(string name)
                {
                    dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
                }
                Shot("person-wide");dialog.Width=390;dialog.Height=540;Shot("person-narrow");
                var film=FindVisual<Button>(dialog,x=>AutomationProperties.GetName(x)=="Открыть "+next.Title)??throw new Exception("Filmography action missing.");
                checkedDialog=true;timer.Stop();film.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }
            catch(Exception error){failure=error;timer.Stop();dialog?.Close();}
        };
        timer.Start();open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));timer.Stop();
        if(failure!=null)throw failure;
        if(!checkedDialog||current?.Id!=next.Id)throw new Exception("Film → person → film UI navigation failed.");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{PersonCard=true,Biography=true,Filmography=true,WideAndNarrow=true,Navigation=true}));Close();
    }
}
