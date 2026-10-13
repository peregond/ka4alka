using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Kachalka;

public partial class MainWindow
{
    public async Task LanguageSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();catalogRefreshTimer.Stop();searchDelay.Stop();
        var original=prefs.UiLanguage;var checks=new List<string>();
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks.Add(message);}
        async Task Settle(){await Task.Delay(100);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);UpdateLayout();}
        void Shot(string name){var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        try
        {
            MaxWidth=1800;MaxHeight=1000;MinWidth=1400;MinHeight=800;Width=1400;Height=800;
            PrepareDesignSmoke();liveKey=CurrentCatalogKey;
            section="Настройки";current=null;Render();await Settle();
            var selector=FindVisual<ComboBox>(Body,box=>box.Name=="SettingsLanguage")??throw new Exception("Language preference is missing.");
            selector.SelectedIndex=1;await Settle();
            Check(AppLanguage.Current=="en"&&Preferences.Load().UiLanguage=="en","Choosing English persists the preference and switches immediately.");
            foreach(var name in new[]{"Movies","Series","Games","Downloads","Settings","Saved"})
                Check(VisualElements<TextBlock>(SidePanel).Any(text=>text.Text==name),"Sidebar translates: "+name);
            Check(VisualElements<TextBlock>(Body).Any(text=>text.Text=="Interface language"),"Settings expose the localized language label.");
            Check(VisualElements<TextBlock>(Body).Any(text=>text.Text=="Appearance"),"Appearance section translates without rebuilding preferences.");
            Shot("settings-english");
            section="Фильмы";current=null;liveKey=CurrentCatalogKey;Render();await Settle();
            Check(VisualElements<TextBlock>(Body).Any(text=>text.Text=="Popular"),"Movie shelves translate in English.");
            Check(VisualElements<TextBlock>(RootGrid).Any(text=>text.Text=="Genre"),"Catalog filters translate while preserving their canonical selection values.");
            Shot("catalog-english");
            section="Сериалы";liveKey=CurrentCatalogKey;Render();await Settle();
            Check(VisualElements<TextBlock>(Body).Any(text=>text.Text=="New series"),"Series shelves use the same English presentation.");
            Shot("series-english");
            var item=new DownloadItem{Name="Фильмы",Paused=true,Progress=100,HasCompletedDownload=true,FilesMissing=true,Status="Файлы не найдены",Hint="Повторная загрузка не запускается автоматически",MissingFilesMessage="Повторная загрузка не запускается автоматически"};
            downloads.Items.Add(item);section="Загрузки";Render();await Settle();
            Check(VisualElements<TextBlock>(Body).Any(text=>text.Text=="Files not found"),"Bound download status is translated without changing its canonical state.");
            Check(item.Status=="Файлы не найдены"&&VisualElements<TextBlock>(Body).Any(text=>text.Text=="Фильмы"),"The original torrent name and stored status remain intact.");
            Check(VisualElements<Button>(Body).Any(button=>Equals(button.Content,"Download again"))&&VisualElements<Button>(Body).Any(button=>Equals(button.Content,"Remove from downloads")),"Missing-file decisions are available in English.");
            Shot("downloads-english");
            foreach(var width in new[]{360d,510d,720d})
            {
                MinWidth=width;Width=width;Height=600;await Settle();
                Check(Body.ActualWidth>0&&Body.ActualHeight>40,"English controls preserve a usable viewport at "+width+" DIP.");
                Shot("downloads-english-"+(int)width);
            }
            var dialog=LocalizedDialog.Create(this,"Файлы загрузки «Фильмы» не найдены. Скачать недостающие файлы заново?","Повторное скачивание",MessageBoxButton.YesNo,MessageBoxResult.No,out var answer);
            dialog.Show();await Settle();
            Check(VisualElements<Button>(dialog).Any(button=>Equals(button.Content,"Yes"))&&VisualElements<Button>(dialog).Any(button=>Equals(button.Content,"No")&&button.IsDefault),"English confirmation buttons honor the safe default No even on Russian Windows.");
            Click(VisualElements<Button>(dialog).Single(button=>Equals(button.Content,"No")));Check(answer()==MessageBoxResult.No,"English confirmation returns the same decision enum as native dialogs.");
            await downloads.Remove(item);
            section="Настройки";Render();await Settle();selector=FindVisual<ComboBox>(Body,box=>box.Name=="SettingsLanguage")!;selector.SelectedIndex=0;await Settle();
            Check(AppLanguage.Current=="ru"&&Preferences.Load().UiLanguage=="ru"&&VisualElements<TextBlock>(Body).Any(text=>text.Text=="Внешний вид"),"Switching back restores Russian and saves the choice.");
            Check(section=="Настройки","Changing language never rewrites navigation section identifiers.");
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,ImmediateSwitch=true,Persists=true,CanonicalDataPreserved=true,Viewports=new[]{360,510,720}},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());throw;}
        finally{prefs.UiLanguage=original;prefs.Save();AppLanguage.Set(original);Close();}
    }
}
