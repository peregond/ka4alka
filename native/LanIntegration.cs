using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Kachalka.Lan;

namespace Kachalka;

public partial class MainWindow
{
    LanService? lanService;
    readonly SemaphoreSlim lanLifecycleGate=new(1,1);
    readonly CancellationTokenSource lanLifetime=new();
    (Guid DeviceId,string ReleaseKey,LanDownloadRequest Request)? pendingLanDownload;
    bool lanSending;
    string LanDeviceDisplayName=>string.IsNullOrWhiteSpace(prefs.LanDeviceName)?DeviceDisplayName.Default():prefs.LanDeviceName;

    void InitializeLanDevices()
    {
        if(string.IsNullOrWhiteSpace(prefs.LanDeviceName))prefs.LanDeviceName=DeviceDisplayName.Default();
        bool started=false;
        ContentRendered+=async(_,_)=>
        {
            if(started||!prefs.LanEnabled)return;started=true;
            try{await SetLanEnabledAsync(true);}
            catch(Exception error)when(!closing&&!closed){Status.Text="Устройства в сети недоступны: "+error.Message;DiagnosticLog.Write("lan-start-failed",new{Error=error.GetType().Name});}
            catch(OperationCanceledException)when(closing||closed){}
        };
    }

    async Task<LanService> CreateLanServiceAsync()
    {
        var name=LanDeviceDisplayName;
        var service=await Task.Run(()=>new LanService(new(){StateDirectory=Path.Combine(Preferences.DataDir,"lan"),DeviceName=name}),lanLifetime.Token);
        service.ConfirmPairingAsync=(request,token)=>
        {
            if(closing||closed||Dispatcher.HasShutdownStarted)return Task.FromResult(false);
            return Dispatcher.InvokeAsync(()=>ConfirmLanPairingAsync(request,token)).Task.Unwrap();
        };
        service.ReceiveDownloadAsync=(request,token)=>
        {
            if(closing||closed||Dispatcher.HasShutdownStarted)return Task.FromResult(new LanDownloadReceipt{RequestId=request.RequestId,Message="Приложение закрывается. Повтори отправку после запуска."});
            return Dispatcher.InvokeAsync(()=>ReceiveLanDownloadAsync(request,token)).Task.Unwrap();
        };
        try{await service.StartAsync(lanLifetime.Token);lanLifetime.Token.ThrowIfCancellationRequested();return service;}
        catch{await service.DisposeAsync();throw;}
    }

    async Task SetLanEnabledAsync(bool enabled)
    {
        await lanLifecycleGate.WaitAsync(lanLifetime.Token);
        var previous=prefs.LanEnabled;
        try
        {
            if(closing||closed)throw new OperationCanceledException(lanLifetime.Token);
            if(enabled)
            {
                if(lanService?.IsRunning!=true)lanService=await CreateLanServiceAsync();
            }
            else
            {
                CloseLanDialogs();pendingLanDownload=null;
                var service=lanService;lanService=null;
                if(service!=null)await service.DisposeAsync();
            }
            if(closing||closed)throw new OperationCanceledException(lanLifetime.Token);
            prefs.LanEnabled=enabled;prefs.Save();
            DiagnosticLog.Write("lan-enabled-changed",new{Enabled=enabled});
        }
        catch
        {
            prefs.LanEnabled=previous;
            if(enabled&&!previous)
            {
                var service=lanService;lanService=null;if(service!=null)await service.DisposeAsync();
            }
            else if(!enabled&&previous&&!closing&&!closed&&lanService==null)lanService=await CreateLanServiceAsync();
            throw;
        }
        finally{lanLifecycleGate.Release();}
    }

