using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kachalka;

public partial class MainWindow
{
    public async Task DownloadReliabilitySmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        var originalNotify=prefs.NotifyDownloads;var originalRecover=prefs.AutoRecoverDownloads;var originalLight=prefs.Light;
        var checks=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        void Shot(string name)
        {
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
        }
        try
        {
            section="Настройки";current=null;Render();UpdateLayout();
            var notify=FindVisual<CheckBox>(Body,b=>b.Name=="SettingsNotifyDownloads")??throw new Exception("Notification switch missing.");
            notify.BringIntoView();await Task.Delay(150);
            await ClickWithMouse(notify);
            Check(prefs.NotifyDownloads!=originalNotify&&Preferences.Load().NotifyDownloads==prefs.NotifyDownloads,"Native notification switch persists its preference.");
            await ClickWithMouse(notify);
            Check(prefs.NotifyDownloads==originalNotify,"Notification switch can be restored.");
            var recover=FindVisual<CheckBox>(Body,b=>b.Name=="SettingsAutoRecover")??throw new Exception("Recovery switch missing.");
            recover.BringIntoView();await Task.Delay(150);await ClickWithMouse(recover);
            Check(prefs.AutoRecoverDownloads!=originalRecover&&Preferences.Load().AutoRecoverDownloads==prefs.AutoRecoverDownloads,"Native recovery switch persists separately from startup resume.");
            await ClickWithMouse(recover);Check(prefs.AutoRecoverDownloads==originalRecover,"Recovery switch can be restored.");
            foreach(var light in new[]{false,true})
            {
                prefs.Light=light;ApplyTheme();Render();UpdateLayout();
                var field=FindVisual<CheckBox>(Body,b=>b.Name=="SettingsNotifyDownloads")!;field.BringIntoView();await Task.Delay(150);
                Shot("notification-settings-"+(light?"light":"dark"));
            }
            using(var channel=new WindowsNotifications(this,()=>ShowDownloads(this,new RoutedEventArgs()),()=>true))
            {
                Check(channel.Test()&&channel.LastDelivery==NotificationDelivery.Submitted,"Actual Windows Shell accepted a notification with the application icon (visibility is controlled by Windows).");
                Check(channel.HasPendingNotification,"Native notification has a temporary callback icon.");
                channel.Dispose();Check(!channel.HasPendingNotification,"Disposing the notification removes its temporary icon and hooks.");
            }
            using(var disabled=new WindowsNotifications(this,()=>throw new Exception("Disabled channel activated."),()=>false))
                Check(!disabled.Test()&&disabled.LastDelivery==NotificationDelivery.Disabled&&!disabled.HasPendingNotification,"Disabled notifications do not create a native icon or submit a banner.");
            var signals=0;var forced=false;
            using(var monitor=new DownloadRecoveryMonitor(Dispatcher,force=>{signals++;forced=force;}))
            {
                var schedule=typeof(DownloadRecoveryMonitor).GetMethod("Schedule",BindingFlags.Instance|BindingFlags.NonPublic)!;
                schedule.Invoke(monitor,[false]);schedule.Invoke(monitor,[true]);schedule.Invoke(monitor,[false]);
                var deadline=DateTime.UtcNow.AddSeconds(4);
                while(signals==0&&DateTime.UtcNow<deadline)await Task.Delay(100);
                Check(signals==1&&forced,"Adapter event bursts coalesce while preserving a forced reconnect request.");
                schedule.Invoke(monitor,[true]);monitor.Dispose();await Task.Delay(2200);
                Check(signals==1,"Disposing the network monitor cancels queued callbacks and its timer.");
            }
            await CheckDownloadDiagnostics(output);
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,DiagnosticDialog=true,WindowsNotificationAccepted=true,VisibilityVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());throw;}
        finally
        {
            prefs.NotifyDownloads=originalNotify;prefs.AutoRecoverDownloads=originalRecover;prefs.Light=originalLight;prefs.Save();ApplyTheme();Close();
        }
    }
}
