using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;

public partial class MainWindow
{
    public async Task CacheSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();await Task.Delay(300);
        var queuePath=Path.Combine(Preferences.DataDir,"queue.json");var settingsPath=Path.Combine(Preferences.DataDir,"settings.json");
        if(!File.Exists(queuePath))await File.WriteAllTextAsync(queuePath,"[]");prefs.Save();
        var queue=await File.ReadAllBytesAsync(queuePath);var settings=await File.ReadAllBytesAsync(settingsPath);
        var resumePath=Path.Combine(Preferences.DataDir,"cache","cache-smoke.resume");Directory.CreateDirectory(Path.GetDirectoryName(resumePath)!);await File.WriteAllTextAsync(resumePath,"active-transfer-state");
        var imagePath=Path.Combine(Preferences.DataDir,"covers","cache-smoke.img");await CacheFiles.WriteAllBytesAsync(imagePath,new byte[4096]);
        var detailsPath=Path.Combine(Preferences.DataDir,"details","cache-smoke.json");await CacheFiles.WriteAllTextAsync(detailsPath,"{}");
        section="Настройки";current=null;Render();await Task.Delay(150);
        var clear=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Очистить кэш")??throw new Exception("Cache settings action missing.");
        clear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while((!clear.IsEnabled||FindVisual<TextBlock>(Body,x=>x.Text.StartsWith("Освобождено "))==null)&&DateTime.UtcNow<deadline)await Task.Delay(100);
        if(File.Exists(imagePath)||File.Exists(detailsPath)||FindVisual<TextBlock>(Body,x=>x.Text.StartsWith("Освобождено "))==null)throw new Exception("Cache settings action failed to clear catalog data and report freed space.");
        if(!(await File.ReadAllBytesAsync(queuePath)).SequenceEqual(queue)||!(await File.ReadAllBytesAsync(settingsPath)).SequenceEqual(settings)||await File.ReadAllTextAsync(resumePath)!="active-transfer-state")throw new Exception("Cache settings action modified transfer state or preferences.");
        clear.BringIntoView();await Task.Delay(100);UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,"cache-settings.png")))encoder.Save(stream);
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{ManualCleanup=true,FreedSpace=true,PreferencesPreserved=true,QueuePreserved=true,TransferStatePreserved=true}));Close();
    }
}
