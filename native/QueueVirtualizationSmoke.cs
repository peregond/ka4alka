using System.IO;
using System.Text.Json;
namespace Kachalka;

public partial class MainWindow
{
    // Keep the queue fixture separate from paging, search, Saved and screenshot
    // checks, so its time budget measures the queue itself on slower runners.
    public async Task QueueVirtualizationSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();ResetDiscoveryData();searchDelay.Stop();catalogRefreshTimer.Stop();
        var original=prefs.UiLanguage;var checks=new List<string>();
        MaxWidth=1800;MaxHeight=1000;MinWidth=1280;MinHeight=300;
        try
        {
            foreach(var language in new[]{"ru","en"})
            {
                AppLanguage.Set(language);Directory.CreateDirectory(Path.Combine(output,language));
                await CheckModernQueueVirtualization(Path.Combine(output,language),(valid,message)=>
                {
                    if(!valid)throw new Exception(language+": "+message);
                    checks.Add(language+": "+message);
                });
            }
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,ItemsPerLanguage=200,Languages=new[]{"ru","en"}},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());throw;}
        finally{AppLanguage.Set(original);Close();}
    }
}