    async Task RenameLanDeviceAsync(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>60||value.Any(char.IsControl))throw new ArgumentException("Укажи имя устройства от 1 до 60 символов.");
        var name=value.Trim();var previous=prefs.LanDeviceName;
        if(lanService is {} service)await Task.Run(()=>service.Name=name,lanLifetime.Token);
        try{prefs.LanDeviceName=name;prefs.Save();}
        catch{prefs.LanDeviceName=previous;if(lanService is {} rollback)await Task.Run(()=>rollback.Name=previous);throw;}
    }

    async Task StopLanDevicesAsync()
    {
        lanLifetime.Cancel();CloseLanDialogs();pendingLanDownload=null;
        var service=lanService;lanService=null;
        if(service==null)return;
        try{await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));}
        catch(Exception error){DiagnosticLog.Write("lan-stop",new{Error=error.GetType().Name});}
    }

    async Task<LanDownloadReceipt> ReceiveLanDownloadAsync(LanDownloadRequest request,CancellationToken token)
    {
        if(closing||closed||!prefs.LanEnabled)return new(){RequestId=request.RequestId,Message="Приём загрузок выключен на этом устройстве."};
        var receiver=new LanDownloadReceiver(downloads,prefs,Path.Combine(Preferences.DataDir,"lan","inbox"));
        var receipt=await receiver.ReceiveAsync(request,token);
        if(!closing&&!closed&&receipt.Accepted)
        {
            SyncTimer();if(section=="Загрузки")Render();
            Status.Text="Получена загрузка из локальной сети. "+receipt.Message;
        }
        return receipt;
    }

    FrameworkElement LanDownloadAction(SourceEntry entry)
    {
        var local=new Button{Content="Скачать",Tag=entry,Style=(Style)FindResource("PrimaryButton"),Margin=new(0),Padding=new(12,9,12,9),ToolTip="Скачать на этом компьютере"};
        AutomationProperties.SetName(local,"Скачать раздачу: "+entry.Title);local.Click+=SourceDownload;
        var remote=ActionButton("","IconDevice",()=>{});remote.Margin=new(4,0,0,0);remote.Padding=new(8);remote.Width=36;remote.MinHeight=36;remote.ToolTip="Скачать на другом устройстве";
        AutomationProperties.SetName(remote,"Скачать на другом устройстве: "+entry.Title);
        remote.Click+=async(_,_)=>await SendReleaseToLanAsync(remote,entry);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(local);actions.Children.Add(remote);return actions;
    }

    async Task SendReleaseToLanAsync(Button owner,SourceEntry entry)
    {
        if(closing||closed)return;
        if(lanSending){Status.Text="Уже отправляем раздачу. Дождись подтверждения принимающего компьютера.";return;}
        var media=current?.Cinema==true?DownloadMetadata.EnrichMedia(current,prefs.LiveFavorites.Concat(liveItems).Concat(catalogIndex.Recent(current.Section,200)).Append(current)):null;
        lanSending=true;
        owner.IsEnabled=false;var content=owner.Content;owner.Content=new ProgressBar{IsIndeterminate=true,Width=16,Height=3};
        try
        {
            if(lanService?.IsRunning!=true)
            {
                if(!prefs.LanEnabled){Settings(this,new RoutedEventArgs());Status.Text="Включи «Загрузки на другом компьютере» в настройках на обоих устройствах, затем вернись к выбранной раздаче.";return;}
                await SetLanEnabledAsync(true);
            }
            using var requestCancellation=CancellationTokenSource.CreateLinkedTokenSource(lanLifetime.Token,reliabilityCancellation.Token);
            var device=await ChooseLanDownloadDeviceAsync(requestCancellation.Token);
            if(device==null||closing||closed)return;
            var service=lanService??throw new LanException("Устройства в сети выключены. Включи их в настройках.");
            Status.Text="Отправляем раздачу на «"+device.Name+"»…";
            requestCancellation.CancelAfter(TimeSpan.FromSeconds(45));
            var key=ReleaseSearch.Identity(entry);
            LanDownloadRequest payload;
            if(pendingLanDownload is {} previous&&previous.DeviceId==device.Id&&previous.ReleaseKey==key)payload=previous.Request;
            else
            {
                var source=await sourceClient.TorrentFile(entry,requestCancellation.Token);
                payload=await LanDownloadPayload.CreateAsync(source,media,entry,requestCancellation.Token);
                pendingLanDownload=(device.Id,key,payload);
            }
            var receipt=await service.SendDownloadAsync(device,payload,requestCancellation.Token);
            if(closing||closed)return;
            if(receipt.Accepted)
            {
                pendingLanDownload=null;Status.Text="Загрузка отправлена на «"+device.Name+"». "+receipt.Message;
                DiagnosticLog.Write("lan-download-sent",new{payload.RequestId,receipt.Accepted});
            }
            else Status.Text="Компьютер «"+device.Name+"» не принял раздачу: "+receipt.Message;
        }
        catch(OperationCanceledException)when(closing||closed){}
        catch(OperationCanceledException){Status.Text="Компьютер не подтвердил загрузку вовремя. Повтори отправку — одна и та же задача не добавится дважды.";}
        catch(Exception error)when(!closing&&!closed){Status.Text="Не удалось отправить загрузку: "+error.Message;DiagnosticLog.Write("lan-send-failed",new{Error=error.GetType().Name});}
        finally{lanSending=false;if(!closing&&!closed){owner.Content=content;owner.IsEnabled=true;}}
    }
}
